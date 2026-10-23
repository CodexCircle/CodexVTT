import { describe, expect, it } from 'vitest';
import type { ModelInfo, SelectionReason } from '../bridge/protocol';
import { modelLimits } from '../state/modelLimits';
import { createI18n, resolveLocale } from './i18n';
import { messages } from './messages';

describe('i18n', () => {
  it('has the same keys and placeholders in Italian and English', () => {
    const it_ = messages.it;
    const en = messages.en;
    expect(Object.keys(en).sort()).toEqual(Object.keys(it_).sort());
    for (const key of Object.keys(it_) as (keyof typeof it_)[]) {
      const placeholders = (s: string) => (s.match(/\{\w+\}/g) ?? []).sort();
      expect(placeholders(en[key]), key).toEqual(placeholders(it_[key]));
      expect(en[key].includes('|'), key).toBe(it_[key].includes('|'));
    }
  });

  it('resolves the locale from settings or system', () => {
    expect(resolveLocale('en', 'it-IT')).toBe('en');
    expect(resolveLocale(null, 'it-IT')).toBe('it');
    expect(resolveLocale(undefined, 'de-DE')).toBe('en');
  });

  it('formats errors, numbers and reasons per locale', () => {
    const it_ = createI18n('it');
    const en = createI18n('en');
    expect(it_.describeError('audio_too_long').action).toContain('4 ore');
    expect(en.describeError('audio_too_long').action).toContain('4 hours');
    expect(en.describeError('unknown_code').title).toBe('An unexpected error occurred.');
    expect(it_.formatBytes(1_533_763_059)).toBe('1,5 GB');
    expect(en.formatBytes(1_533_763_059)).toBe('1.5 GB');
    expect(it_.formatBytes(344_000)).toBe('344 KB');
    expect(it_.formatBytes(12)).toBe('1 KB');
    expect(it_.formatMemory(17_179_869_184)).toBe('16 GB');
    expect(it_.formatMemory(17_200_000_000)).toBe('16 GB');
    expect(it_.formatDuration(3725)).toBe('1:02:05');
    expect(en.reason({ code: 'cpu_budget', args: { cores: '8', maxCost: '10' } })).toBe('8 logical cores: the heaviest models are left out.');
  });

  it('explains why a model is flagged, with the numbers of this PC', () => {
    const model = (id: string, displayName: string, relativeCost: number, ramRequiredBytes = 1e9): ModelInfo => ({
      id,
      displayName,
      file: `ggml-${id}.bin`,
      quantization: 'f16',
      sizeBytes: 1e9,
      ramRequiredBytes,
      relativeCost,
      multilingual: true,
      license: 'MIT',
      downloadable: true,
      state: 'notInstalled',
      issues: [],
      engine: 'whisper',
      preview: false,
      requiresLanguage: false,
    });
    const small = model('small', 'Small', 6);
    const turbo = model('turbo', 'Turbo', 10);
    const large = model('large-v3', 'Large v3', 32, 3.9e9);
    const reasons: SelectionReason[] = [
      { code: 'ram_limit', args: { totalBytes: '17200000000', limitBytes: '8600000000' } },
      { code: 'cpu_budget', args: { cores: '6', maxCost: '6' } },
    ];
    const limits = modelLimits([small, turbo, large], reasons);
    const it_ = createI18n('it');
    const en = createI18n('en');

    expect(limits.heaviestFit?.id).toBe('small');
    expect(it_.issueText(large, 'tooSlow', limits)).toBe(
      'troppo pesante per il processore di questo PC: richiede circa 5 volte il calcolo di Small, il più grande adatto',
    );
    expect(en.issueText(turbo, 'tooSlow', limits)).toBe('too demanding for this PC’s processor: needs about 2x the computation of Small, the largest one suited to it');
    expect(it_.issueText(large, 'insufficientMemory', limits)).toBe('RAM insufficiente: servono 3,9 GB, il limite per questo PC è 8,6 GB');
    expect(it_.reason(reasons[1], limits)).toBe('6 core logici: il modello più pesante adatto a questo processore è Small.');
    // Senza i limiti (profilo non ancora caricato) restano i messaggi brevi.
    expect(it_.issueText(large, 'tooSlow')).toBe('troppo pesante per il processore di questo PC');
    expect(it_.issueText(large, 'insufficientMemory')).toBe('RAM insufficiente');
  });
});
