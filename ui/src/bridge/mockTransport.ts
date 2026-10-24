import type { Transport } from './client';
import type {
  BenchmarkEntry,
  BenchmarkProgress,
  BenchmarkReliability,
  BenchmarkScenario,
  BenchmarkState,
  AppSettings,
  Collection,
  CollectionColor,
  BridgeRequest,
  HistoryEntry,
  ModelInfo,
  PickedAudio,
  QueueItem,
  QueueState,
  SystemInfo,
  UpdateStatus,
  TranscriptionResult,
} from './protocol';

const GB = 1_000_000_000;

export interface MockOptions {
  /** Ritardo tra gli eventi simulati, in millisecondi (0 nei test). */
  tick?: number;
  /** Comandi che falliscono con il codice indicato. */
  failures?: Record<string, string>;
  /** Simula l'annullamento dei dialog di sistema. */
  cancelDialogs?: boolean;
  /** Il test della cartella di lavoro fallisce con questo codice (not_found, cannot_write…). */
  folderTestError?: string;
  installed?: string[];
  uiLanguage?: 'it' | 'en';
  /** Punti percentuali per ogni passo della trascrizione simulata (predefinito 25). */
  jobStep?: number;
  /** Voci di cronologia già presenti. */
  history?: TranscriptionResult[];
  /** Nomi personalizzati, per nome del file audio. */
  titles?: Record<string, string>;
  /** File restituiti dalla selezione multipla (predefinito: solo riunione.mp3). */
  batchFiles?: string[];
  /** File della selezione multipla che il core non riesce a leggere. */
  batchUnreadable?: string[];
  /** Raccolte iniziali: i file sono indicati per nome del file audio, nell'ordine voluto. */
  collections?: { name: string; color: CollectionColor; files: string[] }[];
  /** Simula la versione server (UI nel browser). */
  host?: 'desktop' | 'server';
  /** Schede video rilevate (predefinito: nessuna). */
  gpus?: string[];
  /** Memoria video (GB) della scheda dedicata: con `gpus` simula Whisper sulla scheda video. */
  gpuVramGb?: number;
  /** Misure del Test bench già presenti, per id del modello (velocità in multipli del tempo reale). */
  benchmark?: Record<string, number>;
  /** Stato iniziale degli aggiornamenti (predefinito: installata e aggiornata). */
  updates?: UpdateStatus;
}

/**
 * Core simulato per sviluppare e testare la UI senza la shell MAUI (ADR-0001).
 * Non fa alcuna richiesta di rete.
 */
export function createMockTransport(options: MockOptions = {}): Transport {
  const tick = options.tick ?? 120;
  const handlers = new Set<(message: string) => void>();
  const emit = (message: object) => {
    const raw = JSON.stringify(message);
    queueMicrotask(() => handlers.forEach((h) => h(raw)));
  };
  const event = (name: string, payload?: unknown) => emit({ v: 1, type: 'event', event: name, payload });
  const later = (fn: () => void, steps = 1) => setTimeout(fn, tick * steps);

  const gpuOn = (options.gpus?.length ?? 0) > 0 && options.gpuVramGb !== undefined;
  const slow: ModelInfo['issues'] = gpuOn ? [] : ['tooSlow'];
  const models: ModelInfo[] = [
    model('tiny', 'Tiny', 77_691_713, 0.27, 1),
    model('base', 'Base', 147_951_465, 0.39, 2),
    model('small', 'Small', 487_601_967, 0.85, 6),
    model('large-v3-turbo-q5_0', 'Large v3 Turbo (q5_0)', 574_041_195, 1.2, 10),
    model('medium', 'Medium', 1_533_763_059, 2.1, 16, slow),
    model('large-v3', 'Large v3', 3_095_033_483, 3.9, 32, slow),
    {
      ...model('parakeet-tdt-0.6b-v3-int8', 'Parakeet TDT 0.6B v3 (int8)', 670_478_772, 1.5, 2),
      engine: 'sherpa-transducer',
      preview: true,
      quantization: 'int8',
      license: 'CC-BY-4.0',
      attribution: 'Parakeet TDT 0.6B v3 by NVIDIA, licensed under CC-BY-4.0. Converted to int8 ONNX for sherpa-onnx.',
      accuracy: { language: 'it', percent: 97, metric: '100 - WER FLEURS italiano', source: 'Scheda del modello nvidia/parakeet-tdt-0.6b-v3', measuredByProject: false },
    },
    {
      ...model('canary-1b-v2-int8', 'Canary 1B v2 (int8)', 1_385_592_870, 2.6, 16),
      engine: 'sherpa-canary',
      preview: true,
      requiresLanguage: true,
      quantization: 'int8',
      license: 'CC-BY-4.0',
      attribution: 'Canary-1B-v2 by NVIDIA, licensed under CC-BY-4.0. Converted to int8 ONNX for sherpa-onnx.',
    },
  ];
  for (const id of options.installed ?? []) {
    const m = models.find((x) => x.id === id);
    if (m) {
      m.state = 'ready';
      m.sizeOnDiskBytes = m.sizeBytes;
    }
  }

  let settings: AppSettings = { language: 'auto', showMascot: true, exportIncludeHeader: false, uiLanguage: options.uiLanguage };
  // Una misura per modello e brano (chiave `modello|brano`); `|` vuoto = file scelto dall'utente, senza testo di riferimento.
  const benchEntries = new Map<string, BenchmarkEntry>();
  const benchEntry = (modelId: string, speed: number, clip?: { id: string; title: string; language: string; scenario: BenchmarkScenario; accuracy: number }): BenchmarkEntry => ({
    modelId,
    runtime: 'Cpu',
    threads: 8,
    audioSeconds: 90,
    loadSeconds: 2,
    speed,
    measuredAt: '2026-10-24T10:00:00Z',
    ...(clip === undefined ? {} : { accuracy: clip.accuracy, clipId: clip.id, clipTitle: clip.title, language: clip.language, scenario: clip.scenario }),
    stale: 'none',
  });
  for (const [id, speed] of Object.entries(options.benchmark ?? {})) benchEntries.set(`${id}|`, benchEntry(id, speed));
  // Brani ufficiali simulati: due lingue, ognuna pulita e con rumore.
  const mockClips: { id: string; language: string; scenario: BenchmarkScenario; title: string; seconds: number }[] = [
    { id: 'it-clean-1', language: 'it', scenario: 'clean', title: 'Le avventure di Pinocchio, capitolo 6 (LibriVox)', seconds: 220 },
    { id: 'it-noisy-1', language: 'it', scenario: 'noisy', title: 'Le avventure di Pinocchio, capitolo 6, con rumore', seconds: 220 },
    { id: 'en-clean-1', language: 'en', scenario: 'clean', title: 'The Wonderful Wizard of Oz, chapter 21 (LibriVox)', seconds: 334 },
    { id: 'en-noisy-1', language: 'en', scenario: 'noisy', title: 'The Wonderful Wizard of Oz, chapter 21, with noise', seconds: 334 },
  ];
  let referenceCached = false;
  let benchRunning = false;
  let benchCancel = false;
  const mean = (values: number[]) => (values.length === 0 ? undefined : Math.round((values.reduce((a, b) => a + b, 0) / values.length) * 10) / 10);
  const reliability = (): BenchmarkReliability[] => {
    const result: BenchmarkReliability[] = [];
    for (const modelId of new Set([...benchEntries.values()].map((e) => e.modelId))) {
      for (const language of ['it', 'en']) {
        const of = (scenario: BenchmarkScenario) =>
          mean([...benchEntries.values()].filter((e) => e.modelId === modelId && e.language === language && e.scenario === scenario && e.accuracy !== undefined).map((e) => e.accuracy!));
        const clean = of('clean');
        const noisy = of('noisy');
        if (clean === undefined && noisy === undefined) continue;
        const overall = mean([clean, noisy].filter((v): v is number => v !== undefined));
        result.push({ modelId, language, ...(clean === undefined ? {} : { clean }), ...(noisy === undefined ? {} : { noisy }), ...(overall === undefined ? {} : { overall }) });
      }
    }
    return result;
  };
  const benchSnapshot = (): BenchmarkState => ({ running: benchRunning, entries: [...benchEntries.values()], reliability: reliability() });
  let historyDirectory = 'C:\\Users\\utente\\Documents\\CodexVTT\\history';
  const history = new Map<string, TranscriptionResult>();
  const saveHistory = (result: TranscriptionResult) => {
    const base = `${result.completedAt.slice(0, 19).replace('T', ' ').replace(/:/g, '.')} - ${result.audioFileName.replace(/\.[^.]+$/, '')}`;
    let id = base;
    for (let i = 2; history.has(id); i++) id = `${base} (${i})`;
    history.set(id, result);
    return id;
  };
  for (const r of options.history ?? []) saveHistory(r);
  const titles = new Map<string, string>();
  const idOf = (file: string) => [...history.entries()].find(([, r]) => r.audioFileName === file)?.[0];
  for (const [file, title] of Object.entries(options.titles ?? {})) {
    const id = idOf(file);
    if (id) titles.set(id, title);
  }
  let collectionSeq = 0;
  const collections: Collection[] = (options.collections ?? []).map((c) => ({
    id: `col-${++collectionSeq}`,
    name: c.name,
    color: c.color,
    entryIds: c.files.map(idOf).filter((id): id is string => Boolean(id)),
  }));
  const collectionOf = (id: string) => collections.find((c) => c.entryIds.includes(id));
  const findCollection = (id: unknown) => {
    const c = collections.find((x) => x.id === id);
    if (!c) throw { code: 'invalid_request', message: 'Raccolta inesistente.' };
    return c;
  };
  const entry = (id: string, r: TranscriptionResult): HistoryEntry => ({
    id,
    audioFileName: r.audioFileName,
    completedAt: r.completedAt,
    audioDuration: r.audioDuration,
    modelDisplayName: r.model.displayName,
    detectedLanguage: r.detectedLanguage,
    preview: r.text.length > 160 ? `${r.text.slice(0, 160)}…` : r.text,
    textFileName: `${id}.txt`,
    title: titles.get(id) ?? null,
    collectionId: collectionOf(id)?.id ?? null,
  });
  const cancelled = new Set<string>();
  const paused = new Set<string>();
  let job = 0;
  let audioSeq = 0;
  const audioNames = new Map<string, string>([['audio-1', 'riunione.mp3']]);
  const audioInfo = (fileName: string) => ({
    fileName,
    format: fileName.split('.').pop() ?? 'mp3',
    duration: 754.2,
    sampleRate: 44100,
    channels: 2,
    sizeBytes: 12_100_000,
  });
  const results = new Map<string, TranscriptionResult>();

  // Coda nel core simulato, come in BridgeDispatcher.Queue.cs.
  const queue: QueueState = { items: [], running: false };
  let queueSeq = 0;
  let queueOptions: { modelId: string; language: string } = { modelId: 'base', language: 'auto' };
  const queueView = (): QueueState => ({ items: queue.items.map((i) => ({ ...i })), running: queue.running, jobId: queue.jobId });
  const queueChanged = () => {
    const view = queueView();
    event('queue.changed', view);
    return view;
  };
  const updateQueueItem = (key: string, patch: Partial<QueueItem>) => {
    queue.items = queue.items.map((i) => (i.key === key ? { ...i, ...patch } : i));
  };
  const pump = () => {
    if (!queue.running || queue.jobId) return;
    const next = queue.items.find((i) => i.status === 'queued' && i.audioId);
    if (!next) {
      queue.running = false;
      event('queue.finished', {
        done: queue.items.filter((i) => i.status === 'done').length,
        failed: queue.items.filter((i) => i.status === 'failed').length,
      });
      return;
    }
    const jobId = runJob(next.audioId!, queueOptions.modelId, (status, historyId) => {
      if (queue.jobId !== jobId) return;
      queue.jobId = undefined;
      updateQueueItem(next.key, status === 'completed' ? { status: 'done', historyId } : { status: 'queued', jobId: undefined });
      pump();
      queueChanged();
    });
    queue.jobId = jobId;
    updateQueueItem(next.key, { status: 'working', error: undefined, jobId });
  };

  const system: SystemInfo = {
    appVersion: '0.1.0-mock',
    protocolVersion: 1,
    processing: 'local',
    hardware: {
      operatingSystem: 'Windows 11 (simulato)',
      architecture: 'X64',
      logicalCores: 8,
      totalMemoryBytes: 16 * 2 ** 30,
      availableMemoryBytes: 9 * GB,
      cpuFeatures: 'Avx, Avx2, Fma, F16C',
      freeDiskBytes: 120 * GB,
      gpus: options.gpus ?? [],
      ...(gpuOn
        ? {
            gpuDetails: (options.gpus ?? []).map((name) => ({ name, kind: 'dedicata' as const, vramBytes: options.gpuVramGb! * GB })),
            gpuAcceleration: true,
            gpuVramBytes: options.gpuVramGb! * GB,
          }
        : {}),
    },
    supportsStandardRuntime: true,
    supportedAudioExtensions: ['.wav', '.mp3', '.m4a', '.aac', '.wma', '.flac', '.ogg', '.opus'],
    host: options.host ?? 'desktop',
  };

  /** Trascrizione simulata: progresso a passi, poi risultato salvato in cronologia. */
  const runJob = (
    audioId: string,
    modelId: string,
    done: (status: 'completed' | 'cancelled', historyId?: string, result?: TranscriptionResult) => void,
  ) => {
    const audioFileName = audioNames.get(audioId) ?? 'riunione.mp3';
    const jobId = `job-${++job}`;
    const m = models.find((x) => x.id === modelId);
    if (!m || m.state !== 'ready') {
      throw { code: 'model_not_installed', message: 'Il modello non è installato.' };
    }
    let percent = 0;
    let announcedPause = false;
    const advance = () => {
      if (cancelled.has(jobId)) {
        paused.delete(jobId);
        done('cancelled');
        return;
      }
      if (paused.has(jobId)) {
        // In pausa non avanza nulla: lo dice una volta e poi aspetta la ripresa.
        if (!announcedPause) {
          announcedPause = true;
          event('transcription.progress', { jobId, status: 'paused', percent: Math.min(percent, 99), processed: 754.2 * (percent / 100), total: 754.2 });
        }
        later(advance);
        return;
      }
      announcedPause = false;
      percent += options.jobStep ?? 25;
      const processed = 754.2 * (percent / 100);
      // Come il core: nessuna stima al primo avanzamento, poi la velocità misurata (qui 2× il tempo reale).
      const estimate = percent > (options.jobStep ?? 25) ? { speed: 2, remaining: Math.max(0, (754.2 - processed) / 2) } : {};
      event('transcription.progress', { jobId, status: 'transcribing', percent: Math.min(percent, 99), processed, total: 754.2, ...estimate });
      if (percent < 100) later(advance);
      else {
        const result: TranscriptionResult = {
          audioFileName,
          audioDuration: 754.2,
          detectedLanguage: 'it',
          model: { modelId: m.id, displayName: m.displayName, file: m.file, sha256: 'e3b0c44298fc1c149afbf4c8996fb924', runtime: 'Cpu' },
          segments: [
            { start: 0, end: 4.2, text: 'Buongiorno a tutti e grazie per essere qui.' },
            { start: 4.2, end: 9.8, text: 'Oggi parliamo del rilascio della prima versione di CodexVTT.' },
            { start: 9.8, end: 14.1, text: 'Tutto il lavoro avviene in locale, sul vostro computer.' },
          ],
          elapsed: 96.4,
          completedAt: new Date().toISOString(),
          text: 'Buongiorno a tutti e grazie per essere qui. Oggi parliamo del rilascio della prima versione di CodexVTT. Tutto il lavoro avviene in locale, sul vostro computer.',
        };
        results.set(jobId, result);
        event('transcription.progress', { jobId, status: 'completed', percent: 100, processed: 754.2, total: 754.2 });
        const historyId = saveHistory(result);
        done('completed', historyId, result);
        event('history.changed');
      }
    };
    later(advance);
    return jobId;
  };

  let updates: UpdateStatus = options.updates ?? { state: 'upToDate', currentVersion: '0.1.0' };
  const handle = (request: BridgeRequest): unknown => {
    const payload = (request.payload ?? {}) as Record<string, unknown>;
    switch (request.type) {
      case 'system.info':
        return system;
      case 'models.list':
        return models;
      case 'models.recommend': {
        // Con misure di affidabilità, il consiglio segue la lingua scelta: il modello con la media più alta.
        const language = payload.language === 'it' || payload.language === 'en' ? payload.language : undefined;
        const scores = new Map<string, number[]>();
        for (const r of reliability()) {
          if (r.overall === undefined || (language !== undefined && r.language !== language)) continue;
          scores.set(r.modelId, [...(scores.get(r.modelId) ?? []), r.overall]);
        }
        const ranked = [...scores.entries()]
          .map(([modelId, values]) => ({ modelId, score: mean(values)! }))
          .sort((a, b) => b.score - a.score);
        if (ranked.length > 0) {
          const best = ranked[0];
          const model = models.find((m) => m.id === best.modelId);
          return {
            recommendedId: best.modelId,
            basis: 'benchmark',
            reasons: [
              { code: 'benchmark_recommended', args: { modelId: best.modelId, model: model?.displayName ?? best.modelId, speed: '10', accuracy: String(best.score), language: language ?? 'auto' } },
            ],
            hardware: system.hardware,
          };
        }
        return {
          recommendedId: 'large-v3-turbo-q5_0',
          reasons: [
            { code: 'ram_limit', args: { totalBytes: String(16 * 2 ** 30), limitBytes: String(8 * GB) } },
            gpuOn
              ? { code: 'gpu_budget', args: { cores: '8', vramBytes: String(options.gpuVramGb! * GB), limitBytes: String(Math.floor(options.gpuVramGb! * GB * 0.8)) } }
              : { code: 'cpu_budget', args: { cores: '8', maxCost: '10' } },
            { code: 'recommended', args: { modelId: 'large-v3-turbo-q5_0', model: 'Large v3 Turbo (q5_0)' } },
          ],
          hardware: system.hardware,
        };
      }
      case 'models.download': {
        const m = models.find((x) => x.id === payload.modelId)!;
        m.state = 'downloading';
        event('models.changed');
        let step = 0;
        const advance = () => {
          if (cancelled.has(m.id)) {
            cancelled.delete(m.id);
            m.state = 'notInstalled';
            event('models.downloadFailed', { modelId: m.id, error: { code: 'cancelled', message: 'Annullato' } });
            event('models.changed');
            return;
          }
          step++;
          event('models.downloadProgress', {
            modelId: m.id,
            receivedBytes: Math.min(m.sizeBytes, (m.sizeBytes / 5) * step),
            totalBytes: m.sizeBytes,
            host: 'huggingface.co',
          });
          if (step < 5) later(advance);
          else {
            m.state = 'ready';
            m.sizeOnDiskBytes = m.sizeBytes;
            event('models.downloadCompleted', m);
            event('models.changed');
          }
        };
        later(advance);
        return { started: true };
      }
      case 'models.cancelDownload':
        cancelled.add(String(payload.modelId));
        return { cancelled: true };
      case 'models.import':
        if (options.cancelDialogs) return null;
        models[1].state = 'ready';
        return models[1];
      case 'models.remove': {
        const m = models.find((x) => x.id === payload.modelId)!;
        m.state = 'notInstalled';
        m.sizeOnDiskBytes = undefined;
        event('models.changed');
        return { removed: true };
      }
      case 'audio.pick':
        if (options.cancelDialogs) return null;
        return {
          audioId: 'audio-1',
          info: { fileName: 'riunione.mp3', format: 'mp3', duration: 754.2, sampleRate: 44100, channels: 2, sizeBytes: 12_100_000 },
        } satisfies PickedAudio;
      case 'audio.pickMany': {
        if (options.cancelDialogs) return null;
        const picked = (options.batchFiles ?? ['riunione.mp3']).map((fileName): PickedAudio => {
          const audioId = `batch-${++audioSeq}`;
          audioNames.set(audioId, fileName);
          return { audioId, info: audioInfo(fileName) };
        });
        const failed = (options.batchUnreadable ?? []).map((fileName) => ({
          fileName,
          error: { code: 'audio_unsupported', message: 'Formato audio non supportato.' },
        }));
        return { picked, failed };
      }
      case 'transcription.start': {
        const modelId = String(payload.modelId ?? settings.selectedModelId ?? 'base');
        const jobId = runJob(String(payload.audioId), modelId, (status, historyId, result) => {
          if (status === 'completed') event('transcription.completed', { jobId, result, historyId });
          else event('transcription.cancelled', { jobId });
        });
        return { jobId, modelId };
      }
      case 'transcription.get': {
        const result = results.get(String(payload.jobId));
        if (!result) throw { code: 'transcription_not_found', message: 'Trascrizione non trovata.' };
        return { jobId: payload.jobId, status: 'completed', result };
      }
      case 'transcription.active':
        return null;
      case 'updates.status':
        return updates;
      case 'updates.check': {
        if (options.failures?.['updates.check.network']) throw { code: options.failures['updates.check.network'], message: 'rete' };
        updates = options.updates ?? updates;
        event('updates.changed', updates);
        return updates;
      }
      case 'updates.apply': {
        if (updates.state !== 'available') throw { code: 'update_unavailable', message: 'nessun aggiornamento' };
        updates = { ...updates, state: 'downloading' };
        for (const percent of [40, 100]) later(() => event('updates.progress', { percent }));
        return { started: true };
      }
      case 'queue.get':
        return queueView();
      case 'queue.add': {
        for (const audioId of (payload.audioIds as string[]) ?? []) {
          const fileName = audioNames.get(audioId) ?? 'riunione.mp3';
          queue.items.push({ key: `q${++queueSeq}`, fileName, audioId, info: audioInfo(fileName), status: 'queued' });
        }
        for (const f of (payload.failed as { fileName: string; error: QueueItem['error'] }[]) ?? []) {
          queue.items.push({ key: `q${++queueSeq}`, fileName: f.fileName, status: 'failed', error: f.error });
        }
        return queueChanged();
      }
      case 'queue.remove':
        queue.items = queue.items.filter((i) => i.key !== payload.key || i.status === 'working');
        return queueChanged();
      case 'queue.clear':
        if (!queue.running) queue.items = queue.items.filter((i) => i.status === 'working');
        return queueChanged();
      case 'queue.start': {
        const modelId = String(payload.modelId ?? settings.selectedModelId ?? '');
        if (!models.some((m) => m.id === modelId && m.state === 'ready')) throw { code: 'model_not_installed', message: 'Il modello non è installato.' };
        queueOptions = { modelId, language: String(payload.language ?? settings.language) };
        queue.items = queue.items.map((i) => (i.status === 'failed' && i.audioId ? { ...i, status: 'queued', error: undefined } : i));
        queue.running = true;
        pump();
        return queueChanged();
      }
      case 'queue.stop': {
        // Qui l'annullamento è immediato: il file in corso torna subito in coda.
        queue.running = false;
        const working = queue.items.find((i) => i.jobId === queue.jobId);
        if (queue.jobId) cancelled.add(queue.jobId);
        queue.jobId = undefined;
        if (working) updateQueueItem(working.key, { status: 'queued', jobId: undefined });
        return queueChanged();
      }
      case 'transcription.pause':
        paused.add(String(payload.jobId));
        return { pausing: true };
      case 'transcription.resume':
        paused.delete(String(payload.jobId));
        return { resumed: true };
      case 'transcription.cancel':
        cancelled.add(String(payload.jobId));
        return { cancelling: true };
      case 'export.save':
        if (options.cancelDialogs) return null;
        return { saved: true, fileName: `riunione.${String(payload.format)}` };
      case 'settings.get':
        return settings;
      case 'settings.set':
        settings = payload as unknown as AppSettings;
        return settings;
      case 'history.list':
        return {
          directory: settings.historyDirectory ?? historyDirectory,
          entries: [...history.entries()]
            .map(([id, r]) => entry(id, r))
            .sort((a, b) => b.completedAt.localeCompare(a.completedAt)),
          collections: collections.map((c) => ({ ...c, entryIds: [...c.entryIds] })),
        };
      case 'history.update': {
        const id = String(payload.id);
        const r = history.get(id);
        if (!r) throw { code: 'history_not_found', message: 'not found' };
        if ('title' in payload) {
          const title = typeof payload.title === 'string' ? payload.title.trim() : '';
          if (title) titles.set(id, title);
          else titles.delete(id);
        }
        if ('collectionId' in payload) {
          const target = payload.collectionId ? findCollection(payload.collectionId) : undefined;
          const current = collectionOf(id);
          if (current !== target) {
            if (current) current.entryIds = current.entryIds.filter((x) => x !== id);
            target?.entryIds.push(id);
          }
        }
        return entry(id, r);
      }
      case 'collections.save': {
        const name = String(payload.name ?? '').trim();
        if (!name) throw { code: 'invalid_request', message: 'Nome della raccolta mancante.' };
        const color = payload.color as CollectionColor;
        if (payload.id) {
          const c = findCollection(payload.id);
          c.name = name;
          c.color = color;
          return c;
        }
        const created: Collection = { id: `col-${++collectionSeq}`, name, color, entryIds: [] };
        collections.push(created);
        return created;
      }
      case 'collections.delete': {
        const c = findCollection(payload.id);
        collections.splice(collections.indexOf(c), 1);
        return { deleted: true };
      }
      case 'collections.reorder': {
        const c = findCollection(payload.id);
        const ids = (payload.entryIds as string[]) ?? [];
        if (ids.length !== c.entryIds.length || ids.some((x) => !c.entryIds.includes(x))) {
          throw { code: 'invalid_request', message: 'Ordine non valido.' };
        }
        c.entryIds = [...ids];
        return c;
      }
      case 'history.get': {
        const r = history.get(String(payload.id));
        if (!r) throw { code: 'history_not_found', message: 'not found' };
        return r;
      }
      case 'history.delete':
        if (!history.delete(String(payload.id))) throw { code: 'history_not_found', message: 'not found' };
        titles.delete(String(payload.id));
        for (const c of collections) c.entryIds = c.entryIds.filter((x) => x !== payload.id);
        return { deleted: true };
      case 'benchmark.get':
        return benchSnapshot();
      case 'benchmark.reference': {
        const files = [
          { id: 'it-clean-1', language: 'it', title: 'Le avventure di Pinocchio, capitolo 6 (LibriVox)', url: 'https://archive.org/download/avventure_pinocchio_librivox/avventurepinocchio_06_collodi_64kb.mp3', sizeBytes: 1_764_750, sha256: '4d0f7bfe60934ce8c7490c3525ab118a55e31f2fe26bcb8d11e91ec1bc25f148', cached: referenceCached },
          { id: 'en-clean-1', language: 'en', title: 'The Wonderful Wizard of Oz, chapter 21 (LibriVox)', url: 'https://archive.org/download/wizard_of_oz/wizardofoz_21_baum_64kb.mp3', sizeBytes: 2_673_121, sha256: '99e4729c9b77706aeb1b00488d7700c39b882900ea938a5fbb34ac7108919437', cached: referenceCached },
        ];
        return {
          available: true,
          host: 'archive.org',
          license: 'Pubblico dominio',
          files,
          clips: mockClips,
          totalBytes: files.reduce((sum, f) => sum + f.sizeBytes, 0),
          missingBytes: files.filter((f) => !f.cached).reduce((sum, f) => sum + f.sizeBytes, 0),
          cached: referenceCached,
        };
      }
      case 'benchmark.estimate': {
        const found = [...benchEntries.values()].find((e) => e.modelId === String(payload.modelId));
        if (!found || found.stale !== 'none') return { basis: 'none' };
        const total = (payload.durations as number[]).filter((d) => d > 0).reduce((a, b) => a + b, 0);
        return { basis: 'benchmark', seconds: total > 0 ? found.loadSeconds + total / found.speed : 0, measuredAt: found.measuredAt, runtime: found.runtime };
      }
      case 'benchmark.cancel':
        benchCancel = true;
        return { cancelling: true };
      case 'benchmark.start': {
        if (options.cancelDialogs) return null;
        if (payload?.useReference) referenceCached = true;
        if (benchRunning) throw { code: 'transcription_busy', message: 'busy' };
        const ready = models.filter((m) => m.state === 'ready');
        benchRunning = true;
        benchCancel = false;
        const outcomes: BenchmarkProgress[] = [];
        // Un passo per modello e brano; con un file proprio c'è un solo brano e nessuna affidabilità.
        const jobs = payload?.useReference ? mockClips.map((c) => ({ clip: c })) : [{ clip: undefined }];
        const total = ready.length * jobs.length;
        const step = (i: number) => {
          if (benchCancel) {
            benchRunning = false;
            event('benchmark.cancelled', { snapshot: benchSnapshot() });
            return;
          }
          if (i >= total) {
            benchRunning = false;
            event('benchmark.completed', { outcomes, snapshot: benchSnapshot() });
            return;
          }
          const m = ready[Math.floor(i / jobs.length)];
          const { clip } = jobs[i % jobs.length];
          event('benchmark.progress', { modelId: m.id, index: i, total, phase: 'running', ...(clip ? { clipId: clip.id } : {}) });
          later(() => {
            const speed = Math.round((64 / m.relativeCost) * 10) / 10;
            // Il rumore costa qualche punto a tutti; i modelli più pesanti perdono meno.
            const accuracy = clip ? Math.round((100 - m.relativeCost * 2 - (clip.scenario === 'noisy' ? 6 : 0) - (clip.language === 'en' ? 1 : 0)) * 10) / 10 : undefined;
            if (clip && accuracy !== undefined) {
              benchEntries.set(`${m.id}|${clip.id}`, benchEntry(m.id, speed, { id: clip.id, title: clip.title, language: clip.language, scenario: clip.scenario, accuracy }));
            } else {
              benchEntries.set(`${m.id}|`, benchEntry(m.id, speed));
            }
            const done: BenchmarkProgress = { modelId: m.id, index: i, total, phase: 'done', speed, loadSeconds: 2, ...(accuracy === undefined ? {} : { accuracy }), ...(clip ? { clipId: clip.id } : {}) };
            outcomes.push(done);
            event('benchmark.progress', done);
            step(i + 1);
          });
        };
        // Un nuovo test sostituisce le misure precedenti.
        benchEntries.clear();
        later(() => step(0));
        return { started: true, fileName: 'riferimento.wav', duration: 90 };
      }
      case 'history.openFolder':
        return { opened: true };
      case 'diagnostics.openLogs':
        return { opened: true };
      case 'history.pickFolder':
        if (options.cancelDialogs) return null;
        return { directory: 'D:\\Trascrizioni' };
      case 'history.testFolder':
        if (!options.folderTestError) return { ok: true };
        return options.folderTestError === 'blocked_by_windows'
          ? { ok: false, error: 'blocked_by_windows', controlledFolderAccess: 'on', appPath: 'C:\\Users\\utente\\AppData\\Local\\CodexVTT\\current\\CodexVtt.App.exe' }
          : { ok: false, error: options.folderTestError };
      case 'history.setFolder':
        if (options.folderTestError) throw { code: 'folder_test_failed', message: options.folderTestError };
        historyDirectory = String(payload.directory);
        settings = { ...settings, historyDirectory, historyDirectoryTestedAt: '2026-10-24T16:00:00.000Z' };
        return settings;
      default:
        throw { code: 'invalid_request', message: `Comando sconosciuto: ${request.type}` };
    }
  };

  return {
    send(raw) {
      const request = JSON.parse(raw) as BridgeRequest;
      const failure = options.failures?.[request.type];
      try {
        if (failure) throw { code: failure, message: failure };
        const payload = handle(request);
        emit({ v: 1, id: request.id, type: 'result', ok: true, payload });
      } catch (error) {
        emit({ v: 1, id: request.id, type: 'result', ok: false, error });
      }
    },
    onMessage(handler) {
      handlers.add(handler);
      return () => handlers.delete(handler);
    },
  };
}

function model(id: string, displayName: string, sizeBytes: number, ramGb: number, relativeCost: number, issues: ModelInfo['issues'] = []): ModelInfo {
  return {
    id,
    displayName,
    file: `ggml-${id}.bin`,
    quantization: id.includes('q5') ? 'q5_0' : 'f16',
    sizeBytes,
    ramRequiredBytes: ramGb * GB,
    relativeCost,
    multilingual: true,
    license: 'MIT',
    downloadable: true,
    state: 'notInstalled',
    issues,
    engine: 'whisper',
    preview: false,
    requiresLanguage: false,
  };
}
