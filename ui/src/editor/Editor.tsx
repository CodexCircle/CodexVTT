import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ExportFormat, TranscriptionResult } from '../bridge/protocol';
import { Icon } from '../components/Icon';
import { useI18n } from '../i18n/i18n';
import { segmentAt, timelineDuration } from './model';
import { PropsPanel } from './PropsPanel';
import { SegmentList } from './SegmentList';
import { speakerLabel } from './speakers';
import { speeds, Timeline } from './Timeline';
import { useEditorState } from './useEditorState';

interface Props {
  result: TranscriptionResult;
  title: string;
  onRename?: (title: string) => void;
  onExport: (format: ExportFormat) => void;
  onDirtyChange: (dirty: boolean) => void;
  onCopied: () => void;
}

/**
 * Editor di una trascrizione: segmenti con parlanti e tempi cliccabili, timeline con lettore e pannello delle proprietà.
 * Il lettore sposta il cursore sulla timeline: la riproduzione dell'audio non passa ancora dal bridge (#24).
 */
export function Editor({ result, title, onRename, onExport, onDirtyChange, onCopied }: Props) {
  const { t } = useI18n();
  const editor = useEditorState(result);
  const { segments, speakers } = editor;
  const duration = timelineDuration(result, segments);

  const [current, setCurrent] = useState(-1);
  const [playing, setPlaying] = useState(false);
  const [speed, setSpeed] = useState(1);
  const [pps, setPps] = useState(40);
  const time = useRef(0);
  const last = useRef(0);
  const frame = useRef(0);
  const headRef = useRef<HTMLDivElement>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const segmentsRef = useRef(segments);
  segmentsRef.current = segments;
  const ppsRef = useRef(pps);
  ppsRef.current = pps;

  useEffect(() => onDirtyChange(editor.dirty), [editor.dirty, onDirtyChange]);

  const place = useCallback(() => {
    if (headRef.current) headRef.current.style.left = `${time.current * ppsRef.current}px`;
    setCurrent(segmentAt(segmentsRef.current, time.current));
  }, []);
  useEffect(place, [place, pps, segments]);

  const seek = useCallback(
    (seconds: number) => {
      time.current = seconds;
      place();
    },
    [place],
  );

  const stop = useCallback(() => {
    cancelAnimationFrame(frame.current);
    setPlaying(false);
  }, []);

  // Il cursore avanza alla velocità scelta; se esce dalla vista la timeline scorre con lui.
  useEffect(() => {
    if (!playing) return;
    last.current = performance.now();
    const step = (now: number) => {
      time.current += ((now - last.current) / 1000) * speed;
      last.current = now;
      if (time.current >= duration) {
        time.current = 0;
        place();
        setPlaying(false);
        return;
      }
      place();
      const box = scrollRef.current;
      const x = time.current * ppsRef.current;
      if (box && (x > box.scrollLeft + box.clientWidth - 40 || x < box.scrollLeft)) box.scrollLeft = x - 60;
      frame.current = requestAnimationFrame(step);
    };
    frame.current = requestAnimationFrame(step);
    return () => cancelAnimationFrame(frame.current);
  }, [playing, speed, duration, place]);

  useEffect(() => stop, [stop]);

  // Spazio avvia e ferma, ma non mentre si scrive o su un controllo che lo usa già.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (e.code !== 'Space' || target.isContentEditable || /^(INPUT|SELECT|TEXTAREA|BUTTON)$/.test(target.tagName)) return;
      e.preventDefault();
      setPlaying((p) => !p);
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);

  const transport = useMemo(
    () => ({
      playing,
      speed,
      pps,
      toggle: () => setPlaying((p) => !p),
      cycleSpeed: () => setSpeed((s) => speeds[(speeds.indexOf(s) + 1) % speeds.length]),
      setPps,
    }),
    [playing, speed, pps],
  );

  const goToSegment = (index: number) => {
    seek(segments[index].start);
    document.getElementById(`seg${segments[index].id}`)?.scrollIntoView({ block: 'center', behavior: 'smooth' });
  };

  const copy = async () => {
    const lines = segments.map((g) => {
      const index = Math.max(0, speakers.findIndex((s) => s.id === g.speaker));
      return speakers.length > 1 ? `${speakerLabel(speakers[index], index, t)}: ${g.text}` : g.text;
    });
    try {
      await navigator.clipboard.writeText(lines.join('\n'));
      onCopied();
    } catch {
      // Appunti non disponibili: nessun messaggio di conferma.
    }
  };

  const assign = (index: number, to: string) => editor.assignSpeaker(index, to === 'new' ? editor.addSpeaker() : to);

  return (
    <>
      <section className="editor" aria-labelledby="result-title">
        <div className="etop">
          <button type="button" className="btn icon" title={t('undoEdit')} aria-label={t('undoEdit')} disabled={!editor.canUndo} onClick={editor.undo}>
            <Icon name="undo" />
          </button>
          <button type="button" className="btn icon" title={t('redoEdit')} aria-label={t('redoEdit')} disabled={!editor.canRedo} onClick={editor.redo}>
            <Icon name="redo" />
          </button>
          <span className="sp" />
          <h3 className="ttl" id="result-title">
            {t('resultTitle', { file: title })}
          </h3>
          <span className="sp" />
          <button type="button" className="btn sm" onClick={() => void copy()}>
            <Icon name="copy" />
            {t('copy')}
          </button>
        </div>
        <SegmentList
          segments={segments}
          speakers={speakers}
          current={current}
          issues={editor.issues}
          onSeek={seek}
          onEdit={editor.editText}
          onMerge={editor.mergeWithNext}
          onAssign={assign}
          onIssue={(uid) => document.getElementById(`issue${uid}`)?.scrollIntoView({ block: 'nearest' })}
        />
        <Timeline
          segments={segments}
          speakers={speakers}
          duration={duration}
          current={current}
          transport={transport}
          headRef={headRef}
          scrollRef={scrollRef}
          onSeek={seek}
          onBlock={goToSegment}
          onAddSpeaker={() => void editor.addSpeaker()}
          onAddSegment={() => editor.addSegment(time.current, duration)}
        />
      </section>
      <PropsPanel result={result} title={title} onRename={onRename} onExport={onExport} editor={editor} />
    </>
  );
}
