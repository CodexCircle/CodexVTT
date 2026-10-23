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

export type CompatibilityIssue = 'insufficientMemory' | 'insufficientDisk' | 'tooSlow' | 'notPinned';

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
  code: 'ram_limit' | 'cpu_budget' | 'compat_runtime' | 'recommended' | 'none_compatible' | string;
  args: Record<string, string>;
}

export interface Recommendation {
  recommendedId?: string;
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

export type JobStatus = 'queued' | 'decoding' | 'transcribing' | 'completed' | 'cancelled' | 'failed';

export interface JobProgress {
  jobId: string;
  status: JobStatus;
  percent: number;
  processed: number;
  total: number;
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
  'models.recommend': { req: void; res: Recommendation };
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
  'history.pickFolder': { req: void; res: AppSettings | null };
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
  'queue.finished': { done: number; failed: number };
  /** Eventi locali del trasporto web: il collegamento con il server è caduto o è tornato. */
  'connection.lost': undefined;
  'connection.restored': undefined;
}
