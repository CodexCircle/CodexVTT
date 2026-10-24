import { useEffect, useState } from 'react';
import { palettes, type Palette } from '../bridge/protocol';
import { transcriptionLanguages, useI18n, type MessageKey } from '../i18n/i18n';
import type { CodexVttState } from '../state/useCodexVtt';
import { FolderControl } from './FolderControl';

/** Campioni mostrati nel selettore: i colori di ciascun tema, dal più chiaro al più scuro. */
const swatches: Record<Palette | 'default', string[]> = {
  default: ['#ffffff', '#fafafa', '#bcd3ef', '#8fb6e6', '#0f0f10'],
  sabbia: ['#fbf7f4', '#e5ded2', '#a39382', '#685d54', '#232323'],
  natale: ['#fbeee0', '#efd1b2', '#8e2430', '#6c151e', '#0f3d3a'],
  halloween: ['#fdf1e3', '#f7d3a6', '#e0701f', '#a8430f', '#3b1d5c'],
  ciliegia: ['#FFD9D9', '#E8B7C2', '#670626', '#3a0a1d', '#2c0614'],
};

export function SettingsPanel({ app }: { app: CodexVttState }) {
  const { t } = useI18n();
  const s = app.settings;
  // Il cursore si sposta liberamente e il valore si salva quando lo si rilascia, non a ogni scatto.
  const [cpu, setCpu] = useState(s?.cpuLimitPercent ?? 100);
  useEffect(() => setCpu(s?.cpuLimitPercent ?? 100), [s?.cpuLimitPercent]);
  if (!s) return null;
  const cores = app.system?.hardware.logicalCores ?? 1;
  const threads = cpu >= 100 ? undefined : Math.max(1, Math.floor((cores * cpu) / 100));
  const commitCpu = () => {
    if (cpu !== (s.cpuLimitPercent ?? 100)) void app.saveSettings({ ...s, cpuLimitPercent: cpu });
  };
  const entries = app.history?.entries ?? [];
  const seconds = entries.reduce((sum, e) => sum + e.audioDuration, 0);
  // La barra si riempie a ogni ora di audio trascritto.
  const fill = entries.length === 0 ? 0 : Math.max(6, ((seconds % 3600) / 3600) * 100);

  return (
    <section aria-labelledby="settings-title" className="panel">
      <h2 id="settings-title" className="section-title">
        {t('tabSettings')}
      </h2>
      <section className="card stats" aria-labelledby="stats-title">
        <h3 id="stats-title">{t('dockStats')}</h3>
        <p className="stats__big">{t('dockMinutes', { minutes: Math.round(seconds / 60) })}</p>
        <div className="stats__bar">
          <span style={{ width: `${fill}%` }} />
        </div>
        <p className="muted">{t('dockCount', { count: entries.length })}</p>
      </section>

      <div className="card form settings">
        <label>
          {t('uiLanguage')}
          <select
            value={s.uiLanguage ?? 'system'}
            onChange={(e) =>
              void app.saveSettings({ ...s, uiLanguage: e.target.value === 'system' ? null : (e.target.value as 'it' | 'en') })
            }
          >
            <option value="system">{t('uiLanguageSystem')}</option>
            <option value="it">Italiano</option>
            <option value="en">English</option>
          </select>
        </label>
        <fieldset className="theme-picker">
          <legend>{t('palette')}</legend>
          <div className="theme-picker__options">
            {(['default', ...palettes] as const).map((palette) => (
              <label key={palette} className="theme-option">
                <input
                  type="radio"
                  name="palette"
                  value={palette}
                  checked={(s.palette ?? 'default') === palette}
                  onChange={() => void app.saveSettings({ ...s, palette: palette === 'default' ? null : palette })}
                />
                <span className="theme-option__swatches" aria-hidden="true">
                  {swatches[palette].map((color) => (
                    <i key={color} style={{ background: color }} />
                  ))}
                </span>
                <span>{t(`palette_${palette}`)}</span>
              </label>
            ))}
          </div>
        </fieldset>
        <label>
          {t('theme')}
          <select
            value={s.theme ?? 'system'}
            onChange={(e) =>
              void app.saveSettings({ ...s, theme: e.target.value === 'system' ? null : (e.target.value as 'light' | 'dark') })
            }
          >
            <option value="system">{t('themeSystem')}</option>
            <option value="light">{t('themeLight')}</option>
            <option value="dark">{t('themeDark')}</option>
          </select>
        </label>
        <label>
          {t('defaultLanguage')}
          <select value={s.language} onChange={(e) => void app.saveSettings({ ...s, language: e.target.value })}>
            {transcriptionLanguages.map((code) => (
              <option key={code} value={code}>
                {t(`lang_${code}` as MessageKey)}
              </option>
            ))}
          </select>
        </label>
        <label className="check">
          <input
            type="checkbox"
            checked={s.exportIncludeHeader}
            onChange={(e) => void app.saveSettings({ ...s, exportIncludeHeader: e.target.checked })}
          />
          {t('exportHeader')}
        </label>
      </div>

      <div className="card form">
        <h3>{t('performance')}</h3>
        <label htmlFor="cpu-limit">
          {t('cpuLimit', { percent: cpu })}
        </label>
        <input
          id="cpu-limit"
          type="range"
          min={10}
          max={100}
          step={5}
          value={cpu}
          aria-valuetext={`${cpu}%`}
          onChange={(e) => setCpu(Number(e.target.value))}
          onPointerUp={commitCpu}
          onKeyUp={commitCpu}
          onBlur={commitCpu}
        />
        <p className="muted">
          {threads === undefined ? t('cpuLimitNone', { cores }) : t('cpuLimitThreads', { threads, cores })} {t('cpuLimitNote')}
        </p>
        <label className="check">
          <input
            type="checkbox"
            checked={s.useGpu !== false}
            onChange={(e) => void app.saveSettings({ ...s, useGpu: e.target.checked })}
          />
          {t('useGpu')}
        </label>
        <p className="muted">{t('useGpuText')}</p>
        {app.gpuAtStart !== undefined && (s.useGpu !== false) !== app.gpuAtStart && (
          <p role="status" className="badge badge--warn">
            {t('useGpuRestart')}
          </p>
        )}
      </div>

      <div className="card">
        <h3>{t('historySettings')}</h3>
        <p className="folder">
          {t('historyFolder')}: <code>{app.history?.directory}</code>
        </p>
        {app.desktop && (
          <div className="toolbar">
            <button type="button" onClick={() => void app.openHistoryFolder()}>
              {t('openFolder')}
            </button>
          </div>
        )}
        {app.desktop && <FolderControl app={app} />}
      </div>

      <div className="card">
        <h3>{t('diagnostics')}</h3>
        <label className="check">
          <input
            type="checkbox"
            checked={s.diagnosticLogLevel === 'Debug' || s.diagnosticLogLevel === 'Trace'}
            onChange={(e) => void app.saveSettings({ ...s, diagnosticLogLevel: e.target.checked ? 'Debug' : null })}
          />
          {t('debugMode')}
        </label>
        <p className="muted">{t('debugModeText')}</p>
        {app.desktop && (
          <div className="toolbar">
            <button type="button" className="secondary" onClick={() => void app.openLogsFolder()}>
              {t('openLogs')}
            </button>
          </div>
        )}
      </div>

      {app.desktop && app.updates && (
        <div className="card" aria-labelledby="updates-title" role="group">
          <h3 id="updates-title">{t('updates')}</h3>
          {app.updates.state === 'unsupported' ? (
            <p className="muted">{t('updatesUnsupported')}</p>
          ) : (
            <>
              <p aria-live="polite">
                {app.updateProgress !== undefined
                  ? t('updatesDownloading', { percent: app.updateProgress })
                  : app.updates.state === 'available'
                    ? t('updatesAvailable', { version: app.updates.latestVersion ?? '', current: app.updates.currentVersion })
                    : app.checkingUpdates
                      ? t('updatesChecking')
                      : t('updatesUpToDate', { version: app.updates.currentVersion })}
              </p>
              {app.updates.state === 'available' && app.updates.notes && <p className="muted">{app.updates.notes}</p>}
              <div className="toolbar">
                {app.updates.state === 'available' ? (
                  <button type="button" disabled={app.updateProgress !== undefined} onClick={() => void app.applyUpdate()}>
                    {t('updatesInstall', { version: app.updates.latestVersion ?? '' })}
                  </button>
                ) : (
                  <button type="button" className="secondary" disabled={app.checkingUpdates} onClick={() => void app.checkUpdates()}>
                    {t('updatesCheck')}
                  </button>
                )}
              </div>
              <label className="check">
                <input
                  type="checkbox"
                  checked={s.autoCheckUpdates === true}
                  onChange={(e) => void app.saveSettings({ ...s, autoCheckUpdates: e.target.checked })}
                />
                {t('updatesAuto')}
              </label>
              <p className="muted">{t('updatesAutoText')}</p>
            </>
          )}
        </div>
      )}

      <div className="card">
        <h3>{t('privacy')}</h3>
        <p>{t('privacyText')}</p>
        <p className="muted">{t('version', { version: app.system?.appVersion ?? '' })}</p>
      </div>
    </section>
  );
}
