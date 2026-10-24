import { useId, useState } from 'react';
import { useI18n, type MessageKey } from '../i18n/i18n';
import type { CodexVttState } from '../state/useCodexVtt';

type Check = { phase: 'untested' } | { phase: 'testing' } | { phase: 'failed'; reason: string };

/**
 * Cambio della cartella di lavoro: «Cambia cartella…» apre la selezione, la scelta resta «non testata» finché
 * «Testa cartella» non riesce a scrivere, rileggere e cancellare un file di prova. Se il test fallisce la
 * cartella non viene salvata e resta la precedente.
 */
export function FolderControl({ app, compact = false }: { app: CodexVttState; compact?: boolean }) {
  const { t } = useI18n();
  const [pending, setPending] = useState<string>();
  const [check, setCheck] = useState<Check>({ phase: 'untested' });
  const statusId = useId();

  const choose = async () => {
    const directory = await app.pickHistoryFolder();
    if (!directory) return;
    setPending(directory);
    setCheck({ phase: 'untested' });
  };

  const test = async () => {
    if (!pending) return;
    setCheck({ phase: 'testing' });
    const result = await app.testHistoryFolder(pending);
    if (!result) {
      setCheck({ phase: 'failed', reason: 'cannot_write' });
      return;
    }
    if (!result.ok) {
      setCheck({ phase: 'failed', reason: result.error ?? 'cannot_write' });
      return;
    }
    // Solo a test riuscito la cartella viene adottata (il core rifà il test prima di salvarla).
    if (await app.applyHistoryFolder(pending)) setPending(undefined);
    else setCheck({ phase: 'failed', reason: 'cannot_write' });
  };

  return (
    <div className={`folder-control${compact ? ' folder-control--compact' : ''}`}>
      <div className="toolbar">
        <button type="button" className="secondary" onClick={() => void choose()} aria-describedby={pending ? statusId : undefined}>
          {t('changeFolder')}
        </button>
      </div>
      {pending && (
        <div className="folder-control__pending" id={statusId}>
          <code>{pending}</code>
          {check.phase === 'failed' ? (
            <span className="badge badge--danger">{t('folderTestFailedBadge')}</span>
          ) : (
            <span className="badge badge--warn">{t('folderUntested')}</span>
          )}
          <button type="button" className="btn pri" disabled={check.phase === 'testing'} onClick={() => void test()}>
            {check.phase === 'testing' ? t('folderTesting') : t('folderTest')}
          </button>
          <button type="button" className="link" disabled={check.phase === 'testing'} onClick={() => setPending(undefined)}>
            {t('cancel')}
          </button>
          {check.phase === 'failed' && (
            <p role="alert" className="folder-control__error">
              {t(`folderFail_${check.reason}` as MessageKey)} {t('folderKept')}
            </p>
          )}
          {check.phase === 'untested' && <p className="muted">{t('folderUntestedHint')}</p>}
        </div>
      )}
    </div>
  );
}
