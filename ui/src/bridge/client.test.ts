import { describe, expect, it } from 'vitest';
import { BridgeCallError, BridgeClient, type Transport } from './client';

function loopback(reply: (request: { id: string; type: string; payload?: unknown }) => object | null) {
  const handlers = new Set<(m: string) => void>();
  const sent: string[] = [];
  const transport: Transport = {
    send(message) {
      sent.push(message);
      const response = reply(JSON.parse(message));
      if (response) queueMicrotask(() => handlers.forEach((h) => h(JSON.stringify(response))));
    },
    onMessage(handler) {
      handlers.add(handler);
      return () => handlers.delete(handler);
    },
  };
  const push = (message: object) => handlers.forEach((h) => h(JSON.stringify(message)));
  return { transport, sent, push };
}

describe('BridgeClient', () => {
  it('sends versioned requests and resolves matching responses', async () => {
    const { transport, sent } = loopback((r) => ({ v: 1, id: r.id, type: 'result', ok: true, payload: { removed: true } }));
    const client = new BridgeClient(transport);

    await expect(client.call('models.remove', { modelId: 'base' })).resolves.toEqual({ removed: true });
    expect(JSON.parse(sent[0])).toMatchObject({ v: 1, type: 'models.remove', payload: { modelId: 'base' } });
  });

  it('rejects with typed errors', async () => {
    const { transport } = loopback((r) => ({
      v: 1,
      id: r.id,
      type: 'result',
      ok: false,
      error: { code: 'model_unknown', message: 'Modello sconosciuto' },
    }));
    const client = new BridgeClient(transport);

    const error = await client.call('models.remove', { modelId: 'x' }).catch((e) => e);
    expect(error).toBeInstanceOf(BridgeCallError);
    expect(error.code).toBe('model_unknown');
  });

  it('dispatches events and ignores malformed or unknown messages', () => {
    const { transport, push } = loopback(() => null);
    const client = new BridgeClient(transport);
    const seen: number[] = [];
    const off = client.on('transcription.progress', (p) => seen.push(p.percent));

    push({ v: 1, type: 'event', event: 'transcription.progress', payload: { percent: 40 } });
    push({ v: 1, id: 'nobody', type: 'result', ok: true });
    off();
    push({ v: 1, type: 'event', event: 'transcription.progress', payload: { percent: 80 } });

    expect(seen).toEqual([40]);
  });
});
