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
  /** Scrive i segmenti nella cronologia; assente se la trascrizione non è ancora in cronologia. Restituisce false se non è riuscito. */
  onSave?: (segments: { start: number; end: number; text: string }[]) => Promise<boolean>;
  /** Indirizzo dell'audio originale; senza (o se non si carica) il lettore muove solo il cursore sulla timeline. */
  loadAudio?: () => Promise<string | undefined>;
  onDirtyChange: (dirty: boolean) => void;
  onCopied: () => void;
}

/**
 * Editor di una trascrizione: segmenti con parlanti e tempi cliccabili, timeline con lettore e pannello delle proprietà.
 * Il lettore riproduce l'audio originale quando c'è (history.audio) e il cursore della timeline ne segue il tempo;
 * altrimenti muove solo il cursore con un orologio proprio.
 */
export function Editor({ result, title, onRename, onExport, onSave, loadAudio, onDirtyChange, onCopied }: Props) {
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

  const audioRef = useRef<HTMLAudioElement>(null);
  const [audioUrl, setAudioUrl] = useState<string>();
  const [audioLost, setAudioLost] = useState(false);
  // Una sola volta per trascrizione aperta (l'editor riparte da zero a ogni trascrizione).
  const loadAudioRef = useRef(loadAudio);
  useEffect(() => {
    let cancelled = false;
    void loadAudioRef.current?.().then((url) => {
      if (cancelled) return;
      setAudioUrl(url);
      setAudioLost(!url);
    });
    return () => {
      cancelled = true;
    };
  }, []);
  const hasAudio = audioUrl !== undefined && !audioLost;

  useEffect(() => onDirtyChange(editor.dirty), [editor.dirty, onDirtyChange]);

  const place = useCallback(() => {
    if (headRef.current) headRef.current.style.left = `${time.current * ppsRef.current}px`;
    setCurrent(segmentAt(segmentsRef.current, time.current));
  }, []);
  useEffect(place, [place, pps, segments]);

  const seek = useCallback(
    (seconds: number) => {
      time.current = seconds;
      if (audioRef.current) audioRef.current.currentTime = seconds;
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
      // Con l'audio il tempo è quello del lettore (resta in sintonia anche se si ferma a caricare).
      if (audioRef.current && hasAudio) time.current = audioRef.current.currentTime;
      else time.current += ((now - last.current) / 1000) * speed;
      last.current = now;
      if (time.current >= duration) {
        time.current = 0;
        if (audioRef.current) audioRef.current.currentTime = 0;
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
  }, [playing, speed, duration, place, hasAudio]);

  // Avvio, pausa e velocità del lettore audio seguono il trasporto; se il file non parte si torna al cursore senza audio.
  useEffect(() => {
    const el = audioRef.current;
    if (!el) return;
    el.playbackRate = speed;
    if (playing) void el.play().catch(() => setAudioLost(true));
    else el.pause();
  }, [playing, speed, hasAudio]);

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

  const [saving, setSaving] = useState(false);
  const save = async () => {
    if (!onSave || saving || !editor.dirty) return;
    const snapshot = editor.snapshot;
    setSaving(true);
    try {
      const ok = await onSave(snapshot.segments.filter((g) => g.text.trim()).map((g) => ({ start: g.start, end: g.end, text: g.text })));
      if (ok) editor.markSaved(snapshot);
    } finally {
      setSaving(false);
    }
  };
  const saveRef = useRef(save);
  saveRef.current = save;

  // Ctrl+S (o Cmd+S) salva anche mentre si scrive in un segmento.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        void saveRef.current();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);

  const assign = (index: number, to: string) => editor.assignSpeaker(index, to === 'new' ? editor.addSpeaker() : to);

  return (
    <>
      <section className="editor" aria-labelledby="result-title">
        {audioUrl && !audioLost && (
          <audio ref={audioRef} src={audioUrl} preload="metadata" onEnded={() => setPlaying(false)} onError={() => setAudioLost(true)} />
        )}
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
          <button type="button" className="btn pri sm" disabled={!onSave || !editor.dirty || saving} title={onSave ? t('saveEdits') : t('saveNeedsHistory')} onClick={() => void save()}>
            <Icon name="save" />
            {t('saveEdits')}
          </button>
          <button type="button" className="btn sm" onClick={() => void copy()}>
            <Icon name="copy" />
            {t('copy')}
          </button>
        </div>
        {audioLost && loadAudio && (
          <p className="note" role="status">
            {t('audioMissing')}
          </p>
        )}
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
