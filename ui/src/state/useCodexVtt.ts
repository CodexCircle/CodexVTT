import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { BridgeCallError, type BridgeClient } from '../bridge/client';
import type {
  AppSettings,
  BenchmarkEstimate,
  BenchmarkProgress,
  BenchmarkState,
  BridgeError,
  CollectionColor,
  DownloadProgress,
  ExportFormat,
  HistoryList,
  JobProgress,
  ModelInfo,
  PickedAudio,
  PickedBatch,
  QueueState,
  Recommendation,
  SystemInfo,
  TranscriptionResult,
  UpdateStatus,
} from '../bridge/protocol';
import type { MessageKey } from '../i18n/i18n';

export type Activity = 'idle' | 'working' | 'downloading' | 'done' | 'error';

export interface UiError {
  code: string;
  message: string;
}

/** Avviso tradotto dalla UI: chiave del dizionario più parametri. */
export interface Notice {
  key: MessageKey;
  params?: Record<string, string>;
}

/** Risultato mostrato: da una trascrizione appena conclusa o riaperto dalla cronologia. */
export interface OpenResult {
  result: TranscriptionResult;
  jobId?: string;
  historyId?: string;
}

export type { QueueItem, QueueStatus } from '../bridge/protocol';

const emptyQueue: QueueState = { items: [], running: false };

function toUiError(error: unknown): UiError {
  if (error instanceof BridgeCallError) return { code: error.code, message: error.message };
  const e = error as Partial<BridgeError>;
  return { code: e?.code ?? 'internal_error', message: e?.message ?? String(error) };
}

/** Stato dell'app e azioni, sopra il client del bridge. */
export function useCodexVtt(client: BridgeClient) {
  const [system, setSystem] = useState<SystemInfo>();
  const [models, setModels] = useState<ModelInfo[]>([]);
  const [recommendation, setRecommendation] = useState<Recommendation>();
  const [settings, setSettings] = useState<AppSettings>();
  /** Valore di «Usa la scheda video» all'avvio: il motore di Whisper si sceglie una volta per processo. */
  const gpuAtStart = useRef<boolean | undefined>(undefined);
  const [audio, setAudio] = useState<PickedAudio>();
  const [jobId, setJobId] = useState<string>();
  const [progress, setProgress] = useState<JobProgress>();
  const [open, setOpen] = useState<OpenResult>();
  const [history, setHistory] = useState<HistoryList>();
  const [downloads, setDownloads] = useState<Record<string, DownloadProgress>>({});
  const [error, setError] = useState<UiError>();
  const [notice, setNotice] = useState<Notice>();
  // La coda vive nel core (prosegue anche a pagina chiusa nella versione server): qui se ne tiene solo lo stato.
  const [queueState, setQueueState] = useState<QueueState>(emptyQueue);
  const [uploading, setUploading] = useState(false);
  const [updates, setUpdates] = useState<UpdateStatus>();
  const [updateProgress, setUpdateProgress] = useState<number>();
  const [checkingUpdates, setCheckingUpdates] = useState(false);
  const [benchmark, setBenchmark] = useState<BenchmarkState>({ running: false, entries: [] });
  const [benchmarkProgress, setBenchmarkProgress] = useState<BenchmarkProgress>();
  const [estimate, setEstimate] = useState<BenchmarkEstimate>();
  const queue = queueState.items;
  const queueRunning = queueState.running;

  const guard = useCallback(async <T>(action: () => Promise<T>): Promise<T | undefined> => {
    try {
      setError(undefined);
      return await action();
    } catch (e) {
      setError(toUiError(e));
      return undefined;
    }
  }, []);

  const refreshModels = useCallback(async () => {
    const [list, rec] = await Promise.all([client.call('models.list'), client.call('models.recommend')]);
    setModels(list);
    setRecommendation(rec);
  }, [client]);

  const refreshHistory = useCallback(async () => setHistory(await client.call('history.list')), [client]);

  // All'avvio e quando il collegamento con il server torna: stato completo, compresi coda e job in corso.
  const load = useCallback(
    () =>
      guard(async () => {
        const [info, s, q, active, u, bench] = await Promise.all([
          client.call('system.info'),
          client.call('settings.get'),
          client.call('queue.get'),
          client.call('transcription.active'),
          client.call('updates.status'),
          client.call('benchmark.get').catch(() => ({ running: false, entries: [] }) as BenchmarkState),
        ]);
        setBenchmark(bench);
        setSystem(info);
        gpuAtStart.current ??= s.useGpu !== false;
        setSettings(s);
        setUpdates(u);
        setQueueState(q ?? emptyQueue);
        if (q?.running && q.progress) setProgress(q.progress);
        if (active) {
          setAudio(active.audio);
          setJobId(active.jobId);
          setProgress(active.progress ?? { jobId: active.jobId, status: 'queued', percent: 0, processed: 0, total: active.audio.info.duration });
        }
        await Promise.all([refreshModels(), refreshHistory()]);
      }),
    [client, guard, refreshModels, refreshHistory],
  );

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    const report = (e: unknown) => setError(toUiError(e));
    const offs = [
      client.on('models.changed', () => void refreshModels().catch(report)),
      client.on('history.changed', () => void refreshHistory().catch(report)),
      client.on('history.saveFailed', ({ error: e }) => {
        setError(e);
        // Il core toglie la spunta «cartella testata» quando il salvataggio fallisce.
        void client.call('settings.get').then(setSettings).catch(report);
      }),
      client.on('models.downloadProgress', (p) => setDownloads((d) => ({ ...d, [p.modelId]: p }))),
      client.on('models.downloadCompleted', (m) => {
        setDownloads(({ [m.id]: _, ...rest }) => rest);
        setNotice({ key: 'modelInstalled', params: { model: m.displayName } });
      }),
      client.on('models.downloadFailed', ({ modelId, error: e }) => {
        setDownloads(({ [modelId]: _, ...rest }) => rest);
        if (e.code !== 'cancelled') setError(e);
      }),
      client.on('transcription.progress', (p) => setProgress(p)),
      // Gli eventi transcription.* riguardano solo le trascrizioni singole: la coda arriva con queue.changed.
      client.on('transcription.completed', ({ jobId: finished, result, historyId }) => {
        setOpen({ result, jobId: finished, historyId });
        if (historyId) setNotice({ key: 'savedToHistory' });
        setJobId(undefined);
      }),
      client.on('transcription.failed', ({ error: e }) => {
        setError(e);
        setJobId(undefined);
        setProgress(undefined);
      }),
      client.on('transcription.cancelled', () => {
        setJobId(undefined);
        setProgress(undefined);
        setNotice({ key: 'cancelled' });
      }),
      client.on('updates.changed', (u) => setUpdates(u)),
      client.on('updates.progress', ({ percent }) => setUpdateProgress(percent)),
      client.on('updates.failed', (e) => {
        setUpdateProgress(undefined);
        setError(e);
      }),
      client.on('benchmark.progress', (p) => setBenchmarkProgress(p)),
      client.on('benchmark.completed', ({ snapshot }) => {
        setBenchmark(snapshot);
        // Il modello consigliato segue le misure appena fatte.
        void refreshModels().catch(report);
        setBenchmarkProgress(undefined);
        setNotice({ key: 'benchDone' });
      }),
      client.on('benchmark.cancelled', ({ snapshot }) => {
        setBenchmark(snapshot);
        setBenchmarkProgress(undefined);
        setNotice({ key: 'benchCancelled' });
      }),
      client.on('benchmark.failed', ({ error: e, snapshot }) => {
        setBenchmark(snapshot);
        setBenchmarkProgress(undefined);
        setError(e);
      }),
      client.on('queue.changed', (q) => setQueueState(q)),
      client.on('queue.finished', ({ done, failed }) => {
        setProgress(undefined);
        setNotice({ key: 'batchDone', params: { done: String(done), failed: String(failed) } });
      }),
      client.on('connection.lost', () => setError({ code: 'connection_lost', message: 'Collegamento interrotto.' })),
      client.on('connection.restored', () => void load()),
    ];
    return () => offs.forEach((off) => off());
  }, [client, refreshModels, refreshHistory, load]);

  const readyModels = useMemo(() => models.filter((m) => m.state === 'ready'), [models]);
  const activeModel = useMemo(
    () => readyModels.find((m) => m.id === settings?.selectedModelId) ?? readyModels[0],
    [readyModels, settings?.selectedModelId],
  );

  // Stima del tempo per i file ancora da trascrivere, dall'ultima misura valida del Test bench per il modello scelto.
  const pendingDurations = useMemo(() => {
    const queued = queueState.items.filter((i) => i.status === 'queued' && i.info).map((i) => i.info!.duration);
    return queued.length > 0 ? queued : audio ? [audio.info.duration] : [];
  }, [queueState.items, audio]);
  const pendingKey = pendingDurations.join(',');
  const estimateModelId = activeModel?.id;
  useEffect(() => {
    let current = true;
    if (!estimateModelId || pendingDurations.length === 0) {
      setEstimate(undefined);
      return;
    }
    client
      .call('benchmark.estimate', { modelId: estimateModelId, durations: pendingDurations })
      .then((e) => current && setEstimate(e))
      .catch(() => current && setEstimate(undefined));
    return () => {
      current = false;
    };
    // pendingKey rappresenta il contenuto di pendingDurations; benchmark.entries rifà la stima dopo un nuovo test.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client, estimateModelId, pendingKey, benchmark.entries]);

  // Nell'app il dialog nativo; nel browser (versione server) il selettore del browser e il caricamento sul server.
  const pickBatch = useCallback(async (): Promise<PickedBatch | null> => {
    if (!client.files) return client.call('audio.pickMany');
    setUploading(true);
    try {
      return await client.files.pickAudio(system?.supportedAudioExtensions ?? []);
    } finally {
      setUploading(false);
    }
  }, [client, system?.supportedAudioExtensions]);

  const addBatch = useCallback(
    (batch: PickedBatch, audioIds: string[] = []) =>
      client.call('queue.add', { audioIds: [...audioIds, ...batch.picked.map((p) => p.audioId)], failed: batch.failed }).then(setQueueState),
    [client],
  );

  const activity: Activity = error
    ? 'error'
    : jobId || queueRunning
      ? 'working'
      : Object.keys(downloads).length > 0
        ? 'downloading'
        : open
          ? 'done'
          : 'idle';

  const checkUpdates = useCallback(
    async (silent = false) => {
      setCheckingUpdates(true);
      try {
        const status = await client.call('updates.check');
        setUpdates(status);
        if (!silent && status.state === 'upToDate') setNotice({ key: 'updatesUpToDate', params: { version: status.currentVersion } });
      } catch (e) {
        // Il controllo automatico all'avvio non disturba se manca la rete; quello chiesto dall'utente sì.
        if (!silent) setError(toUiError(e));
      } finally {
        setCheckingUpdates(false);
      }
    },
    [client],
  );

  // Controllo all'avvio solo se l'utente l'ha attivato (ADR-0008) e la copia è installata.
  const autoCheck = settings?.autoCheckUpdates === true && updates?.state === 'upToDate';
  const [autoChecked, setAutoChecked] = useState(false);
  useEffect(() => {
    if (autoCheck && !autoChecked) {
      setAutoChecked(true);
      void checkUpdates(true);
    }
  }, [autoCheck, autoChecked, checkUpdates]);

  const saveSettings = useCallback(
    (next: AppSettings) =>
      guard(async () => {
        setSettings(await client.call('settings.set', next));
      }),
    [client, guard],
  );

  return {
    system,
    models,
    readyModels,
    activeModel,
    recommendation,
    settings,
    gpuAtStart: gpuAtStart.current,
    audio,
    queue,
    queueRunning,
    uploading,
    updates,
    updateProgress,
    checkingUpdates,
    checkUpdates: () => checkUpdates(false),
    applyUpdate: () =>
      guard(async () => {
        setUpdateProgress(0);
        await client.call('updates.apply');
      }),
    benchmark,
    benchmarkProgress,
    /** Stima del tempo dei file in attesa, o `basis: none` se manca una misura valida per il modello scelto. */
    estimate,
    pendingDurations,
    benchmarkReference: () => guard(() => client.call('benchmark.reference')),
    /** Con `useReference` scarica (se manca) il file ufficiale; altrimenti si sceglie un file proprio. */
    startBenchmark: (useReference = false) =>
      guard(async () => {
        const started = await client.call('benchmark.start', useReference ? { useReference: true } : undefined);
        if (started) setBenchmark((b) => ({ ...b, running: true }));
      }),
    cancelBenchmark: () => guard(() => client.call('benchmark.cancel')),
    /** false nella versione server: niente dialog né Esplora file sul PC del core. */
    desktop: system?.host !== 'server',
    jobId,
    progress,
    open,
    history,
    downloads,
    error,
    notice,
    activity,
    dismissError: () => setError(undefined),
    dismissNotice: () => setNotice(undefined),
    // Un solo file scelto resta una trascrizione singola; più file formano la coda.
    pickAudio: () =>
      guard(async () => {
        const batch = await pickBatch();
        if (!batch) return;
        setOpen(undefined);
        setProgress(undefined);
        if (!queueRunning) setQueueState(await client.call('queue.clear'));
        if (batch.picked.length === 1 && batch.failed.length === 0) {
          setAudio(batch.picked[0]);
        } else {
          setAudio(undefined);
          await addBatch(batch);
        }
      }),
    addToQueue: () =>
      guard(async () => {
        const batch = await pickBatch();
        if (!batch) return;
        // Il file singolo già scelto entra in coda per primo.
        const first = queue.length === 0 && audio ? [audio.audioId] : [];
        setAudio(undefined);
        await addBatch(batch, first);
      }),
    removeFromQueue: (key: string) => guard(async () => setQueueState(await client.call('queue.remove', { key }))),
    clearQueue: () => guard(async () => setQueueState(await client.call('queue.clear'))),
    startQueue: (language: string) =>
      guard(async () => {
        setOpen(undefined);
        setQueueState(await client.call('queue.start', { modelId: activeModel?.id, language }));
      }),
    openQueueItem: (key: string) =>
      guard(async () => {
        const item = queue.find((i) => i.key === key);
        if (item?.historyId) setOpen({ result: await client.call('history.get', { id: item.historyId }), historyId: item.historyId });
        else if (item?.jobId) {
          const job = await client.call('transcription.get', { jobId: item.jobId });
          if (job.result) setOpen({ result: job.result, jobId: item.jobId });
        }
      }),
    start: (language: string) =>
      guard(async () => {
        if (!audio || !activeModel) return;
        setOpen(undefined);
        setProgress({ jobId: '', status: 'queued', percent: 0, processed: 0, total: audio.info.duration });
        const started = await client.call('transcription.start', {
          audioId: audio.audioId,
          modelId: activeModel.id,
          language,
        });
        setJobId(started.jobId);
        return started;
      }).then((r) => {
        if (r === undefined) setProgress(undefined);
      }),
    // Pausa vera: il motore si ferma e la CPU torna libera; con una coda si ferma il file in corso, gli altri aspettano.
    pause: () =>
      guard(async () => {
        const target = jobId ?? queueState.jobId;
        if (target) await client.call('transcription.pause', { jobId: target });
      }),
    resume: () =>
      guard(async () => {
        const target = jobId ?? queueState.jobId;
        if (target) await client.call('transcription.resume', { jobId: target });
      }),
    // Annullare ferma la coda; il file interrotto resta in coda e riparte con "Avvia coda".
    cancel: () =>
      guard(async () => {
        if (queueRunning) {
          setQueueState(await client.call('queue.stop'));
          setProgress(undefined);
          setNotice({ key: 'cancelled' });
        } else if (jobId) {
          await client.call('transcription.cancel', { jobId });
        }
      }),
    exportAs: (format: ExportFormat) =>
      guard(async () => {
        if (!open) return;
        const request = {
          ...(open.historyId ? { historyId: open.historyId } : { jobId: open.jobId }),
          format,
          includeHeader: settings?.exportIncludeHeader,
        };
        if (client.files) {
          client.files.download(request);
          setNotice({ key: 'downloadStarted', params: { file: `${open.result.audioFileName.replace(/\.[^.]+$/, '')}.${format}` } });
          return;
        }
        const saved = await client.call('export.save', request);
        if (saved) setNotice({ key: 'saved', params: { file: saved.fileName } });
      }),
    openFromHistory: (id: string) =>
      guard(async () => {
        const result = await client.call('history.get', { id });
        setOpen({ result, historyId: id });
        return result;
      }),
    deleteFromHistory: (id: string) =>
      guard(async () => {
        await client.call('history.delete', { id });
        if (open?.historyId === id) setOpen(undefined);
        setNotice({ key: 'deleted' });
        await refreshHistory();
      }),
    renameEntry: (id: string, title: string) =>
      guard(async () => {
        await client.call('history.update', { id, title: title.trim() || null });
        await refreshHistory();
      }),
    /** Salva nella cronologia il testo corretto; restituisce true se è stato scritto. */
    saveEdits: async (id: string, segments: { start: number; end: number; text: string }[]): Promise<boolean> => {
      const saved = await guard(async () => {
        await client.call('history.saveEdits', { id, segments });
        const result = await client.call('history.get', { id });
        setOpen((o) => (o?.historyId === id ? { ...o, result } : o));
        setNotice({ key: 'savedEdits' });
        await refreshHistory();
        return true;
      });
      return saved === true;
    },
    moveToCollection: (id: string, collectionId: string | null) =>
      guard(async () => {
        await client.call('history.update', { id, collectionId });
        await refreshHistory();
      }),
    saveCollection: (collection: { id?: string; name: string; color: CollectionColor }) =>
      guard(async () => {
        const saved = await client.call('collections.save', collection);
        await refreshHistory();
        return saved;
      }),
    deleteCollection: (id: string) =>
      guard(async () => {
        await client.call('collections.delete', { id });
        await refreshHistory();
      }),
    reorderCollection: (id: string, entryIds: string[]) =>
      guard(async () => {
        // Aggiornamento ottimistico: l'ordine cambia subito, poi si allinea al core.
        setHistory((h) => h && { ...h, collections: h.collections.map((c) => (c.id === id ? { ...c, entryIds } : c)) });
        await client.call('collections.reorder', { id, entryIds });
        await refreshHistory();
      }),
    openHistoryFolder: () => guard(() => client.call('history.openFolder')),
    openLogsFolder: () => guard(() => client.call('diagnostics.openLogs')),
    /** Apre la selezione della cartella e restituisce quella scelta, senza salvarla. */
    pickHistoryFolder: () => guard(async () => (await client.call('history.pickFolder'))?.directory),
    testHistoryFolder: (directory: string) => guard(() => client.call('history.testFolder', { directory })),
    /** Adotta la cartella (il core rifà il test): se fallisce resta la precedente. */
    applyHistoryFolder: (directory: string) =>
      guard(async () => {
        const updated = await client.call('history.setFolder', { directory });
        setSettings(updated);
        await refreshHistory();
        return true;
      }),
    download: (modelId: string) => guard(() => client.call('models.download', { modelId })),
    cancelDownload: (modelId: string) => guard(() => client.call('models.cancelDownload', { modelId })),
    importModel: () =>
      guard(async () => {
        const imported = await client.call('models.import');
        if (imported) setNotice({ key: 'modelImported', params: { model: imported.displayName } });
        await refreshModels();
      }),
    removeModel: (modelId: string) => guard(() => client.call('models.remove', { modelId })),
    verifyModel: (modelId: string) =>
      guard(async () => {
        await client.call('models.verify', { modelId });
        await refreshModels();
      }),
    selectModel: (modelId: string) => (settings ? saveSettings({ ...settings, selectedModelId: modelId }) : undefined),
    saveSettings,
  };
}

export type CodexVttState = ReturnType<typeof useCodexVtt>;
