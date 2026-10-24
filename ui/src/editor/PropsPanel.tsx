import { useState, type ReactNode } from 'react';
import type { TranscriptionResult } from '../bridge/protocol';
import { Icon } from '../components/Icon';
import { transcriptionLanguages, useI18n, type MessageKey } from '../i18n/i18n';
import type { ExportFormat } from '../bridge/protocol';
import type { EditorState } from './useEditorState';
import { SpeakerDot } from './speakers';
import { speakerLabel } from './speakers';

function Section({ title, children }: { title: string; children: ReactNode }) {
  const [open, setOpen] = useState(true);
  return (
    <section className={`sec${open ? '' : ' closed'}`}>
      <button type="button" aria-expanded={open} onClick={() => setOpen(!open)}>
        <Icon name="chevron" />
        {title}
      </button>
      {open && <div className="secb">{children}</div>}
    </section>
  );
}

interface Props {
  result: TranscriptionResult;
  title: string;
  /** Rinominare è possibile solo per le voci salvate in cronologia. */
  onRename?: (title: string) => void;
  onExport: (format: ExportFormat) => void;
  editor: EditorState;
}

/** Pannello destro: proprietà del file, parlanti e controllo ortografico. */
export function PropsPanel({ result, title, onRename, onExport, editor }: Props) {
  const { t, formatDuration } = useI18n();
  const [name, setName] = useState(title);
  const language = result.detectedLanguage
    ? transcriptionLanguages.includes(result.detectedLanguage as (typeof transcriptionLanguages)[number])
      ? t(`lang_${result.detectedLanguage}` as MessageKey)
      : result.detectedLanguage
    : '—';
  const open = editor.issues;

  return (
    <aside className="props" aria-label={t('properties')}>
      <Section title={t('properties')}>
        {onRename ? (
          <input
            className="fname"
            aria-label={t('fileName')}
            value={name}
            spellCheck={false}
            onChange={(e) => setName(e.target.value)}
            onBlur={() => name.trim() !== title && onRename(name.trim())}
            onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
          />
        ) : (
          <div className="fname">{title}</div>
        )}
        <div className="kv">
          <span className="k">{t('language')}</span>
          <span className="v">
            {language} {result.detectedLanguage && <span className="chip auto">{t('languageDetected')}</span>}
          </span>
        </div>
        <div className="kv">
          <span className="k">{t('model')}</span>
          <span className="v" title={result.model.file}>
            {result.model.displayName}
          </span>
        </div>
        <div className="kv">
          <span className="k">{t('speakers')}</span>
          <span className="v">{editor.speakers.length}</span>
        </div>
        <div className="kv">
          <span className="k">{t('duration')}</span>
          <span className="v">{formatDuration(result.audioDuration)}</span>
        </div>
        <div className="kv">
          <span className="k">{t('elapsed')}</span>
          <span className="v">{formatDuration(result.elapsed)}</span>
        </div>
        <div className="kv">
          <span className="k">{t('subtitles')}</span>
          <button type="button" className="btn icon sm ghost" title={t('exportAs', { format: 'SRT' })} aria-label={t('exportAs', { format: 'SRT' })} onClick={() => onExport('srt')}>
            <Icon name="plus" />
          </button>
        </div>
        <p className="note">{t('playNoAudio')}</p>
        <p className="note" data-testid="model-used">
          {t('modelUsed', {
            model: result.model.displayName,
            file: result.model.file,
            runtime: result.model.runtime,
            language: '',
            duration: formatDuration(result.audioDuration),
            elapsed: formatDuration(result.elapsed),
          })}
        </p>
        <p className="note">{t('hallucinationHint')}</p>
      </Section>

      <div className="hr" />
      <Section title={t('speakersFound')}>
        {editor.speakers.map((s, i) => (
          <div className="spkrow" key={s.id}>
            <SpeakerDot speaker={s} />
            <input
              aria-label={t('speakerName', { n: i })}
              defaultValue={speakerLabel(s, i, t)}
              spellCheck={false}
              onBlur={(e) => editor.renameSpeaker(s.id, e.target.value === t('speakerDefault', { n: i }) ? '' : e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
            />
            <span className="note">{t('segmentsCount', { count: editor.segments.filter((g) => g.speaker === s.id).length })}</span>
          </div>
        ))}
        <p className="note">{t('speakersNote')}</p>
      </Section>

      <div className="hr" />
      <Section title={t('spellCheck')}>
        <div className="kv">
          <span className="k">{t('spellEngine')}</span>
          <span className="v">{t('spellEngineLocal')}</span>
        </div>
        <button type="button" className="btn pri sm block" onClick={editor.runSpellCheck}>
          <Icon name="spell" />
          {t('spellRun')}
        </button>
        <div className="note" role="status">
          {editor.spellStatus === 'done' && (open.length > 0 ? t('spellFound', { count: open.length }) : t('spellNone'))}
        </div>
        <button type="button" className="btn sm block" aria-describedby="reflow-note" onClick={editor.reorganize}>
          <Icon name="spell" />
          {t('reflowRun')}
        </button>
        <div className="note" id="reflow-note" role="status">
          {editor.reflowStatus ? (editor.reflowStatus.changed ? t('reflowDone', { before: editor.reflowStatus.before, after: editor.reflowStatus.after }) : t('reflowNone')) : t('reflowHint')}
        </div>
        <div className="issues">
          {open.map((i) => (
            <div className="issue" key={i.uid} id={`issue${i.uid}`}>
              <s>{i.original}</s> → <b>{i.suggestion}</b>
              <div className="note">{t('spellMessage')}</div>
              <div className="act">
                <button type="button" className="btn sm" onClick={() => editor.acceptIssue(i.uid)}>
                  {t('spellAccept')}
                </button>
                <button type="button" className="btn sm" onClick={() => editor.ignoreIssue(i.uid)}>
                  {t('spellIgnore')}
                </button>
              </div>
            </div>
          ))}
        </div>
      </Section>
    </aside>
  );
}
