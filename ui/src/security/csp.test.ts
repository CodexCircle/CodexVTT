import { describe, expect, it } from 'vitest';
import { APP_CSP } from './csp';

/** Direttive che devono restare chiuse verso l'esterno (ADR-0006, issue #6: nessuna richiesta di rete dalla WebView). */
function directive(name: string): string[] {
  const found = APP_CSP.split(';').map((d) => d.trim()).find((d) => d.startsWith(`${name} `));
  return found ? found.split(/\s+/).slice(1) : [];
}

describe('Content-Security-Policy', () => {
  it('limita script, connessioni e risorse all’origine locale', () => {
    expect(directive('default-src')).toEqual(["'self'"]);
    expect(directive('script-src')).toEqual(["'self'"]);
    expect(directive('connect-src')).toEqual(["'self'"]);
    expect(directive('style-src')).toEqual(["'self'"]);
    expect(directive('font-src')).toEqual(["'self'"]);
    expect(directive('img-src')).toEqual(["'self'", 'data:']);
  });

  it('non consente plugin, frame, form o base URI esterni', () => {
    expect(directive('object-src')).toEqual(["'none'"]);
    expect(directive('frame-src')).toEqual(["'none'"]);
    expect(directive('form-action')).toEqual(["'none'"]);
    expect(directive('base-uri')).toEqual(["'self'"]);
  });

  it('non contiene host esterni né script inline', () => {
    expect(APP_CSP).not.toMatch(/https?:/);
    expect(APP_CSP).not.toContain("'unsafe-inline'");
    expect(APP_CSP).not.toContain("'unsafe-eval'");
  });
});
