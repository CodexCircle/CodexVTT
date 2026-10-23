import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { BridgeClient } from './bridge/client';
import { createHybridTransport } from './bridge/hybridTransport';
import { createMockTransport } from './bridge/mockTransport';
import { createWebFiles, createWebTransport } from './bridge/webTransport';
import { createI18n, resolveLocale } from './i18n/i18n';
import { previewCollections, previewHistory, previewTitles } from './preview/seed';
import './styles.css';

// Nell'app MAUI c'è sempre HybridWebView; la build `--mode server` parla con CodexVtt.Server via WebSocket.
// Il mock serve solo per lo sviluppo nel browser e per la preview statica (`npm run build:preview`), che parte con dati di esempio.
const preview = import.meta.env.MODE === 'preview';
const server = import.meta.env.MODE === 'server';
const transport =
  createHybridTransport() ??
  (server ? createWebTransport() : null) ??
  (preview
    ? createMockTransport({
        tick: 700,
        jobStep: 5,
        installed: ['base', 'small', 'large-v3-turbo-q5_0'],
        history: previewHistory,
        titles: previewTitles,
        collections: previewCollections,
        uiLanguage: 'it',
      })
    : import.meta.env.DEV
      ? createMockTransport({ installed: [] })
      : null);

const root = createRoot(document.getElementById('root')!);
if (transport) {
  root.render(
    <StrictMode>
      <App client={new BridgeClient(transport, server ? createWebFiles() : undefined)} />
    </StrictMode>,
  );
} else {
  root.render(<p role="alert">{createI18n(resolveLocale(null)).t('desktopOnly')}</p>);
}
