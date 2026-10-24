import { transcriptionLanguages, useI18n, type MessageKey } from '../i18n/i18n';
import type { CodexVttState } from '../state/useCodexVtt';
import { Icon } from './Icon';
import { RunStatus } from './RunStatus';

const manageModels = '__manage';

interface Props {
  app: CodexVttState;
  language: string;
  onLanguage: (language: string) => void;
  needsLanguage: boolean;
  running: boolean;
  onPick: () => void;
  onOpenModels: () => void;
  onStart: () => void;
  onStartQueue: () => void;
}

/** «Nuova trascrizione»: scelta dei file, modello e lingua, avvio. Durante il lavoro mostra l'avanzamento. */
export function StartPage({ app, language, onLanguage, needsLanguage, running, onPick, onOpenModels, onStart, onStartQueue }: Props) {
  const { t, formatBytes, formatDuration } = useI18n();
  const inQueue = app.queue.length > 0;
  const pending = app.queue.filter((i) => i.status === 'queued' || (i.status === 'failed' && i.audioId)).length;
  const queuedDuration = app.queue.reduce((sum, i) => sum + (i.info?.duration ?? 0), 0);

  return (
    <div className="page">
      <h2 className="section-title">{t('newTranscription')}</h2>

      <button
        type="button"
        className="drop"
        onClick={onPick}
        disabled={running || app.uploading}
        aria-label={app.audio || inQueue ? t('pickOtherAudio') : t('dropToStart')}
      >
        <Icon name="dropUpload" size={28} />
        <b>{app.uploading ? t('uploading') : t('dropToStart')}</b>
        <span className="muted">{t('noAudioHint')}</span>
        <span className="btn pri" aria-hidden="true">
          {t('pickFiles')}
        </span>
      </button>

      <div className="grid2">
        <div className="card">
          <label htmlFor="start-model">{t('model')}</label>
          <select
            id="start-model"
            value={app.activeModel?.id}
            disabled={running}
            onChange={(e) => (e.target.value === manageModels ? onOpenModels() : void app.selectModel(e.target.value))}
          >
            {app.readyModels.map((m) => (
              <option key={m.id} value={m.id}>
                {m.displayName}
              </option>
            ))}
            <option value={manageModels}>{t('manageModels')}</option>
          </select>
        </div>
        <div className="card">
          <label htmlFor="start-language">{t('language')}</label>
          <select id="start-language" value={language} disabled={running} onChange={(e) => onLanguage(e.target.value)}>
            {transcriptionLanguages.map((code) => (
              <option key={code} value={code}>
                {t(`lang_${code}` as MessageKey)}
              </option>
            ))}
          </select>
        </div>
      </div>
      {needsLanguage && (
        <p className="warning" role="status">
          {t('languageRequiredHint')}
        </p>
      )}

      {running ? (
        <div className="card">
          <RunStatus app={app} />
        </div>
      ) : (
        (app.audio || inQueue) && (
          <div className="card">
            {inQueue ? (
              <>
                <div className="kv">
                  <b>{t('queueCount', { count: app.queue.length })}</b>
                  <span className="muted">{formatDuration(queuedDuration)}</span>
                </div>
                {pending > 0 && (
                  <button type="button" className="btn pri" disabled={needsLanguage} onClick={onStartQueue}>
                    {t('queueStart')}
                  </button>
                )}
              </>
            ) : (
              app.audio && (
                <>
                  <div className="kv">
                    <b>{app.audio.info.fileName}</b>
                    <span className="muted">
                      {formatDuration(app.audio.info.duration)} · {app.audio.info.format.toUpperCase()} · {formatBytes(app.audio.info.sizeBytes)}
                    </span>
                  </div>
                  <button type="button" className="btn pri" disabled={needsLanguage} onClick={onStart}>
                    {t('start')}
                  </button>
                </>
              )
            )}
          </div>
        )
      )}
    </div>
  );
}
