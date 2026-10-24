// Contratto core ↔ UI (ADR-0001). Specchio dei DTO in src/CodexVtt.Bridge: va aggiornato insieme.

export const PROTOCOL_VERSION = 1;

export interface BridgeError {
  code: string;
  message: string;
  details?: Record<string, string>;
}

export interface BridgeRequest {
  v: number;
  id: string;
  type: string;
  payload?: unknown;
}

export interface BridgeResponse {
  v: number;
  id: string;
  type: 'result';
  ok: boolean;
  payload?: unknown;
  error?: BridgeError;
}

export interface BridgeEvent {
  v: number;
  type: 'event';
  event: string;
  payload?: unknown;
}

export type ModelState = 'notInstalled' | 'downloading' | 'verifying' | 'unverified' | 'ready' | 'corrupted';

export type CompatibilityIssue = 'insufficientMemory' | 'insufficientDisk' | 'tooSlow' | 'notPinned' | 'insufficientVram';

/** Affidabilità dichiarata: 100 meno il WER sull'italiano, con la fonte. */
export interface ModelAccuracy {
  language: string;
  percent: number;
  metric: string;
  source: string;
  /** false: valore preso dalla scheda del modello, non misurato da CodexVTT. */
  measuredByProject: boolean;
}

export interface ModelInfo {
  id: string;
  displayName: string;
  file: string;
  quantization: string;
  sizeBytes: number;
  ramRequiredBytes: number;
  /** Costo di calcolo relativo del catalogo (Tiny = 1); serve a confrontare i modelli tra loro. */
  relativeCost: number;
  multilingual: boolean;
  license: string;
  downloadable: boolean;
  state: ModelState;
  sizeOnDiskBytes?: number;
  issues: CompatibilityIssue[];
  /** Motore che lo esegue: "whisper", "sherpa-transducer", "sherpa-canary", "sherpa-cohere". */
  engine: string;
  /** Anteprima: non viene mai raccomandato in automatico. */
  preview: boolean;
  /** Non rileva la lingua: va scelta prima di trascrivere. */
  requiresLanguage: boolean;
  /** Attribuzione richiesta dalla licenza (CC-BY-4.0), da mostrare. */
  attribution?: string;
  accuracy?: ModelAccuracy;
}

export interface HardwareProfile {
  operatingSystem: string;
  architecture: string;
  logicalCores: number;
  totalMemoryBytes: number;
  availableMemoryBytes: number;
  cpuFeatures: string;
  freeDiskBytes: number;
  /** Schede video utilizzabili con Vulkan; vuoto se non ce ne sono (Whisper usa la CPU). */
  gpus?: string[];
  /** Dettagli delle schede: la memoria video c'è solo per le dedicate e solo se il driver la riporta. */
  gpuDetails?: GpuDevice[];
  /** Whisper userà la scheda video (impostazione attiva, scheda trovata, AVX2): i modelli si giudicano dalla sua memoria. */
  gpuAcceleration?: boolean;
  /** Memoria video della scheda dedicata più grande, se nota. */
  gpuVramBytes?: number;
}

export interface GpuDevice {
  name: string;
  kind: 'dedicata' | 'integrata' | 'virtuale';
  vramBytes?: number;
}

export interface SystemInfo {
  appVersion: string;
  protocolVersion: number;
  processing: 'local';
  hardware: HardwareProfile;
  supportsStandardRuntime: boolean;
  supportedAudioExtensions: string[];
  /** "server": UI nel browser, file caricati e scaricati via HTTP; assente o "desktop": app con dialog nativi. */
  host?: 'desktop' | 'server';
}

export interface SelectionReason {
  code: 'ram_limit' | 'cpu_budget' | 'gpu_budget' | 'compat_runtime' | 'recommended' | 'none_compatible' | string;
  args: Record<string, string>;
}

export interface Recommendation {
  recommendedId?: string;
  /** `benchmark` se il consiglio viene dalle misure del Test bench, `hardware` se viene dalle regole sul PC. */
  basis?: 'benchmark' | 'hardware';
  reasons: SelectionReason[];
  hardware: HardwareProfile;
}

export interface AudioInfo {
  fileName: string;
  format: string;
  duration: number;
  sampleRate: number;
  channels: number;
  sizeBytes: number;
}

export interface PickedAudio {
  audioId: string;
  info: AudioInfo;
}

/** Esito della scelta di più file (batch): quelli leggibili e quelli scartati con il loro errore. */
export interface PickedBatch {
  picked: PickedAudio[];
  failed: { fileName: string; error: BridgeError }[];
}

export interface Segment {
  start: number;
  end: number;
  text: string;
  probability?: number;
}

export interface ModelUsage {
  modelId: string;
  displayName: string;
  file: string;
  sha256: string;
  runtime: string;
}

export interface TranscriptionResult {
  audioFileName: string;
  audioDuration: number;
  detectedLanguage?: string;
  model: ModelUsage;
  segments: Segment[];
  elapsed: number;
  completedAt: string;
  text: string;
}

export type QueueStatus = 'queued' | 'working' | 'done' | 'failed';

/** Un file della coda nel core. I file illeggibili entrano già come "failed", senza audioId. */
export interface QueueItem {
  key: string;
  fileName: string;
  audioId?: string;
  info?: AudioInfo;
  status: QueueStatus;
  error?: BridgeError;
  historyId?: string;
  jobId?: string;
}

/** Stato della coda: vive nel core, così prosegue anche senza UI collegata (versione server). */
export interface QueueState {
  items: QueueItem[];
  running: boolean;
  jobId?: string;
  /** Avanzamento del file in corso (anche in pausa), per chi riapre la pagina. */
  progress?: JobProgress;
}

export type UpdateState = 'unsupported' | 'upToDate' | 'available' | 'downloading' | 'failed';

/** Esito del controllo aggiornamenti (ADR-0008). Versione e note solo se ce n'è una nuova. */
export interface UpdateStatus {
  state: UpdateState;
  currentVersion: string;
  latestVersion?: string;
  notes?: string;
  releasesUrl?: string;
}

export type JobStatus = 'queued' | 'decoding' | 'transcribing' | 'completed' | 'cancelled' | 'failed' | 'paused';

export interface JobProgress {
  jobId: string;
  status: JobStatus;
  percent: number;
  processed: number;
  total: number;
  /** Secondi stimati alla fine del file; assente finché è troppo presto per stimarli. */
  remaining?: number;
  /** Secondi di audio trascritti per secondo reale (2 = il doppio del tempo reale). */
  speed?: number;
}

export interface DownloadProgress {
  modelId: string;
  receivedBytes: number;
  totalBytes: number;
  host: string;
}

export const mascotCharacters = ['tina', 'giulio'] as const;
export type MascotCharacter = (typeof mascotCharacters)[number];

/** Temi di colore: il primo è il predefinito (salvato come assente). */
/**
 * Esito del test di una cartella: `error` è not_found, blocked_by_windows, cannot_write, cannot_read o cannot_delete.
 * Con blocked_by_windows arrivano anche lo stato di «Accesso alle cartelle controllato» e il percorso dell'app da autorizzare.
 */
export interface FolderTestResult {
  ok: boolean;
  error?: string;
  controlledFolderAccess?: 'on' | 'audit' | 'off' | 'unknown';
  appPath?: string;
  /** Eccezione grezza (tipo e HRESULT), per capire cosa ha restituito il sistema. */
  detail?: string;
}

/** Perché una misura del Test bench non vale più (`none` = vale). */
export type BenchmarkStale = 'none' | 'hardwareChanged' | 'modelChanged' | 'runtimeChanged';

export interface BenchmarkEntry {
  modelId: string;
  /** Runtime usato nella misura, per esempio «Cpu» o «Vulkan (GPU)». */
  runtime: string;
  threads?: number;
  audioSeconds: number;
  /** Tempo fisso (caricamento del modello). */
  loadSeconds: number;
  /** Secondi di audio per secondo reale dopo il caricamento (4 = quattro volte il tempo reale). */
  speed: number;
  measuredAt: string;
  /** Affidabilità in percentuale (100 meno l'errore sulle parole) contro il testo del file ufficiale; assente con un file proprio. */
  accuracy?: number;
  /** Brano ufficiale della misura, la sua lingua (it, en) e la condizione; assenti con un file proprio. */
  clipId?: string;
  /** Titolo del brano ufficiale, per il dettaglio per brano. */
  clipTitle?: string;
  language?: string;
  scenario?: BenchmarkScenario;
  stale: BenchmarkStale;
}

/** Condizione di ascolto di un brano: lettura pulita o la stessa con rumore sintetico aggiunto. */
export type BenchmarkScenario = 'clean' | 'noisy';

/** Affidabilità di un modello in una lingua: media sui brani puliti, sul brano rumoroso e media delle due (i due scenari pesano uguale). */
export interface BenchmarkReliability {
  modelId: string;
  language: string;
  clean?: number;
  noisy?: number;
  overall?: number;
}

export interface BenchmarkState {
  running: boolean;
  entries: BenchmarkEntry[];
  reliability: BenchmarkReliability[];
}

export interface BenchmarkProgress {
  modelId: string;
  index: number;
  total: number;
  phase: 'running' | 'done' | 'failed';
  speed?: number;
  loadSeconds?: number;
  accuracy?: number;
  errorCode?: string;
  clipId?: string;
}

/** `none` = nessuna misura valida per il modello: la UI non inventa numeri. */
export interface BenchmarkEstimate {
  basis: 'none' | 'benchmark';
  seconds?: number;
  measuredAt?: string;
  runtime?: string;
}

/** Un file ufficiale da scaricare: indirizzo, dimensione e hash fissati nel codice. */
export interface BenchmarkReferenceFile {
  id: string;
  language: string;
  title: string;
  url: string;
  sizeBytes: number;
  sha256: string;
  /** Già scaricato e verificato. */
  cached: boolean;
}

/** Un brano del test: i rumorosi usano il file di un brano pulito con rumore sintetico aggiunto dall'app. */
export interface BenchmarkReferenceClip {
  id: string;
  language: string;
  scenario: BenchmarkScenario;
  title: string;
  seconds: number;
}

export interface BenchmarkReferenceInfo {
  available: boolean;
  host: string;
  license: string;
  files: BenchmarkReferenceFile[];
  clips: BenchmarkReferenceClip[];
  totalBytes: number;
  /** Quanto resta da scaricare. */
  missingBytes: number;
  /** Tutti i file già scaricati e verificati: il test parte senza rete. */
  cached: boolean;
}

export const palettes = ['sabbia', 'natale', 'halloween', 'ciliegia'] as const;
export type Palette = (typeof palettes)[number];

export interface AppSettings {
  selectedModelId?: string;
  language: string;
  showMascot: boolean;
  exportIncludeHeader: boolean;
  modelsDirectory?: string;
  /** "it", "en" oppure assente (lingua di sistema). */
  uiLanguage?: 'it' | 'en' | null;
  /** "light", "dark" oppure assente (come il sistema). */
  theme?: 'light' | 'dark' | null;
  /** Tema di colore; assente = Sabbia. */
  palette?: Palette | null;
  /** Mascotte scelta; assente = Tina. */
  mascot?: MascotCharacter | null;
  historyDirectory?: string | null;
  /** Quando la cartella scelta ha superato il test (ISO 8601); assente = mai testata. Lo imposta solo il test. */
  historyDirectoryTestedAt?: string | null;
  /** Quanta CPU possono usare i motori, in percentuale dei core (10-100); assente = 100, nessun limite. Vale dalla prossima trascrizione. */
  cpuLimitPercent?: number;
  /** Usa la scheda video (Vulkan) per Whisper quando il PC la supporta; assente = sì. Si applica al prossimo avvio. */
  useGpu?: boolean;
  /** Controlla da solo gli aggiornamenti all'avvio (contatta GitHub); assente = no, solo a richiesta. */
  autoCheckUpdates?: boolean;
  /** Livello del log diagnostico ("Debug" attiva la modalità debug dal prossimo avvio); assente = Information. */
  diagnosticLogLevel?: string | null;
}

export interface HistoryEntry {
  id: string;
  audioFileName: string;
  completedAt: string;
  audioDuration: number;
  modelDisplayName: string;
  detectedLanguage?: string;
  preview: string;
  textFileName: string;
  /** Nome scelto dall'utente; assente = nome del file audio. */
  title?: string | null;
  /** Raccolta a cui appartiene, se c'è. */
  collectionId?: string | null;
}

/** Colori disponibili per le raccolte (token --tag-* nello stile). */
export const collectionColors = ['bordeaux', 'green', 'cream', 'rose', 'sage', 'ochre'] as const;
export type CollectionColor = (typeof collectionColors)[number];

/** Raccolta di trascrizioni: entryIds è l'ordine scelto dall'utente. */
export interface Collection {
  id: string;
  name: string;
  color: CollectionColor;
  entryIds: string[];
}

export interface HistoryList {
  directory: string;
  entries: HistoryEntry[];
  collections: Collection[];
}

export type ExportFormat = 'txt' | 'srt' | 'vtt';

/** Comandi supportati con i relativi payload e risposte. */
export interface Commands {
  'system.info': { req: void; res: SystemInfo };
  'models.list': { req: void; res: ModelInfo[] };
  /** `language`: la lingua scelta in Home (it, en, auto); senza, quella delle impostazioni. */
  'models.recommend': { req: { language?: string } | void; res: Recommendation };
  'models.download': { req: { modelId: string }; res: { started: boolean } };
  'models.cancelDownload': { req: { modelId: string }; res: { cancelled: boolean } };
  'models.verify': { req: { modelId: string }; res: ModelInfo };
  'models.import': { req: void; res: ModelInfo | null };
  'models.remove': { req: { modelId: string }; res: { removed: boolean } };
  'audio.pick': { req: void; res: PickedAudio | null };
  /** Selezione multipla di file audio per la coda. */
  'audio.pickMany': { req: void; res: PickedBatch | null };
  'transcription.start': {
    req: { audioId: string; modelId?: string; language?: string };
    res: { jobId: string; modelId: string };
  };
  'transcription.cancel': { req: { jobId: string }; res: { cancelling: boolean } };
  'transcription.pause': { req: { jobId: string }; res: { pausing: boolean } };
  'transcription.resume': { req: { jobId: string }; res: { resumed: boolean } };
  'transcription.get': {
    req: { jobId: string };
    res: { jobId: string; status: JobStatus; result?: TranscriptionResult; error?: BridgeError };
  };
  /** Trascrizione singola in corso, per chi riapre la pagina mentre il core lavora. */
  'transcription.active': {
    req: void;
    res: { jobId: string; modelId: string; audio: PickedAudio; progress?: JobProgress } | null;
  };
  'updates.status': { req: void; res: UpdateStatus };
  'updates.check': { req: void; res: UpdateStatus };
  /** Scarica e installa: l'app si riavvia da sola alla fine. */
  'updates.apply': { req: void; res: { started: boolean } };
  'queue.get': { req: void; res: QueueState };
  'queue.add': { req: { audioIds: string[]; failed?: PickedBatch['failed'] }; res: QueueState };
  'queue.remove': { req: { key: string }; res: QueueState };
  'queue.clear': { req: void; res: QueueState };
  'queue.start': { req: { modelId?: string; language?: string }; res: QueueState };
  'queue.stop': { req: void; res: QueueState };
  'export.save': {
    req: { jobId?: string; historyId?: string; format: ExportFormat; includeHeader?: boolean };
    res: { saved: boolean; fileName: string } | null;
  };
  'settings.get': { req: void; res: AppSettings };
  'settings.set': { req: AppSettings; res: AppSettings };
  'history.list': { req: void; res: HistoryList };
  'history.get': { req: { id: string }; res: TranscriptionResult };
  'history.delete': { req: { id: string }; res: { deleted: boolean } };
  /** Cambia nome e/o raccolta di una trascrizione (null = toglie). */
  'history.update': { req: { id: string; title?: string | null; collectionId?: string | null }; res: HistoryEntry };
  /** Crea (senza id) o modifica una raccolta. */
  'collections.save': { req: { id?: string; name: string; color: CollectionColor }; res: Collection };
  'collections.delete': { req: { id: string }; res: { deleted: boolean } };
  /** Nuovo ordine delle trascrizioni nella raccolta. */
  'collections.reorder': { req: { id: string; entryIds: string[] }; res: Collection };
  'history.openFolder': { req: void; res: { opened: boolean } };
  'diagnostics.openLogs': { req: void; res: { opened: boolean } };
  /** Apre la selezione della cartella: non la salva, va prima testata. */
  /** Sceglie il file di riferimento (dialog nativo) e avvia il test su tutti i modelli pronti; null se si annulla. */
  'benchmark.start': { req: { useReference?: boolean } | void; res: { started: boolean; fileName: string; duration: number } | null };
  /** File di riferimento ufficiale: indirizzo, dimensione, hash e licenza da mostrare prima di scaricarlo. Non scarica nulla. */
  'benchmark.reference': { req: void; res: BenchmarkReferenceInfo };
  'benchmark.cancel': { req: void; res: { cancelling: boolean } };
  'benchmark.get': { req: void; res: BenchmarkState };
  /** Stima per file di queste durate (secondi) con il modello indicato, dall'ultima misura valida. */
  'benchmark.estimate': { req: { modelId: string; durations: number[] }; res: BenchmarkEstimate };
  'history.pickFolder': { req: void; res: { directory: string } | null };
  'history.testFolder': { req: { directory: string }; res: FolderTestResult };
  /** Salva la cartella solo se il test riesce (altrimenti errore folder_test_failed e resta la precedente). */
  'history.setFolder': { req: { directory: string }; res: AppSettings };
}

export interface Events {
  'models.changed': undefined;
  'models.downloadProgress': DownloadProgress;
  'models.downloadCompleted': ModelInfo;
  'models.downloadFailed': { modelId: string; error: BridgeError };
  'transcription.progress': JobProgress;
  'transcription.completed': { jobId: string; result: TranscriptionResult; historyId?: string };
  'transcription.failed': { jobId: string; error: BridgeError };
  'transcription.cancelled': { jobId: string };
  'history.changed': undefined;
  'history.saveFailed': { jobId: string; error: BridgeError };
  'updates.changed': UpdateStatus;
  'updates.progress': { percent: number };
  'updates.failed': BridgeError;
  'queue.changed': QueueState;
  'benchmark.progress': BenchmarkProgress;
  'benchmark.completed': { outcomes: BenchmarkProgress[]; snapshot: BenchmarkState };
  'benchmark.cancelled': { snapshot: BenchmarkState };
  'benchmark.failed': { error: BridgeError; snapshot: BenchmarkState };
  'queue.finished': { done: number; failed: number };
  /** Eventi locali del trasporto web: il collegamento con il server è caduto o è tornato. */
  'connection.lost': undefined;
  'connection.restored': undefined;
}
