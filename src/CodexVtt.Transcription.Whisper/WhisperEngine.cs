using System.Runtime.CompilerServices;
using CodexVtt.Core.Errors;
using CodexVtt.Core.Transcription;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace CodexVtt.Transcription.Whisper;

/// <summary>
/// Motore di trascrizione locale basato su whisper.cpp tramite Whisper.net (ADR-0002).
/// Nessuna funzione di rete di Whisper.net (es. WhisperGgmlDownloader) viene usata.
/// </summary>
public sealed class WhisperEngine : ITranscriptionEngine
{
    private static readonly Lock RuntimeGate = new();
    private static bool _runtimeConfigured;

    public Task<ITranscriptionSession> LoadAsync(string modelPath, CancellationToken ct)
    {
        ConfigureRuntimeOrder();
        return Task.Run<ITranscriptionSession>(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = false });
                var runtime = RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown";
                return new WhisperSession(factory, runtime);
            }
            catch (WhisperModelLoadException ex)
            {
                throw new CodexVttException(ErrorCodes.ModelLoadFailed, "Impossibile caricare il modello.", inner: ex);
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or TypeInitializationException)
            {
                throw new CodexVttException(ErrorCodes.RuntimeUnavailable,
                    "Runtime nativo di Whisper non disponibile (VC++ Redistributable mancante o CPU non supportata).", inner: ex);
            }
            catch (OutOfMemoryException ex)
            {
                throw new CodexVttException(ErrorCodes.InsufficientMemory, "Memoria insufficiente per caricare il modello.", inner: ex);
            }
        }, ct);
    }

    /// <summary>Ordine esplicito dei runtime: CPU standard, poi CPU senza AVX.</summary>
    private static void ConfigureRuntimeOrder()
    {
        lock (RuntimeGate)
        {
            if (_runtimeConfigured)
                return;
            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
            _runtimeConfigured = true;
        }
    }

    private sealed class WhisperSession(WhisperFactory factory, string runtime) : ITranscriptionSession
    {
        public string Runtime { get; } = runtime;

        public string? DetectedLanguage { get; private set; }

        public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
            ReadOnlyMemory<float> samples,
            TranscriptionOptions options,
            IProgress<int>? progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var builder = factory.CreateBuilder()
                .WithThreads(options.Threads ?? Math.Max(1, Environment.ProcessorCount - 1))
                .WithProbabilities();
            builder = string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase)
                ? builder.WithLanguageDetection()
                : builder.WithLanguage(options.Language);
            if (options.Translate)
                builder = builder.WithTranslate();
            if (progress is not null)
                builder = builder.WithProgressHandler(progress.Report);

            await using var processor = builder.Build();
            var enumerator = processor.ProcessAsync(samples, ct).GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    SegmentData segment;
                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                            break;
                        segment = enumerator.Current;
                    }
                    catch (WhisperProcessingException ex)
                    {
                        throw new CodexVttException(ErrorCodes.TranscriptionFailed, "Errore del runtime durante la trascrizione.", inner: ex);
                    }

                    DetectedLanguage ??= segment.Language;
                    yield return new TranscriptSegment(segment.Start, segment.End, segment.Text, segment.Probability);
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }

        public ValueTask DisposeAsync()
        {
            factory.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
