import type { Transport } from './client';

declare global {
  interface Window {
    HybridWebView?: {
      SendRawMessage(message: string): void;
    };
  }
}

/** Trasporto reale: messaggi raw di .NET MAUI HybridWebView. */
export function createHybridTransport(): Transport | null {
  if (!window.HybridWebView) return null;
  return {
    send: (message) => window.HybridWebView!.SendRawMessage(message),
    onMessage: (handler) => {
      const listener = (event: Event) => {
        const detail = (event as CustomEvent<{ message: string }>).detail;
        if (detail?.message) handler(detail.message);
      };
      window.addEventListener('HybridWebViewMessageReceived', listener);
      return () => window.removeEventListener('HybridWebViewMessageReceived', listener);
    },
  };
}
