/**
 * Content-Security-Policy dell'app (ADR-0006): consente solo risorse locali e lo script di HybridWebView.
 * Definita qui per poterla verificare nei test; vite.config.ts la inserisce solo nella build dell'app.
 */
export const APP_CSP =
  "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
  "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'none'; frame-src 'none'";
