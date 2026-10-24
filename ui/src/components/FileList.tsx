import { useI18n, type MessageKey } from '../i18n/i18n';
import type { CodexVttState, QueueItem } from '../state/useCodexVtt';
import { Icon } from './Icon';

export type Selection = { kind: 'queue'; key: string } | { kind: 'recent'; id: string } | { kind: 'open' } | null;

const dotClass: Record<QueueItem['status'], string> = { queued: '', working: 'run', done: 'done', failed: 'err' };

interface Props {
  app: CodexVttState;
  selection: Selection;
  running: boolean;
  paused: boolean;
  onSelectQueue: (item: QueueItem) => void;
  onSelectRecent: (id: string) => void;
  onNew: () => void;
  onAddFiles: () => void;
}

/** Barra laterale: coda del lavoro in corso e, sotto, le trascrizioni già fatte. */
export function FileList({ app, selection, running, paused, onSelectQueue, onSelectRecent, onNew, onAddFiles }: Props) {
  const { t, formatDuration, formatShortDate, describeError } = useI18n();
  const inQueue = app.queue.length > 0;
  const entries = (app.history?.entries ?? []).slice(0, 30);
  const openId = app.open?.historyId;
  const percent = app.progress?.percent ?? 0;
  const single = !inQueue && app.audio;

  return (
    <aside className="side" aria-label={t('files')}>
      <div className="list">
        {(inQueue || single) && (
          <>
            <h4>{inQueue ? t('queueTitle') : t('toTranscribe')}</h4>
            <ul aria-label={t('queueTitle')}>
              {single && (
                <li aria-label={app.audio!.info.fileName}>
                  <button type="button" className="file" onClick={onNew}>
                    <span className={`st ${app.jobId ? 'run' : ''}`} />
                    <span className="file__text">
                      <b>{app.audio!.info.fileName}</b>
                      <small>{app.jobId ? t('queueStatus_working') : formatDuration(app.audio!.info.duration)}</small>
                      {app.jobId && (
                        <span className="bar" aria-hidden="true">
                          <i style={{ width: `${percent}%` }} />
                        </span>
                      )}
                    </span>
                  </button>
                </li>
              )}
              {app.queue.map((item) => {
                const on = selection?.kind === 'queue' && selection.key === item.key;
                return (
                  <li key={item.key} aria-label={item.fileName} className="row">
                    <button
                      type="button"
                      className={`file${on ? ' on' : ''}`}
                      aria-current={on ? 'true' : undefined}
                      aria-label={item.status === 'done' ? t('queueOpenFile', { file: item.fileName }) : item.fileName}
                      onClick={() => onSelectQueue(item)}
                    >
                      <span className={`st ${dotClass[item.status]}`} />
                      <span className="file__text">
                        <b>{item.fileName}</b>
                        <small>
                          <span>{item.status === 'working' ? `${t('queueStatus_working')} · ${percent}%` : t(`queueStatus_${item.status}` as MessageKey)}</span>
                          {item.info && <span> · {formatDuration(item.info.duration)}</span>}
                        </small>
                        {item.error && (
                          <small className="err">
                            {describeError(item.error.code).title} {describeError(item.error.code).action}
                          </small>
                        )}
                        {item.status === 'working' && (
                          <span className="bar" aria-hidden="true">
                            <i style={{ width: `${percent}%` }} />
                          </span>
                        )}
                      </span>
                    </button>
                    {item.status !== 'working' && item.status !== 'done' && !running && (
                      <button type="button" className="remove" aria-label={t('queueRemove', { file: item.fileName })} onClick={() => app.removeFromQueue(item.key)}>
                        <Icon name="close" />
                      </button>
                    )}
                  </li>
                );
              })}
            </ul>
          </>
        )}

        <h4>{t('recent')}</h4>
        {entries.length === 0 ? (
          <p className="note pad">{t('recentEmpty')}</p>
        ) : (
          <ul aria-label={t('recent')}>
            {entries.map((e) => {
              const on = (selection?.kind === 'recent' && selection.id === e.id) || (selection?.kind === 'open' && openId === e.id);
              return (
                <li key={e.id}>
                  <button type="button" className={`file${on ? ' on' : ''}`} aria-current={on ? 'true' : undefined} onClick={() => onSelectRecent(e.id)}>
                    <span className="st done" />
                    <span className="file__text">
                      <b>{e.title || e.audioFileName}</b>
                      <small>
                        {formatShortDate(e.completedAt)} · {formatDuration(e.audioDuration)}
                      </small>
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </div>

      <div className="sidefoot">
        {running ? (
          <div className="sidefoot__row">
            {paused ? (
              <button type="button" className="btn sm pri" onClick={() => void app.resume()}>
                {t('resume')}
              </button>
            ) : (
              <button type="button" className="btn sm" disabled={app.progress?.status === 'queued' || app.progress?.status === 'decoding'} onClick={() => void app.pause()}>
                {t('pause')}
              </button>
            )}
            <button type="button" className="btn sm" onClick={() => void app.cancel()}>
              {t('cancel')}
            </button>
          </div>
        ) : (
          inQueue && (
            <div className="sidefoot__row">
              <button type="button" className="btn sm" disabled={app.uploading} onClick={onAddFiles}>
                {t('queueAdd')}
              </button>
              <button type="button" className="btn sm" onClick={() => app.clearQueue()}>
                {t('queueClear')}
              </button>
            </div>
          )
        )}
        <button type="button" className="btn sm block" onClick={onNew}>
          <Icon name="plus" />
          {t('newTranscription')}
        </button>
      </div>
    </aside>
  );
}
