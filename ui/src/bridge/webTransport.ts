import { BridgeCallError, type FileHost, type Transport } from './client';
import { PROTOCOL_VERSION, type BridgeError, type PickedBatch } from './protocol';

/**
 * Trasporto della versione server (ADR-0007): gli stessi messaggi JSON di HybridWebView su un WebSocket
 * verso il core. Se il collegamento cade si riconnette da solo; il core intanto continua a lavorare.
 */
export function createWebTransport(url = socketUrl(window.location)): Transport {
  const handlers = new Set<(message: string) => void>();
  const outbox: string[] = [];
  let socket: WebSocket | undefined;
  let attempts = 0;
  let connectedOnce = false;
  const deliver = (message: string) => handlers.forEach((h) => h(message));
  const local = (event: string) => deliver(JSON.stringify({ v: PROTOCOL_VERSION, type: 'event', event }));

  const connect = () => {
    const ws = new WebSocket(url);
    socket = ws;
    ws.onopen = () => {
      attempts = 0;
      if (connectedOnce) local('connection.restored');
      connectedOnce = true;
      while (outbox.length > 0) ws.send(outbox.shift()!);
    };
    ws.onmessage = (e) => {
      if (typeof e.data === 'string') deliver(e.data);
    };
    ws.onclose = () => {
      if (socket !== ws) return;
      socket = undefined;
      if (connectedOnce) local('connection.lost');
      setTimeout(connect, Math.min(10_000, 500 * 2 ** attempts++));
    };
  };
  connect();

  return {
    send: (message) => {
      if (socket?.readyState === WebSocket.OPEN) socket.send(message);
      else outbox.push(message);
    },
    onMessage: (handler) => {
      handlers.add(handler);
      return () => handlers.delete(handler);
    },
  };
}

export function socketUrl(location: Pick<Location, 'protocol' | 'host'>): string {
  return `${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/bridge`;
}

/** Caricamento con il selettore del browser e download degli export dal server. */
export function createWebFiles(): FileHost {
  return {
    pickAudio: (accept) =>
      new Promise<File[]>((resolve) => {
        const input = document.createElement('input');
        input.type = 'file';
        input.multiple = true;
        input.accept = accept.join(',');
        input.addEventListener('change', () => resolve([...(input.files ?? [])]));
        input.addEventListener('cancel', () => resolve([]));
        input.click();
      }).then((files) => (files.length === 0 ? null : uploadAudio(files))),
    download: (request) => {
      const query = new URLSearchParams({ format: request.format });
      if (request.jobId) query.set('jobId', request.jobId);
      if (request.historyId) query.set('historyId', request.historyId);
      if (request.includeHeader !== undefined) query.set('includeHeader', String(request.includeHeader));
      const link = document.createElement('a');
      link.href = `api/export?${query.toString()}`;
      link.download = '';
      link.click();
    },
  };
}

export async function uploadAudio(files: File[], fetcher: typeof fetch = fetch): Promise<PickedBatch> {
  const form = new FormData();
  for (const file of files) form.append('files', file, file.name);
  let response: Response;
  try {
    response = await fetcher('api/audio', { method: 'POST', body: form, credentials: 'same-origin' });
  } catch {
    throw new BridgeCallError({ code: 'upload_failed', message: 'Caricamento non riuscito.' });
  }
  if (!response.ok) {
    const error = (await response.json().catch(() => undefined)) as BridgeError | undefined;
    throw new BridgeCallError(error?.code ? error : { code: 'upload_failed', message: `HTTP ${response.status}` });
  }
  return (await response.json()) as PickedBatch;
}
