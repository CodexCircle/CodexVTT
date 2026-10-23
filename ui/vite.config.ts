import { defineConfig, type Plugin } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { APP_CSP } from './src/security/csp';

/**
 * Solo nella build per l'app (ADR-0006): CSP che consente esclusivamente risorse locali e
 * script di HybridWebView (vedi src/security/csp.ts). In sviluppo Vite inietta stili inline, incompatibili con la CSP.
 */
const CSP = APP_CSP;

/** Nella build per la versione server (`--mode server`) la stessa CSP, ma senza lo script di HybridWebView. */
function appShell(hybridWebView: boolean): Plugin {
  return {
    name: 'codexvtt-app-shell',
    apply: 'build',
    transformIndexHtml() {
      return [
        { tag: 'meta', attrs: { 'http-equiv': 'Content-Security-Policy', content: CSP }, injectTo: 'head-prepend' },
        ...(hybridWebView ? [{ tag: 'script', attrs: { src: '_framework/hybridwebview.js' }, injectTo: 'head' as const }] : []),
      ];
    },
  };
}

export default defineConfig(({ mode }) => ({
  base: './',
  // La preview statica gira in un normale browser: niente CSP dell'app né script di HybridWebView.
  plugins: mode === 'preview' ? [react()] : [react(), appShell(mode !== 'server')],
  build: {
    target: 'es2022',
    assetsInlineLimit: 0,
    sourcemap: false,
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
}));
