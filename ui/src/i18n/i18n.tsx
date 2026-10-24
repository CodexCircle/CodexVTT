import { createContext, useContext, useMemo, type ReactNode } from 'react';
import type { CompatibilityIssue, ModelInfo, SelectionReason } from '../bridge/protocol';
import type { ModelLimits } from '../state/modelLimits';
import { messages, type Locale, type MessageKey } from './messages';

export type { Locale, MessageKey };

export const transcriptionLanguages = ['auto', 'it', 'en', 'es', 'fr', 'de', 'pt'] as const;

/** Lingua dalle impostazioni, altrimenti dal sistema: italiano se il sistema è in italiano, altrimenti inglese. */
export function resolveLocale(setting: string | undefined | null, systemLanguage = navigator.language): Locale {
  if (setting === 'it' || setting === 'en') return setting;
  return systemLanguage.toLowerCase().startsWith('it') ? 'it' : 'en';
}

function format(template: string, params?: Record<string, string | number>): string {
  if (!params) return template;
  return template.replace(/\{(\w+)\}/g, (match, key: string) => (key in params ? String(params[key]) : match));
}

export function createI18n(locale: Locale) {
  const numberLocale = locale === 'it' ? 'it-IT' : 'en-US';
  const t = (key: MessageKey, params?: Record<string, string | number>) => format(messages[locale][key], params);

  const formatBytes = (bytes: number) =>
    bytes >= 1e9
      ? `${(bytes / 1e9).toLocaleString(numberLocale, { maximumFractionDigits: 1 })} GB`
      : bytes >= 1e6
        ? `${Math.round(bytes / 1e6).toLocaleString(numberLocale)} MB`
        : `${Math.max(1, Math.round(bytes / 1e3)).toLocaleString(numberLocale)} KB`;

  /** Memoria del PC come la mostra Windows: potenze di 1024 (16 GB per 17,2 miliardi di byte). */
  const formatMemory = (bytes: number) =>
    `${(bytes / 2 ** 30).toLocaleString(numberLocale, { maximumFractionDigits: 1 })} GB`;

  const formatDuration = (seconds: number) => {
    const total = Math.round(seconds);
    const h = Math.floor(total / 3600);
    const m = Math.floor((total % 3600) / 60);
    const s = total % 60;
    const mm = String(m).padStart(2, '0');
    const ss = String(s).padStart(2, '0');
    return h > 0 ? `${h}:${mm}:${ss}` : `${m}:${ss}`;
  };

  /** «meno di un minuto», «circa 5 min», «circa 1 h 20 min»: una stima, mai al secondo. */
  const formatRemaining = (seconds: number) => {
    const total = Math.max(0, seconds);
    if (total < 60) return t('etaLessThanMinute');
    const minutes = Math.round(total / 60);
    return minutes < 60
      ? t('etaMinutes', { minutes })
      : t('etaHours', { hours: Math.floor(minutes / 60), minutes: minutes % 60 });
  };

  const formatDate = (iso: string) => new Date(iso).toLocaleString(numberLocale, { dateStyle: 'medium', timeStyle: 'short' });

  /** Data breve per le schede, es. "15 ott". */
  const formatShortDate = (iso: string) => new Date(iso).toLocaleDateString(numberLocale, { day: 'numeric', month: 'short' });

  const describeError = (code: string) => {
    const key = `err_${code}` as MessageKey;
    const [title, action] = (messages[locale][key] ?? messages[locale].err_internal_error).split('|');
    return { title, action };
  };

  const reason = (r: SelectionReason, limits?: ModelLimits) => {
    const a = r.args ?? {};
    switch (r.code) {
      case 'ram_limit':
        return t('reason_ram_limit', { total: formatMemory(Number(a.totalBytes)), limit: formatBytes(Number(a.limitBytes)) });
      case 'cpu_budget':
        return limits?.heaviestFit
          ? t('reason_cpu_budget', { cores: a.cores, model: limits.heaviestFit.displayName })
          : t('reason_cpu_budget_generic', { cores: a.cores });
      case 'gpu_budget':
        return a.vramBytes !== undefined
          ? t('reason_gpu_budget', { vram: formatBytes(Number(a.vramBytes)) })
          : t('reason_gpu_budget_unknown');
      case 'compat_runtime':
        return t('reason_compat_runtime');
      case 'recommended':
        return t('reason_recommended', { model: a.model });
      case 'none_compatible':
        return t('reason_none_compatible');
      default:
        return r.code;
    }
  };

  /** Motivo per cui un modello è segnalato, con i numeri di questo PC quando si conoscono. */
  const issueText = (m: ModelInfo, issue: CompatibilityIssue, limits?: ModelLimits) => {
    if (issue === 'insufficientMemory' && limits?.ramLimitBytes !== undefined) {
      return t('issue_insufficientMemory_detail', { ram: formatBytes(m.ramRequiredBytes), limit: formatBytes(limits.ramLimitBytes) });
    }
    if (issue === 'insufficientVram' && limits?.vramBytes !== undefined) {
      return t('issue_insufficientVram_detail', { need: formatBytes(m.ramRequiredBytes), vram: formatBytes(limits.vramBytes) });
    }
    const fit = limits?.heaviestFit;
    if (issue === 'tooSlow' && fit && m.relativeCost > fit.relativeCost) {
      const times = Math.round(m.relativeCost / fit.relativeCost);
      return times >= 2
        ? t('issue_tooSlow_times', { times, model: fit.displayName })
        : t('issue_tooSlow_heavier', { model: fit.displayName });
    }
    return t(`issue_${issue}` as MessageKey);
  };

  return { locale, t, formatBytes, formatMemory, formatDuration, formatRemaining, formatDate, formatShortDate, describeError, reason, issueText };
}

export type I18n = ReturnType<typeof createI18n>;

const I18nContext = createContext<I18n>(createI18n('it'));

export function I18nProvider({ locale, children }: { locale: Locale; children: ReactNode }) {
  const value = useMemo(() => createI18n(locale), [locale]);
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18n {
  return useContext(I18nContext);
}
