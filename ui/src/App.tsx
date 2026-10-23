import { useEffect, useState } from 'react';
import type { BridgeClient } from './bridge/client';
import { HistoryPanel } from './components/HistoryPanel';
import { Dock } from './components/Dock';
import { ModelsPanel } from './components/ModelsPanel';
import { SettingsPanel } from './components/SettingsPanel';
import { TranscribePanel } from './components/TranscribePanel';
import { I18nProvider, resolveLocale, useI18n } from './i18n/i18n';
import { Logo } from './components/Logo';
import { useCodexVtt, type CodexVttState } from './state/useCodexVtt';

type Tab = 'transcribe' | 'history' | 'models' | 'settings';

export function App({ client }: { client: BridgeClient }) {
  const app = useCodexVtt(client);
  const locale = resolveLocale(app.settings?.uiLanguage);

  const theme = app.settings?.theme;
  const palette = app.settings?.palette;

  useEffect(() => {
    document.documentElement.lang = locale;
  }, [locale]);

  // Tema scelto nelle impostazioni; senza attributo vale quello del sistema (prefers-color-scheme).
  useEffect(() => {
    const root = document.documentElement;
    if (theme) root.dataset.theme = theme;
    else delete root.dataset.theme;
  }, [theme]);

  // Tema di colore: senza attributo vale quello principale (Sabbia).
  useEffect(() => {
    const root = document.documentElement;
    if (palette && palette !== 'sabbia') root.dataset.palette = palette;
    else delete root.dataset.palette;
  }, [palette]);

  return (
    <I18nProvider locale={locale}>
      <Shell app={app} />
    </I18nProvider>
  );
}

function Shell({ app }: { app: CodexVttState }) {
  const { t, describeError } = useI18n();
  const [tab, setTab] = useState<Tab>('transcribe');
  const error = app.error ? describeError(app.error.code) : undefined;

  const tabs: { id: Tab; label: string; icon: string }[] = [
    {
      id: 'transcribe',
      label: t('tabTranscribe'),
      icon: 'M12 3a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3zM5 11a7 7 0 0 0 14 0M12 18v3',
    },
    { id: 'history', label: t('tabHistory'), icon: 'M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z' },
    { id: 'models', label: t('tabModels'), icon: 'M12 2.5 3.5 7v10l8.5 4.5 8.5-4.5V7zM3.5 7l8.5 4.5L20.5 7M12 11.5v10' },
  ];

  return (
    <div className="app">
      <nav className="nav" aria-label={t('sections')}>
        <span className="nav__logo" title="CodexVTT">
          <Logo size={48} />
        </span>
        <div className="nav__items">
          {tabs.map((item) => (
            <button
              key={item.id}
              type="button"
              className="nav__item"
              aria-current={tab === item.id ? 'page' : undefined}
              onClick={() => setTab(item.id)}
            >
              <Icon d={item.icon} />
              <span className="nav__label">{item.label}</span>
            </button>
          ))}
        </div>
        <div className="nav__bottom">
          <span className="nav__local" title={t('localBadgeTitle')}>
            <Icon d="M12 2.5 4.5 5.5v6c0 4.6 3.2 8.4 7.5 9.9 4.3-1.5 7.5-5.3 7.5-9.9v-6z M9 12l2 2 4-4" />
            <span className="sr-only">{t('localBadge')}</span>
          </span>
          <button
            type="button"
            className="nav__settings"
            title={t('tabSettings')}
            aria-label={t('tabSettings')}
            aria-current={tab === 'settings' ? 'page' : undefined}
            onClick={() => setTab('settings')}
          >
            <Icon d="M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
          </button>
        </div>
      </nav>

      <div className="content">
        {error && (
          <div role="alert" className="banner banner--error">
            <div>
              <strong>{error.title}</strong> {error.action}
              <span className="banner__code">
                {' '}
                ({t('errorCode')}: {app.error!.code})
              </span>
            </div>
            {app.error!.code === 'history_folder_blocked' && app.desktop && (
              <button type="button" className="link" onClick={() => void app.pickHistoryFolder()}>
                {t('changeFolder')}
              </button>
            )}
            <button type="button" className="link" onClick={app.dismissError}>
              {t('close')}
            </button>
          </div>
        )}
        {app.notice && (
          <div role="status" className="banner banner--info">
            <span>{t(app.notice.key, app.notice.params)}</span>
            <button type="button" className="link" onClick={app.dismissNotice}>
              {t('close')}
            </button>
          </div>
        )}

        <main>
          {tab === 'transcribe' && (
            <>
              <TranscribePanel app={app} onOpenModels={() => setTab('models')} />
              <Dock app={app} onOpenHistory={() => setTab('history')} />
            </>
          )}
          {tab === 'history' && <HistoryPanel app={app} onOpened={() => setTab('transcribe')} />}
          {tab === 'models' && <ModelsPanel app={app} />}
          {tab === 'settings' && <SettingsPanel app={app} />}
        </main>
      </div>
    </div>
  );
}

function Icon({ d }: { d: string }) {
  return (
    <svg className="icon" viewBox="0 0 24 24" width="22" height="22" aria-hidden="true" focusable="false">
      <path d={d} />
    </svg>
  );
}
