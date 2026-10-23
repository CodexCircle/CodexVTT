import { useState } from 'react';
import type { ModelInfo } from '../bridge/protocol';
import { useI18n, type MessageKey } from '../i18n/i18n';
import { modelLimits } from '../state/modelLimits';
import type { CodexVttState } from '../state/useCodexVtt';
import { ConfirmDialog } from './ConfirmDialog';

export function ModelsPanel({ app }: { app: CodexVttState }) {
  const { t, formatBytes, formatMemory, reason, issueText } = useI18n();
  const [confirming, setConfirming] = useState<ModelInfo>();
  const [removing, setRemoving] = useState<ModelInfo>();
  const hw = app.system?.hardware;
  const limits = modelLimits(app.models, app.recommendation?.reasons);
  const issues = (m: ModelInfo) => m.issues.map((i) => issueText(m, i, limits)).join('; ');

  return (
    <section aria-labelledby="models-title" className="panel">
      <h2 id="models-title" className="section-title">
        {t('tabModels')}
      </h2>
      <p className="muted">{t('modelsIntro')}</p>

      <div className="models-top">
        {hw && (
          <div className="card card--dark hardware" aria-label={t('pcCapabilities')}>
            <h3>{t('yourPc')}</h3>
            <dl>
              <dt>{t('cpu')}</dt>
              <dd>
                {t('cpuValue', { cores: hw.logicalCores, features: hw.cpuFeatures || 'n/d' })}
                {!app.system?.supportsStandardRuntime && t('compatMode')}
              </dd>
              <dt>{t('memory')}</dt>
              <dd>{t('memoryValue', { total: formatMemory(hw.totalMemoryBytes) })}</dd>
              <dt>{t('freeDisk')}</dt>
              <dd>{formatBytes(hw.freeDiskBytes)}</dd>
            </dl>
          </div>
        )}

        {app.recommendation && (
          <div className="card recommendation">
            <h3>{t('recommendationTitle')}</h3>
            <ul>
              {app.recommendation.reasons.map((r) => (
                <li key={r.code}>{reason(r, limits)}</li>
              ))}
            </ul>
          </div>
        )}
      </div>

      {/* Importare richiede il dialog "Apri" sul PC del core: solo nell'app desktop. */}
      {app.desktop && (
        <div className="toolbar">
          <button type="button" onClick={() => void app.importModel()}>
            {t('importModel')}
          </button>
        </div>
      )}

      <ul className="model-list" aria-label={t('modelList')}>
        {app.models.map((m) => {
          const download = app.downloads[m.id];
          const recommended = app.recommendation?.recommendedId === m.id;
          const selected = app.activeModel?.id === m.id;
          return (
            <li key={m.id} className={`model tile ${selected ? 'model--selected' : ''}`} aria-label={m.displayName}>
              <div className="model__chip" aria-hidden="true">
                <span className="model__size">{formatBytes(m.sizeBytes)}</span>
                <span className="model__meter">
                  {[1, 2, 3, 4, 5].map((level) => (
                    <i key={level} className={level <= weight(m.sizeBytes) ? 'on' : undefined} />
                  ))}
                </span>
              </div>
              <div className="model__head">
                <strong>{m.displayName}</strong>
                {recommended && <span className="badge badge--accent">{t('recommended')}</span>}
                {selected && <span className="badge">{t('inUse')}</span>}
                <span className={`state state--${m.state}`}>{t(`state_${m.state}` as MessageKey)}</span>
              </div>
              <div className="model__meta">
                {t('modelMeta', {
                  size: formatBytes(m.sizeBytes),
                  ram: formatBytes(m.ramRequiredBytes),
                  quantization: m.quantization,
                  license: m.license,
                })}
              </div>
              {m.issues.length > 0 && <div className="model__issues">{t('warning', { issues: issues(m) })}</div>}
              {download && (
                <div className="download">
                  <progress
                    max={download.totalBytes}
                    value={download.receivedBytes}
                    aria-label={t('downloadOf', { model: m.displayName })}
                  />
                  <span>
                    {t('downloadProgress', {
                      received: formatBytes(download.receivedBytes),
                      total: formatBytes(download.totalBytes),
                      host: download.host,
                    })}
                  </span>
                </div>
              )}
              <div className="model__actions">
                {m.state === 'ready' && !selected && (
                  <button type="button" onClick={() => void app.selectModel(m.id)}>
                    {t('useModel')}
                  </button>
                )}
                {m.state === 'notInstalled' && m.downloadable && (
                  <button type="button" onClick={() => setConfirming(m)}>
                    {t('download')}
                  </button>
                )}
                {m.state === 'downloading' && (
                  <button type="button" onClick={() => void app.cancelDownload(m.id)}>
                    {t('cancelDownload')}
                  </button>
                )}
                {(m.state === 'unverified' || m.state === 'corrupted') && (
                  <button type="button" onClick={() => void app.verifyModel(m.id)}>
                    {t('verify')}
                  </button>
                )}
                {m.state === 'corrupted' && m.downloadable && (
                  <button type="button" onClick={() => setConfirming(m)}>
                    {t('redownload')}
                  </button>
                )}
                {m.state !== 'notInstalled' && m.state !== 'downloading' && m.state !== 'verifying' && (
                  <button type="button" className="secondary" onClick={() => setRemoving(m)}>
                    {t('remove')}
                  </button>
                )}
              </div>
            </li>
          );
        })}
      </ul>

      {removing && (
        <ConfirmDialog
          title={t('removeTitle', { model: removing.displayName })}
          confirmLabel={t('remove')}
          onConfirm={() => {
            void app.removeModel(removing.id);
            setRemoving(undefined);
          }}
          onCancel={() => setRemoving(undefined)}
        >
          <p>{t('removeText', { size: formatBytes(removing.sizeOnDiskBytes ?? removing.sizeBytes) })}</p>
        </ConfirmDialog>
      )}

      {confirming && (
        <ConfirmDialog
          title={t('downloadTitle', { model: confirming.displayName })}
          confirmLabel={t('downloadConfirm')}
          onConfirm={() => {
            void app.download(confirming.id);
            setConfirming(undefined);
          }}
          onCancel={() => setConfirming(undefined)}
        >
          <p>{t('downloadText', { size: formatBytes(confirming.sizeBytes) })}</p>
          {confirming.issues.length > 0 && <p className="warning">{t('warning', { issues: issues(confirming) })}</p>}
        </ConfirmDialog>
      )}
    </section>
  );
}

/** Peso del modello da 1 a 5 (scala logaritmica, da ~75 MB a ~3 GB) per il misuratore della scheda. */
function weight(bytes: number): number {
  return Math.min(5, Math.max(1, Math.round(Math.log2(bytes / 75e6)) + 1));
}
