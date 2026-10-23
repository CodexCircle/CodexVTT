import { useState } from 'react';
import type { ExportFormat, TranscriptionResult } from '../bridge/protocol';
import { useI18n } from '../i18n/i18n';

export function ResultView({ result, onExport }: { result: TranscriptionResult; onExport: (format: ExportFormat) => void }) {
  const { t, formatDuration } = useI18n();
  const [view, setView] = useState<'text' | 'segments'>('text');
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(result.text);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
    }
  };

  return (
    <article className="card result" aria-labelledby="result-title">
      <header className="result__head">
        <h3 id="result-title">{t('resultTitle', { file: result.audioFileName })}</h3>
        <p className="muted" data-testid="model-used">
          {t('modelUsed', {
            model: result.model.displayName,
            file: result.model.file,
            runtime: result.model.runtime,
            language: result.detectedLanguage ? t('detectedLanguage', { language: result.detectedLanguage }) : '',
            duration: formatDuration(result.audioDuration),
            elapsed: formatDuration(result.elapsed),
          })}
        </p>
      </header>

      <div className="toolbar" role="group" aria-label={t('view')}>
        <button type="button" aria-pressed={view === 'text'} onClick={() => setView('text')}>
          {t('viewText')}
        </button>
        <button type="button" aria-pressed={view === 'segments'} onClick={() => setView('segments')}>
          {t('viewSegments')}
        </button>
      </div>

      {view === 'text' ? (
        <textarea className="result__text" readOnly value={result.text} aria-label={t('transcribedText')} rows={10} />
      ) : (
        <ol className="segments">
          {result.segments.map((s, i) => (
            <li key={i}>
              <time>{formatDuration(s.start)}</time> {s.text}
            </li>
          ))}
        </ol>
      )}

      <p className="hint">{t('hallucinationHint')}</p>

      <div className="toolbar">
        <button type="button" onClick={() => void copy()}>
          {copied ? t('copied') : t('copy')}
        </button>
        {(['txt', 'srt', 'vtt'] as const).map((format) => (
          <button key={format} type="button" onClick={() => onExport(format)}>
            {t('exportAs', { format: format.toUpperCase() })}
          </button>
        ))}
      </div>
    </article>
  );
}
