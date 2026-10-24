import { useState } from 'react';
import type { BenchmarkReferenceInfo } from '../bridge/protocol';
import { useI18n, type MessageKey } from '../i18n/i18n';
import type { CodexVttState } from '../state/useCodexVtt';
import { ConfirmDialog } from './ConfirmDialog';

/**
 * Test bench: trascrive un file di riferimento scelto dall'utente con ogni modello installato e mostra la velocità misurata
 * su questo PC. Parte solo da un clic, non usa la rete e si può annullare.
 */
export function BenchPanel({ app }: { app: CodexVttState }) {
  const { t, formatShortDate, formatBytes } = useI18n();
  const [reference, setReference] = useState<BenchmarkReferenceInfo>();
  const [asking, setAsking] = useState(false);
  const { benchmark, benchmarkProgress: progress } = app;
  const busy = benchmark.running || Boolean(app.jobId) || app.queueRunning;
  const names = new Map(app.models.map((m) => [m.id, m.displayName]));
  // Mostrare l'indirizzo non scarica nulla: il download parte solo da «Scarica e avvia».
  const startOfficial = async () => {
    const info = reference ?? (await app.benchmarkReference());
    if (!info?.available) return;
    setReference(info);
    if (info.cached) void app.startBenchmark(true);
    else setAsking(true);
  };
  const number = (value: number) => new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value);

  return (
    <section className="card bench" aria-labelledby="bench-title">
      <h3 id="bench-title">{t('benchTitle')}</h3>
      <p className="muted">{t('benchIntro')}</p>
      <p className="muted">{t('benchWarn')}</p>
      <p className="muted">{t('benchOfficialFile')}</p>

      {app.desktop && (
        <div className="toolbar">
          {benchmark.running ? (
            <button type="button" className="secondary" onClick={() => void app.cancelBenchmark()}>
              {t('benchCancel')}
            </button>
          ) : (
            <>
              <button type="button" disabled={busy || app.readyModels.length === 0} aria-describedby="bench-hint" onClick={() => void startOfficial()}>
                {reference?.cached ? t('benchOfficialStartCached') : t('benchOfficialStart')}
              </button>
              <button type="button" className="secondary" disabled={busy || app.readyModels.length === 0} aria-describedby="bench-hint" onClick={() => void app.startBenchmark()}>
                {t('benchStart')}
              </button>
            </>
          )}
        </div>
      )}
      <p id="bench-hint" className="muted">
        {app.readyModels.length === 0 ? t('benchNoModels') : t('benchFileHint')}
      </p>

      <div role="status" aria-live="polite">
        {benchmark.running && progress && (
          <p>{t('benchRunning', { model: names.get(progress.modelId) ?? progress.modelId, n: progress.index + 1, total: progress.total })}</p>
        )}
      </div>
      {benchmark.running && (
        <progress aria-label={t('benchTitle')} max={progress?.total ?? 1} value={progress ? progress.index + (progress.phase === 'running' ? 0 : 1) : 0} />
      )}

      <h4>{t('benchResults')}</h4>
      {benchmark.entries.length === 0 ? (
        <p className="muted">{t('benchNone')}</p>
      ) : (
        <table className="bench__table">
          <caption className="sr-only">{t('benchResults')}</caption>
          <thead>
            <tr>
              <th scope="col">{t('benchColModel')}</th>
              <th scope="col">{t('benchColRuntime')}</th>
              <th scope="col">{t('benchColSpeed')}</th>
              <th scope="col">{t('benchColFixed')}</th>
              <th scope="col">{t('benchColAccuracy')}</th>
              <th scope="col">{t('benchColDate')}</th>
              <th scope="col">{t('benchColStatus')}</th>
            </tr>
          </thead>
          <tbody>
            {benchmark.entries.map((e) => (
              <tr key={`${e.modelId}-${e.runtime}`} className={e.stale === 'none' ? undefined : 'bench__stale'}>
                <th scope="row">{names.get(e.modelId) ?? e.modelId}</th>
                <td>{e.runtime}</td>
                <td>{t('benchSpeed', { speed: number(e.speed) })}</td>
                <td>{t('benchFixed', { seconds: number(e.loadSeconds) })}</td>
                <td>{e.accuracy === undefined ? <span title={t('benchAccuracyNone')}>—</span> : `${number(e.accuracy)}%`}</td>
                <td>{formatShortDate(e.measuredAt)}</td>
                <td>{e.stale === 'none' ? t('benchValid') : t(`benchStale_${e.stale}` as MessageKey)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {asking && reference && (
        <ConfirmDialog
          title={t('benchOfficialTitle')}
          confirmLabel={t('benchOfficialConfirm')}
          onConfirm={() => {
            setAsking(false);
            void app.startBenchmark(true).then(() => app.benchmarkReference().then(setReference));
          }}
          onCancel={() => setAsking(false)}
        >
          <p>{t('benchOfficialText', { title: reference.title, size: formatBytes(reference.sizeBytes) })}</p>
          <p>
            <code>{t('benchOfficialUrl', { url: reference.url })}</code>
          </p>
          <p>
            <code>{t('benchOfficialHash', { hash: reference.sha256 })}</code>
          </p>
          <p>{t('benchOfficialLicense', { license: reference.license })}</p>
        </ConfirmDialog>
      )}
    </section>
  );
}
