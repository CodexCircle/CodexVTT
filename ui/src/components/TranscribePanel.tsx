import { useEffect, useState } from 'react';
import { transcriptionLanguages, useI18n, type MessageKey } from '../i18n/i18n';
import type { CodexVttState } from '../state/useCodexVtt';
import { Mascot } from '../mascot/Mascot';
import { ResultView } from './ResultView';

const manageModels = '__manage';

export function TranscribePanel({ app, onOpenModels }: { app: CodexVttState; onOpenModels: () => void }) {
  const { t, formatBytes, formatDuration, formatRemaining, describeError } = useI18n();
  const [language, setLanguage] = useState(app.settings?.language ?? 'auto');
  useEffect(() => {
    if (app.settings?.language) setLanguage(app.settings.language);
  }, [app.settings?.language]);

  const running = Boolean(app.jobId) || app.progress?.status === 'queued' || app.queueRunning;
  const paused = running && app.progress?.status === 'paused';
  const inQueue = app.queue.length > 0;
  const pending = app.queue.filter((i) => i.status === 'queued' || (i.status === 'failed' && i.audioId)).length;
  const queuedDuration = app.queue.reduce((sum, i) => sum + (i.info?.duration ?? 0), 0);
  const ready = Boolean(app.audio) || (inQueue && pending > 0);
  // Canary e Cohere non rilevano la lingua: finché è «automatica» non si può avviare.
  const needsLanguage = Boolean(app.activeModel?.requiresLanguage) && language === 'auto';
  const result = app.open && <ResultView result={app.open.result} onExport={(f) => void app.exportAs(f)} />;

  if (app.readyModels.length === 0) {
    return (
      <section aria-labelledby="transcribe-title" className="panel">
        <h2 id="transcribe-title" className="section-title">
          {t('tabTranscribe')}
        </h2>
        <div className="card callout">
          <p>{t('needModel')}</p>
          <button type="button" onClick={onOpenModels}>
            {t('chooseModel')}
          </button>
        </div>
        {result}
      </section>
    );
  }

  const showMascot = app.settings?.showMascot !== false;
  const percent = app.progress?.percent ?? 0;
  // Tempo rimanente: stima del core dalla velocità misurata. Per la coda si aggiungono i file ancora da fare,
  // alla stessa velocità (stesso modello): è un'indicazione, non una promessa.
  const remaining = app.progress?.remaining;
  const queuedSeconds = app.queue.filter((i) => i.status === 'queued' && i.audioId).reduce((sum, i) => sum + (i.info?.duration ?? 0), 0);
  const queueRemaining =
    app.queueRunning && remaining !== undefined && app.progress?.speed && queuedSeconds > 0
      ? remaining + queuedSeconds / app.progress.speed
      : undefined;

  return (
    <section aria-labelledby="transcribe-title" className="panel">
      <h2 id="transcribe-title" className="section-title">
        {t('tabTranscribe')}
      </h2>

      <div className="stage">
        {showMascot && (
          <div className="stage__mascot">
            <Mascot
              activity={running && !paused ? 'working' : 'idle'}
              character={app.settings?.mascot}
              headphones={ready}
              seated
              size={180}
              testId={running ? 'mascot-listening' : 'mascot-stage'}
            />
          </div>
        )}

        <div className={`bar${ready ? ' bar--ready' : ''}${running ? ' bar--running' : ''}`}>
          {running && (
            <div
              role="progressbar"
              aria-label={t('progressLabel')}
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={percent}
              className="bar__progress"
            >
              <span style={{ width: `${percent}%` }} />
            </div>
          )}
          <button
            type="button"
            className="bar__file"
            onClick={() => void app.pickAudio()}
            disabled={running || app.uploading}
            title={app.audio || inQueue ? t('pickOtherAudio') : t('pickAudio')}
          >
            <svg className="icon" viewBox="0 0 24 24" width="22" height="22" aria-hidden="true" focusable="false">
              <path d="M9 18V5l12-2v13M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0zm12-2a3 3 0 1 1-6 0 3 3 0 0 1 6 0z" />
            </svg>
            <span className="bar__text" aria-live="polite">
              {app.uploading ? (
                <strong>{t('uploading')}</strong>
              ) : inQueue ? (
                <>
                  <strong>{t('queueCount', { count: app.queue.length })}</strong>
                  <span className="bar__meta">{formatDuration(queuedDuration)}</span>
                </>
              ) : app.audio ? (
                <>
                  <strong>{app.audio.info.fileName}</strong>
                  <span className="bar__meta">
                    {formatDuration(app.audio.info.duration)} · {app.audio.info.format.toUpperCase()} ·{' '}
                    {formatBytes(app.audio.info.sizeBytes)}
                  </span>
                </>
              ) : (
                <strong>{t('dropToStart')}</strong>
              )}
            </span>
          </button>

          {running ? (
            <>
              {paused ? (
                <button type="button" className="bar__action primary" onClick={() => void app.resume()}>
                  {t('resume')}
                </button>
              ) : (
                <button
                  type="button"
                  className="bar__action secondary"
                  disabled={app.progress?.status === 'queued' || app.progress?.status === 'decoding'}
                  onClick={() => void app.pause()}
                >
                  {t('pause')}
                </button>
              )}
              <button type="button" className="bar__action secondary" onClick={() => void app.cancel()}>
                {t('cancel')}
              </button>
            </>
          ) : inQueue ? (
            pending > 0 && (
              <button type="button" className="bar__action primary" disabled={needsLanguage} onClick={() => app.startQueue(language)}>
                {t('queueStart')}
              </button>
            )
          ) : (
            app.audio && (
              <button type="button" className="bar__action primary" disabled={needsLanguage} onClick={() => void app.start(language)}>
                {t('start')}
              </button>
            )
          )}

          <select
            className="bar__model"
            aria-label={t('model')}
            value={app.activeModel?.id}
            disabled={running}
            onChange={(e) => {
              if (e.target.value === manageModels) onOpenModels();
              else void app.selectModel(e.target.value);
            }}
          >
            {app.readyModels.map((m) => (
              <option key={m.id} value={m.id}>
                {m.displayName}
              </option>
            ))}
            <option value={manageModels}>{t('manageModels')}</option>
          </select>
        </div>

        {running && app.progress ? (
          <div className="stage__status" aria-live="polite">
            <strong>{paused ? t('pausedTitle') : t('listening')}</strong>{' '}
            <span className="muted">
              {app.progress.status === 'queued' || app.progress.status === 'decoding'
                ? t('preparing')
                : t('progressOf', {
                    percent,
                    processed: formatDuration(app.progress.processed),
                    total: formatDuration(app.progress.total),
                  })}
            </span>
            {app.progress.status === 'transcribing' && (
              <span className="muted eta" data-testid="eta">
                {' · '}
                {remaining !== undefined ? t('etaRemaining', { time: formatRemaining(remaining) }) : t('etaCalculating')}
                {queueRemaining !== undefined && ` · ${t('etaQueue', { time: formatRemaining(queueRemaining) })}`}
              </span>
            )}
          </div>
        ) : (
          <div className="stage__status">
            <label className="stage__lang">
              {t('language')}
              <select value={language} onChange={(e) => setLanguage(e.target.value)}>
                {transcriptionLanguages.map((code) => (
                  <option key={code} value={code}>
                    {t(`lang_${code}` as MessageKey)}
                  </option>
                ))}
              </select>
            </label>
            {needsLanguage && (
              <span className="warning" role="status">
                {t('languageRequiredHint')}
              </span>
            )}
            {!app.audio && !inQueue && !needsLanguage && <span className="muted">{t('noAudioHint')}</span>}
          </div>
        )}
      </div>

      {inQueue && (
        <div className="card queue">
          <div className="queue__head">
            <h3>{t('queueTitle')}</h3>
            {!running && (
              <div className="queue__actions">
                <button type="button" className="secondary" disabled={app.uploading} onClick={() => void app.addToQueue()}>
                  {t('queueAdd')}
                </button>
                <button type="button" className="secondary" onClick={() => app.clearQueue()}>
                  {t('queueClear')}
                </button>
              </div>
            )}
          </div>
          <ul className="queue__list" aria-label={t('queueTitle')}>
            {app.queue.map((item) => {
              const error = item.error ? describeError(item.error.code) : undefined;
              return (
                <li key={item.key} aria-label={item.fileName} className={`queue__item queue__item--${item.status}`}>
                  <div className="queue__file">
                    <strong>{item.fileName}</strong>
                    {item.info && <span className="muted">{formatDuration(item.info.duration)}</span>}
                    {error && (
                      <span className="queue__error">
                        {error.title} {error.action}
                      </span>
                    )}
                  </div>
                  <span className="queue__status">
                    {item.status === 'working' ? `${t('queueStatus_working')} · ${percent}%` : t(`queueStatus_${item.status}` as MessageKey)}
                  </span>
                  {item.status === 'done' && (
                    <button type="button" className="secondary" aria-label={t('queueOpenFile', { file: item.fileName })} onClick={() => app.openQueueItem(item.key)}>
                      {t('queueOpen')}
                    </button>
                  )}
                  {item.status !== 'working' && item.status !== 'done' && !running && (
                    <button type="button" className="secondary" aria-label={t('queueRemove', { file: item.fileName })} onClick={() => app.removeFromQueue(item.key)}>
                      ×
                    </button>
                  )}
                </li>
              );
            })}
          </ul>
        </div>
      )}

      {result}
    </section>
  );
}
