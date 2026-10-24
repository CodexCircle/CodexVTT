import { useI18n } from '../i18n/i18n';
import type { CodexVttState } from '../state/useCodexVtt';

/** Avanzamento del lavoro in corso, con il tempo rimanente stimato dal core (e per tutta la coda). */
export function RunStatus({ app }: { app: CodexVttState }) {
  const { t, formatDuration, formatRemaining } = useI18n();
  const progress = app.progress;
  const running = Boolean(app.jobId) || progress?.status === 'queued' || app.queueRunning;
  if (!running || !progress) return null;

  const paused = progress.status === 'paused';
  const percent = progress.percent;
  // Per la coda si aggiungono i file ancora da fare, alla stessa velocità (stesso modello): un'indicazione, non una promessa.
  const remaining = progress.remaining;
  const queuedSeconds = app.queue.filter((i) => i.status === 'queued' && i.audioId).reduce((sum, i) => sum + (i.info?.duration ?? 0), 0);
  const queueRemaining =
    app.queueRunning && remaining !== undefined && progress.speed && queuedSeconds > 0 ? remaining + queuedSeconds / progress.speed : undefined;

  return (
    <div className="run" aria-live="polite">
      <div
        role="progressbar"
        aria-label={t('progressLabel')}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent}
        className="meter"
      >
        <i style={{ width: `${percent}%` }} />
      </div>
      <p>
        <strong>{paused ? t('pausedTitle') : t('listening')}</strong>{' '}
        <span className="muted">
          {progress.status === 'queued' || progress.status === 'decoding'
            ? t('preparing')
            : t('progressOf', { percent, processed: formatDuration(progress.processed), total: formatDuration(progress.total) })}
        </span>
        {progress.status === 'transcribing' && (
          <span className="muted" data-testid="eta">
            {' · '}
            {remaining !== undefined
              ? t('etaRemaining', { time: formatRemaining(remaining) })
              : app.estimate?.basis === 'benchmark' && app.estimate.seconds
                ? `${t('etaCalculating')} ${t('etaInitial', { time: formatRemaining(app.estimate.seconds) })}`
                : t('etaCalculating')}
            {queueRemaining !== undefined && ` · ${t('etaQueue', { time: formatRemaining(queueRemaining) })}`}
          </span>
        )}
      </p>
    </div>
  );
}
