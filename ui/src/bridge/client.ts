import {
  PROTOCOL_VERSION,
  type BridgeError,
  type BridgeEvent,
  type BridgeResponse,
  type Commands,
  type Events,
  type ExportFormat,
  type PickedBatch,
} from './protocol';

/** Canale di trasporto: HybridWebView nell'app, mock nello sviluppo e nei test. */
export interface Transport {
  send(message: string): void;
  onMessage(handler: (message: string) => void): () => void;
}

/**
 * File quando la UI gira nel browser (versione server): il core non può aprire dialog sul PC di chi guarda,
 * quindi i file audio si caricano e gli export si scaricano via HTTP.
 */
export interface FileHost {
  pickAudio(accept: string[]): Promise<PickedBatch | null>;
  download(request: { jobId?: string; historyId?: string; format: ExportFormat; includeHeader?: boolean }): void;
}

export class BridgeCallError extends Error {
  readonly code: string;
  readonly details?: Record<string, string>;

  constructor(error: BridgeError) {
    super(error.message);
    this.code = error.code;
    this.details = error.details;
  }
}

type Pending = { resolve: (value: unknown) => void; reject: (error: unknown) => void };
type EventHandler = (payload: unknown) => void;

export class BridgeClient {
  private readonly pending = new Map<string, Pending>();
  private readonly listeners = new Map<string, Set<EventHandler>>();
  private nextId = 0;

  constructor(
    private readonly transport: Transport,
    readonly files?: FileHost,
  ) {
    transport.onMessage((raw) => this.receive(raw));
  }

  call<K extends keyof Commands>(
    type: K,
    ...args: Commands[K]['req'] extends void ? [] : [Commands[K]['req']]
  ): Promise<Commands[K]['res']> {
    const id = `ui-${++this.nextId}`;
    const message = JSON.stringify({ v: PROTOCOL_VERSION, id, type, payload: args[0] });
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve: resolve as (value: unknown) => void, reject });
      this.transport.send(message);
    });
  }

  on<K extends keyof Events>(event: K, handler: (payload: Events[K]) => void): () => void {
    const set = this.listeners.get(event) ?? new Set<EventHandler>();
    set.add(handler as EventHandler);
    this.listeners.set(event, set);
    return () => set.delete(handler as EventHandler);
  }

  private receive(raw: string) {
    let message: BridgeResponse | BridgeEvent;
    try {
      message = JSON.parse(raw);
    } catch {
      return;
    }

    if (message.type === 'event') {
      // Le risposte in sospeso non arriveranno più: la UI ricarica lo stato quando il collegamento torna.
      if (message.event === 'connection.lost') {
        const pending = [...this.pending.values()];
        this.pending.clear();
        pending.forEach((p) => p.reject(new BridgeCallError({ code: 'connection_lost', message: 'Collegamento interrotto.' })));
      }
      this.listeners.get(message.event)?.forEach((handler) => handler(message.payload));
      return;
    }

    const pending = this.pending.get(message.id);
    if (!pending) return;
    this.pending.delete(message.id);
    if (message.ok) pending.resolve(message.payload ?? null);
    else pending.reject(new BridgeCallError(message.error ?? { code: 'internal_error', message: 'Errore sconosciuto' }));
  }
}
