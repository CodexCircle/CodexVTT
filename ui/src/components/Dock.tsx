import { useI18n } from '../i18n/i18n';
import type { CodexVttState } from '../state/useCodexVtt';
import { AudioCover } from './AudioCover';

/** Pannello in fondo alla schermata principale: trascrizioni recenti. */
export function Dock({ app, onOpenHistory }: { app: CodexVttState; onOpenHistory: () => void }) {
  const { t } = useI18n();
  const entries = app.history?.entries ?? [];

  return (
    <aside className="dock" aria-label={t('recent')}>
      <div className="dock__head">
        <h3>{t('recent')}</h3>
        {entries.length > 0 && (
          <button type="button" className="dock__link" onClick={onOpenHistory}>
            {t('recentAll')}
          </button>
        )}
      </div>
      {entries.length === 0 ? (
        <p className="dock__muted">{t('recentEmpty')}</p>
      ) : (
        <div className="dock__recent">
          {entries.slice(0, 6).map((e) => (
            <button key={e.id} type="button" className="dock__item" onClick={() => void app.openFromHistory(e.id)}>
              <AudioCover name={e.audioFileName} size="sm" />
              <span>{e.title || e.audioFileName}</span>
            </button>
          ))}
        </div>
      )}
    </aside>
  );
}
