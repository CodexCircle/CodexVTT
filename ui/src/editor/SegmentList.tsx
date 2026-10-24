import { memo, useEffect, useRef, useState, type ReactNode } from 'react';
import { Icon } from '../components/Icon';
import { useI18n } from '../i18n/i18n';
import { formatStamp, type EditSegment, type EditSpeaker } from './model';
import type { SpellIssue } from './spellcheck';
import { SpeakerDot, speakerLabel } from './speakers';

interface RowProps {
  segment: EditSegment;
  index: number;
  last: boolean;
  speaker: EditSpeaker;
  speakerIndex: number;
  current: boolean;
  issues: SpellIssue[];
  onSeek: (seconds: number) => void;
  onEdit: (index: number, text: string) => void;
  onMerge: (index: number) => void;
  onSpeaker: (index: number, anchor: HTMLElement) => void;
  onIssue: (uid: number) => void;
}

function Marked({ text, issues, onIssue }: { text: string; issues: SpellIssue[]; onIssue: (uid: number) => void }) {
  if (issues.length === 0) return <>{text}</>;
  const parts: ReactNode[] = [];
  let at = 0;
  for (const issue of [...issues].sort((a, b) => a.start - b.start)) {
    if (issue.start < at) continue;
    parts.push(text.slice(at, issue.start));
    parts.push(
      <mark key={issue.uid} onClick={() => onIssue(issue.uid)}>
        {text.slice(issue.start, issue.end)}
      </mark>,
    );
    at = issue.end;
  }
  parts.push(text.slice(at));
  return <>{parts}</>;
}

const Row = memo(function Row({ segment: g, index, last, speaker, speakerIndex, current, issues, onSeek, onEdit, onMerge, onSpeaker, onIssue }: RowProps) {
  const { t } = useI18n();
  const before = useRef(g.text);
  return (
    <div className={`seg${current ? ' cur' : ''}`} id={`seg${g.id}`}>
      <div className="spk">
        <button type="button" className="spkbtn" onClick={(e) => onSpeaker(index, e.currentTarget)} aria-haspopup="menu">
          <SpeakerDot speaker={speaker} />
          <span>{speakerLabel(speaker, speakerIndex, t)}</span>
        </button>
      </div>
      <div className="rule" />
      <div className="sbody">
        <button type="button" className="ts" onClick={() => onSeek(g.start)} aria-label={t('seekTo', { time: formatStamp(g.start) })}>
          {formatStamp(g.start)}
        </button>
        <div
          key={g.rev}
          className="txt"
          contentEditable
          suppressContentEditableWarning
          spellCheck={false}
          role="textbox"
          aria-multiline="true"
          aria-label={t('segmentText', { n: index + 1 })}
          onFocus={(e) => {
            before.current = e.currentTarget.textContent ?? '';
          }}
          onBlur={(e) => {
            const text = (e.currentTarget.textContent ?? '').trim();
            if (text !== before.current.trim()) onEdit(index, text);
          }}
        >
          <Marked text={g.text} issues={issues} onIssue={onIssue} />
        </div>
        <button type="button" className="ts" onClick={() => onSeek(g.end)} aria-label={t('seekTo', { time: formatStamp(g.end) })}>
          {formatStamp(g.end)}
        </button>
      </div>
      {!last && (
        <button type="button" className="merge" title={t('mergeNext')} aria-label={t('mergeNext')} onClick={() => onMerge(index)}>
          <Icon name="merge" />
        </button>
      )}
    </div>
  );
});

interface ListProps {
  segments: EditSegment[];
  speakers: EditSpeaker[];
  current: number;
  issues: SpellIssue[];
  onSeek: (seconds: number) => void;
  onEdit: (index: number, text: string) => void;
  onMerge: (index: number) => void;
  onAssign: (index: number, speakerId: string | 'new') => void;
  onIssue: (uid: number) => void;
}

export function SegmentList({ segments, speakers, current, issues, onSeek, onEdit, onMerge, onAssign, onIssue }: ListProps) {
  const { t } = useI18n();
  const [menu, setMenu] = useState<{ index: number; left: number; top: number }>();
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!menu) return;
    const close = (e: Event) => {
      if (e instanceof KeyboardEvent ? e.key === 'Escape' : !menuRef.current?.contains(e.target as Node)) setMenu(undefined);
    };
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', close);
    menuRef.current?.querySelector('button')?.focus();
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', close);
    };
  }, [menu]);

  return (
    <div className="segs" tabIndex={-1}>
      <div className="segs-in">
        {segments.map((g, i) => {
          const speakerIndex = Math.max(0, speakers.findIndex((s) => s.id === g.speaker));
          return (
            <Row
              key={g.id}
              segment={g}
              index={i}
              last={i === segments.length - 1}
              speaker={speakers[speakerIndex]}
              speakerIndex={speakerIndex}
              current={i === current}
              issues={issues.filter((x) => x.segmentId === g.id)}
              onSeek={onSeek}
              onEdit={onEdit}
              onMerge={onMerge}
              onSpeaker={(index, anchor) => {
                const r = anchor.getBoundingClientRect();
                setMenu({ index, left: r.left, top: r.bottom + 6 });
              }}
              onIssue={onIssue}
            />
          );
        })}
      </div>
      {menu && (
        <div className="popmenu" role="menu" ref={menuRef} style={{ left: menu.left, top: menu.top }}>
          {speakers.map((s, i) => (
            <button
              key={s.id}
              type="button"
              role="menuitem"
              onClick={() => {
                onAssign(menu.index, s.id);
                setMenu(undefined);
              }}
            >
              <SpeakerDot speaker={s} />
              {speakerLabel(s, i, t)}
            </button>
          ))}
          <button
            type="button"
            role="menuitem"
            onClick={() => {
              onAssign(menu.index, 'new');
              setMenu(undefined);
            }}
          >
            <Icon name="plus" />
            {t('newSpeaker')}
          </button>
        </div>
      )}
    </div>
  );
}
