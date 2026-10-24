import { Fragment, useEffect, useState } from 'react';
import type { BenchmarkEntry, BenchmarkReferenceInfo, BenchmarkScenario, BenchmarkStale } from '../bridge/protocol';
import { useI18n, type MessageKey } from '../i18n/i18n';
import type { CodexVttState } from '../state/useCodexVtt';
import { ConfirmDialog } from './ConfirmDialog';

interface ModelRow {
  modelId: string;
  runtime: string;
  speed: number;
  loadSeconds: number;
  measuredAt: string;
  stale: BenchmarkStale;
}

/**
 * Una riga per modello: runtime della misura più recente, velocità sul totale dell'audio provato (secondi di audio diviso
 * secondi di lavoro) e tempo fisso medio. Se una misura non vale più, la riga lo dice.
 */
function combine(entries: BenchmarkEntry[]): ModelRow[] {
  const byModel = new Map<string, BenchmarkEntry[]>();
  for (const e of entries) byModel.set(e.modelId, [...(byModel.get(e.modelId) ?? []), e]);
  return [...byModel.entries()].map(([modelId, list]) => {
    const newest = list.reduce((a, b) => (b.measuredAt > a.measuredAt ? b : a));
    const same = list.filter((e) => e.runtime === newest.runtime && e.speed > 0);
    const audio = same.reduce((sum, e) => sum + e.audioSeconds, 0);
    const work = same.reduce((sum, e) => sum + e.audioSeconds / e.speed, 0);
    return {
      modelId,
      runtime: newest.runtime,
      speed: work > 0 ? audio / work : newest.speed,
      loadSeconds: same.length > 0 ? same.reduce((sum, e) => sum + e.loadSeconds, 0) / same.length : newest.loadSeconds,
      measuredAt: newest.measuredAt,
      stale: list.find((e) => e.stale !== 'none')?.stale ?? 'none',
    };
  });
}

/**
 * Test bench: trascrive dei brani di riferimento con ogni modello installato e mostra velocità e affidabilità misurate su
 * questo PC. I brani ufficiali sono in italiano e in inglese, puliti e con rumore, così l'affidabilità si legge per lingua
 * e per condizione. Parte solo da un clic, si può annullare e scarica i brani ufficiali solo se lo chiedi.
 */
export function BenchPanel({ app }: { app: CodexVttState }) {
  const { t, formatShortDate, formatBytes, languageName } = useI18n();
  const [reference, setReference] = useState<BenchmarkReferenceInfo>();
  const [asking, setAsking] = useState(false);
  // Cosa provare: lingue e condizioni a spunta. Di base la lingua scelta e l'inglese, pulito e con rumore.
  const [pickedLanguages, setPickedLanguages] = useState<string[]>();
  const [pickedScenarios, setPickedScenarios] = useState<BenchmarkScenario[]>(['clean', 'noisy']);
  const { benchmark, benchmarkProgress: progress } = app;
  const loadReference = app.benchmarkReference;
  useEffect(() => {
    // Leggere l'elenco dei brani non scarica nulla: serve a proporre le lingue disponibili.
    void loadReference().then((info) => info && setReference(info));
  }, [loadReference]);
  const availableLanguages = [...new Set((reference?.clips ?? []).map((c) => c.language))].sort();
  // Con lingua automatica (o una lingua senza brani) si propongono tutte; altrimenti la lingua scelta più l'inglese.
  const own = availableLanguages.find((l) => l === app.settings?.language);
  const languagesChosen = pickedLanguages ?? (own ? availableLanguages.filter((l) => l === own || l === 'en') : availableLanguages);
  const chosenClips = (reference?.clips ?? []).filter((c) => languagesChosen.includes(c.language) && pickedScenarios.includes(c.scenario));
  const chosenFiles = (reference?.files ?? []).filter((f) => languagesChosen.includes(f.language));
  const toggle = <T extends string>(list: T[], value: T) => (list.includes(value) ? list.filter((v) => v !== value) : [...list, value]);
  const busy = benchmark.running || Boolean(app.jobId) || app.queueRunning;
  const names = new Map(app.models.map((m) => [m.id, m.displayName]));
  // Mostrare l'indirizzo non scarica nulla: il download parte solo da «Scarica e avvia».
  const selection = { languages: languagesChosen, scenarios: pickedScenarios };
  const startOfficial = async () => {
    const info = reference ?? (await app.benchmarkReference());
    if (!info?.available || chosenClips.length === 0) return;
    setReference(info);
    if (chosenFiles.every((f) => f.cached)) void app.startBenchmark(true, selection);
    else setAsking(true);
  };
  const number = (value: number) => new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value);
  const rows = combine(benchmark.entries);
  const languages = [...new Set(benchmark.reliability.map((r) => r.language))].sort();
  const reliabilityOf = (modelId: string, language: string) => benchmark.reliability.find((r) => r.modelId === modelId && r.language === language);
  // Dettaglio: una riga per modello e brano, con la stessa lettura pulita e con rumore affiancate.
  const detail = [
    ...benchmark.entries
      .filter((e) => e.accuracy !== undefined && e.clipId !== undefined)
      .reduce((map, e) => {
        const key = `${e.modelId}|${e.clipId!.replace(/-(clean|noisy)-/, '-')}`;
        const row = map.get(key) ?? { modelId: e.modelId, clip: e.clipId!.replace(/-(clean|noisy)-/, '-'), title: '', language: e.language ?? '', clean: undefined as number | undefined, noisy: undefined as number | undefined };
        if (e.scenario === 'noisy') row.noisy = e.accuracy;
        else {
          row.clean = e.accuracy;
          row.title = e.clipTitle ?? row.clip;
        }
        return map.set(key, row);
      }, new Map<string, { modelId: string; clip: string; title: string; language: string; clean?: number; noisy?: number }>())
      .values(),
  ].sort((a, b) => a.language.localeCompare(b.language) || a.clip.localeCompare(b.clip) || a.modelId.localeCompare(b.modelId));
  const percent = (value?: number) => (value === undefined ? '—' : `${number(value)}%`);
  // In ogni lingua spicca la media migliore (a parità di arrotondamento, tutte quelle a pari merito).
  const bestOverall = (language: string) =>
    Math.max(-1, ...benchmark.reliability.filter((r) => r.language === language && r.overall !== undefined).map((r) => r.overall as number));

  return (
    <section className="card bench" aria-labelledby="bench-title">
      <h3 id="bench-title">{t('benchTitle')}</h3>
      <p className="muted">{t('benchIntro')}</p>
      <p className="muted">{t('benchWarn')}</p>
      <p className="muted">{t('benchOfficialFile')}</p>

      {app.desktop && reference?.available && !benchmark.running && (
        <fieldset className="bench__select">
          <legend>{t('benchSelectTitle')}</legend>
          <div role="group" aria-label={t('benchSelectLanguages')}>
            <strong>{t('benchSelectLanguages')}</strong>
            {availableLanguages.map((l) => (
              <label key={l}>
                <input type="checkbox" checked={languagesChosen.includes(l)} onChange={() => setPickedLanguages(toggle(languagesChosen, l))} /> {languageName(l)}
              </label>
            ))}
          </div>
          <div role="group" aria-label={t('benchSelectScenarios')}>
            <strong>{t('benchSelectScenarios')}</strong>
            {(['clean', 'noisy'] as const).map((s) => (
              <label key={s}>
                <input type="checkbox" checked={pickedScenarios.includes(s)} onChange={() => setPickedScenarios(toggle(pickedScenarios, s))} /> {t(s === 'clean' ? 'benchColClean' : 'benchColNoisy')}
              </label>
            ))}
          </div>
          <p className="muted" role="status">
            {chosenClips.length === 0
              ? t('benchSelectNone')
              : t('benchSelectSummary', { clips: chosenClips.length, minutes: Math.round(chosenClips.reduce((sum, c) => sum + c.seconds, 0) / 60) })}
          </p>
        </fieldset>
      )}

      {app.desktop && (
        <div className="toolbar">
          {benchmark.running ? (
            <button type="button" className="secondary" onClick={() => void app.cancelBenchmark()}>
              {t('benchCancel')}
            </button>
          ) : (
            <>
              <button type="button" disabled={busy || app.readyModels.length === 0 || chosenClips.length === 0} aria-describedby="bench-hint" onClick={() => void startOfficial()}>
                {chosenFiles.length > 0 && chosenFiles.every((f) => f.cached) ? t('benchOfficialStartCached') : t('benchOfficialStart')}
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
      {rows.length === 0 ? (
        <p className="muted">{t('benchNone')}</p>
      ) : (
        <>
          <table className="bench__table">
            <caption className="sr-only">{t('benchResults')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('benchColModel')}</th>
                <th scope="col">{t('benchColRuntime')}</th>
                <th scope="col">{t('benchColSpeed')}</th>
                <th scope="col">{t('benchColFixed')}</th>
                <th scope="col">{t('benchColDate')}</th>
                <th scope="col">{t('benchColStatus')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((e) => (
                <tr key={e.modelId} className={e.stale === 'none' ? undefined : 'bench__stale'}>
                  <th scope="row">{names.get(e.modelId) ?? e.modelId}</th>
                  <td>{e.runtime}</td>
                  <td>{t('benchSpeed', { speed: number(e.speed) })}</td>
                  <td>{t('benchFixed', { seconds: number(e.loadSeconds) })}</td>
                  <td>{formatShortDate(e.measuredAt)}</td>
                  <td>{e.stale === 'none' ? t('benchValid') : t(`benchStale_${e.stale}` as MessageKey)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <h4>{t('benchReliabilityTitle')}</h4>
          {languages.length === 0 ? (
            <p className="muted">{t('benchAccuracyNone')}</p>
          ) : (
            <>
              <table className="bench__table bench__reliability">
                <caption className="sr-only">{t('benchReliabilityTitle')}</caption>
                <thead>
                  <tr>
                    <th scope="col" rowSpan={2}>
                      {t('benchColModel')}
                    </th>
                    {languages.map((l) => (
                      <th key={l} scope="colgroup" colSpan={3}>
                        {languageName(l)}
                      </th>
                    ))}
                  </tr>
                  <tr>
                    {languages.map((l) => (
                      <Fragment key={l}>
                        <th scope="col">{t('benchColClean')}</th>
                        <th scope="col">{t('benchColNoisy')}</th>
                        <th scope="col">{t('benchColAverage')}</th>
                      </Fragment>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((e) => (
                    <tr key={e.modelId} className={e.stale === 'none' ? undefined : 'bench__stale'}>
                      <th scope="row">{names.get(e.modelId) ?? e.modelId}</th>
                      {languages.map((l) => {
                        const r = reliabilityOf(e.modelId, l);
                        return (
                          <Fragment key={l}>
                            <td>{percent(r?.clean)}</td>
                            <td>{percent(r?.noisy)}</td>
                            <td className={r?.overall !== undefined && r.overall === bestOverall(l) ? 'bench__best' : undefined}>
                              {r?.overall !== undefined && r.overall === bestOverall(l) ? <strong>{percent(r.overall)}</strong> : percent(r?.overall)}
                            </td>
                          </Fragment>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
              <p className="muted">{t('benchReliabilityNote')}</p>
              <details className="bench__detail">
                <summary>{t('benchDetailTitle')}</summary>
                <table className="bench__table">
                  <caption className="sr-only">{t('benchDetailTitle')}</caption>
                  <thead>
                    <tr>
                      <th scope="col">{t('benchColClip')}</th>
                      <th scope="col">{t('benchColModel')}</th>
                      <th scope="col">{t('benchColClean')}</th>
                      <th scope="col">{t('benchColNoisy')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {detail.map((d) => (
                      <tr key={`${d.modelId}|${d.clip}`}>
                        <th scope="row">{d.title || d.clip}</th>
                        <td>{names.get(d.modelId) ?? d.modelId}</td>
                        <td>{percent(d.clean)}</td>
                        <td>{percent(d.noisy)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </details>
            </>
          )}
        </>
      )}
      {asking && reference && (
        <ConfirmDialog
          title={t('benchOfficialTitle')}
          confirmLabel={t('benchOfficialConfirm')}
          onConfirm={() => {
            setAsking(false);
            void app.startBenchmark(true, selection).then(() => app.benchmarkReference().then(setReference));
          }}
          onCancel={() => setAsking(false)}
        >
          <p>{t('benchOfficialText', { count: chosenFiles.filter((f) => !f.cached).length, size: formatBytes(chosenFiles.filter((f) => !f.cached).reduce((sum, f) => sum + f.sizeBytes, 0)) })}</p>
          <ul>
            {chosenFiles
              .filter((f) => !f.cached)
              .map((f) => (
                <li key={f.id}>
                  {f.title} ({languageName(f.language)}, {formatBytes(f.sizeBytes)})
                  <br />
                  <code>{t('benchOfficialUrl', { url: f.url })}</code>
                  <br />
                  <code>{t('benchOfficialHash', { hash: f.sha256 })}</code>
                </li>
              ))}
          </ul>
          <p>{t('benchOfficialLicense', { license: reference.license })}</p>
        </ConfirmDialog>
      )}
    </section>
  );
}
