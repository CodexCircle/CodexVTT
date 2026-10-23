using CodexVtt.Core.Audio;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Transcription;

namespace CodexVtt.Bridge;

/// <summary>
/// Coda di trascrizione (batch) nel core: i file partono uno dopo l'altro anche senza UI collegata,
/// così nella versione server la coda prosegue con la pagina chiusa e chi la riapre ne ritrova lo stato.
/// </summary>
public sealed partial class BridgeDispatcher
{
    private readonly Lock _queueLock = new();
    private readonly List<QueueItem> _queue = [];
    private bool _queueRunning;
    private string? _queueJobId;
    private string? _queueModelId;
    private string? _queueLanguage;
    private int _queueSeq;
    private JobProgress? _lastProgress;

    private QueueView QueueSnapshot()
    {
        lock (_queueLock)
            return Snapshot();
    }

    private QueueView QueueAdd(QueueAddRequest request)
    {
        // Prima si leggono tutte le informazioni: un audioId sconosciuto rifiuta la richiesta senza aggiungere metà dei file.
        var infos = (request.AudioIds ?? []).Select(id => (Id: id, Info: _audio.Info(id))).ToList();
        lock (_queueLock)
        {
            foreach (var (id, info) in infos)
                _queue.Add(new QueueItem(NextKey(), info.FileName, id, info, QueueItemStatus.Queued));
            foreach (var failed in request.Failed ?? [])
                _queue.Add(new QueueItem(NextKey(), failed.FileName, null, null, QueueItemStatus.Failed, failed.Error));
            return Changed();
        }
    }

    private QueueView QueueRemove(string key)
    {
        lock (_queueLock)
        {
            _queue.RemoveAll(i => i.Key == key && i.Status != QueueItemStatus.Working);
            return Changed();
        }
    }

    private QueueView QueueClear()
    {
        lock (_queueLock)
        {
            if (!_queueRunning)
                _queue.RemoveAll(i => i.Status != QueueItemStatus.Working);
            return Changed();
        }
    }

    private QueueView QueueStart(QueueStartRequest request)
    {
        var modelId = request.ModelId ?? _settings.Load().SelectedModelId
            ?? throw new CodexVttException(ErrorCodes.ModelNotInstalled, "Nessun modello selezionato.");
        RequireModel(modelId);
        lock (_queueLock)
        {
            _queueModelId = modelId;
            _queueLanguage = request.Language ?? _settings.Load().Language;
            // I file non riusciti dopo l'avvio (hanno un audioId) si riprovano; quelli illeggibili restano com'è.
            for (var i = 0; i < _queue.Count; i++)
            {
                if (_queue[i] is { Status: QueueItemStatus.Failed, AudioId: not null } failed)
                    _queue[i] = failed with { Status = QueueItemStatus.Queued, Error = null };
            }

            _queueRunning = true;
            Pump();
            return Changed();
        }
    }

    /// <summary>Ferma la coda: il file in corso si annulla e torna in coda, riparte con il prossimo avvio.</summary>
    private QueueView QueueStop()
    {
        lock (_queueLock)
        {
            _queueRunning = false;
            if (_queueJobId is { } jobId)
                _transcription.Cancel(jobId);
            return Changed();
        }
    }

    private object? ActiveTranscription()
    {
        var job = _transcription.Current;
        if (job is null || IsQueueJob(job.JobId))
            return null;
        var progress = _lastProgress is { } last && last.JobId == job.JobId ? last : null;
        return new { job.JobId, job.ModelId, Audio = new PickedAudio(job.AudioId, _audio.Info(job.AudioId)), Progress = progress };
    }

    private bool IsQueueJob(string jobId)
    {
        lock (_queueLock)
            return _queueJobId == jobId;
    }

    private void OnQueueJobFinished(TranscriptionJob job, string? historyId)
    {
        lock (_queueLock)
        {
            if (_queueJobId == job.JobId)
            {
                _queueJobId = null;
                var index = _queue.FindIndex(i => i.JobId == job.JobId);
                if (index >= 0)
                {
                    _queue[index] = job.Status switch
                    {
                        JobStatus.Completed => _queue[index] with { Status = QueueItemStatus.Done, HistoryId = historyId },
                        // Annullato: il file resta in coda e la coda si ferma (QueueStop o annullamento del job).
                        JobStatus.Cancelled => _queue[index] with { Status = QueueItemStatus.Queued, JobId = null },
                        _ => _queue[index] with { Status = QueueItemStatus.Failed, Error = JobError(job) },
                    };
                }

                if (job.Status == JobStatus.Cancelled)
                    _queueRunning = false;
            }
            else if (!_queueRunning)
            {
                return;
            }

            Pump();
            Changed();
        }
    }

    /// <summary>Avvia il prossimo file in coda se il core è libero. Va chiamato con il lock preso.</summary>
    private void Pump()
    {
        if (!_queueRunning || _queueJobId is not null)
            return;
        while (true)
        {
            var index = _queue.FindIndex(i => i.Status == QueueItemStatus.Queued && i.AudioId is not null);
            if (index < 0)
            {
                _queueRunning = false;
                Emit("queue.finished", new
                {
                    Done = _queue.Count(i => i.Status == QueueItemStatus.Done),
                    Failed = _queue.Count(i => i.Status == QueueItemStatus.Failed),
                });
                return;
            }

            var item = _queue[index];
            try
            {
                // V1: nessuna traduzione (issue #17), come per le trascrizioni singole.
                var job = _transcription.Start(item.AudioId!, _queueModelId!, new TranscriptionOptions(_queueLanguage ?? "auto", Translate: false, Threads: ThreadLimit()));
                _queueJobId = job.JobId;
                _queue[index] = item with { Status = QueueItemStatus.Working, Error = null, JobId = job.JobId };
                return;
            }
            catch (CodexVttException ex) when (ex.Code == ErrorCodes.TranscriptionBusy)
            {
                // C'è una trascrizione singola in corso: la coda riparte quando finisce.
                return;
            }
            catch (CodexVttException ex)
            {
                _queue[index] = item with { Status = QueueItemStatus.Failed, Error = ToError(ex) };
            }
        }
    }

    private QueueView Changed()
    {
        var view = Snapshot();
        Emit("queue.changed", view);
        return view;
    }

    // Con l'avanzamento del file in corso, così chi riapre la pagina ritrova subito percentuale, pausa e tempo rimanente.
    private QueueView Snapshot() =>
        new([.. _queue], _queueRunning, _queueJobId, _lastProgress is { } last && last.JobId == _queueJobId ? last : null);

    private string NextKey() => $"q{++_queueSeq}";
}

public enum QueueItemStatus
{
    Queued,
    Working,
    Done,
    Failed,
}

/// <summary>Un file della coda. I file illeggibili entrano già come Failed, senza audioId.</summary>
public sealed record QueueItem(
    string Key,
    string FileName,
    string? AudioId,
    AudioInfo? Info,
    QueueItemStatus Status,
    BridgeError? Error = null,
    string? HistoryId = null,
    string? JobId = null);

public sealed record QueueView(IReadOnlyList<QueueItem> Items, bool Running, string? JobId, JobProgress? Progress = null);

public sealed record QueueAddRequest(IReadOnlyList<string>? AudioIds, IReadOnlyList<FailedAudio>? Failed);

public sealed record QueueItemRef(string Key);

public sealed record QueueStartRequest(string? ModelId, string? Language);
