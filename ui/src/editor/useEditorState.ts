import { useCallback, useRef, useState } from 'react';
import type { TranscriptionResult } from '../bridge/protocol';
import { firstSpeaker, segmentsFromResult, speakerColors, type EditSegment, type EditSpeaker } from './model';
import { reorganizeSegments } from './reflow';
import { checkSpelling, type SpellIssue } from './spellcheck';

interface Snapshot {
  segments: EditSegment[];
  speakers: EditSpeaker[];
}

/** Testo, parlanti e correzioni di una trascrizione aperta, con annulla/ripeti. Vive solo nella UI. */
export function useEditorState(result: TranscriptionResult) {
  const [state, setState] = useState<Snapshot>(() => ({ segments: segmentsFromResult(result), speakers: [firstSpeaker()] }));
  const [past, setPast] = useState<Snapshot[]>([]);
  const [future, setFuture] = useState<Snapshot[]>([]);
  const [issues, setIssues] = useState<SpellIssue[]>([]);
  const [spellStatus, setSpellStatus] = useState<'idle' | 'done'>('idle');
  const [reflowStatus, setReflowStatus] = useState<{ before: number; after: number; changed: boolean }>();
  const uid = useRef(0);
  const nextSegmentId = useRef(result.segments.length + 1);
  const nextRev = useRef(1);

  const apply = useCallback(
    (change: (s: Snapshot) => Snapshot) => {
      setPast((p) => [...p, state]);
      setFuture([]);
      setState(change(state));
    },
    [state],
  );

  const dropIssues = (segmentId: number) => setIssues((all) => all.filter((i) => i.segmentId !== segmentId));

  return {
    segments: state.segments,
    speakers: state.speakers,
    issues,
    spellStatus,
    reflowStatus,
    dirty: past.length > 0,
    canUndo: past.length > 0,
    canRedo: future.length > 0,

    editText(index: number, text: string) {
      const current = state.segments[index];
      if (!current || current.text === text) return;
      apply((s) => ({ ...s, segments: s.segments.map((g, i) => (i === index ? { ...g, text, rev: nextRev.current++ } : g)) }));
      dropIssues(current.id);
    },

    mergeWithNext(index: number) {
      const a = state.segments[index];
      const b = state.segments[index + 1];
      if (!a || !b) return;
      apply((s) => ({
        ...s,
        segments: s.segments.flatMap((g, i) =>
          i === index ? [{ ...a, text: `${a.text} ${b.text}`, end: b.end, rev: nextRev.current++ }] : i === index + 1 ? [] : [g],
        ),
      }));
      setIssues((all) => all.filter((i) => i.segmentId !== a.id && i.segmentId !== b.id));
    },

    assignSpeaker(index: number, speakerId: string) {
      if (state.segments[index]?.speaker === speakerId) return;
      apply((s) => ({ ...s, segments: s.segments.map((g, i) => (i === index ? { ...g, speaker: speakerId } : g)) }));
    },

    /** Aggiunge un parlante e lo restituisce (per assegnarlo subito, dal menu del segmento). */
    addSpeaker(): string {
      const n = state.speakers.length;
      const id = `S${n}_${nextRev.current++}`;
      apply((s) => ({ ...s, speakers: [...s.speakers, { id, color: speakerColors[n % speakerColors.length] }] }));
      return id;
    },

    renameSpeaker(id: string, name: string) {
      setState((s) => ({ ...s, speakers: s.speakers.map((p) => (p.id === id ? { ...p, name: name.trim() || undefined } : p)) }));
    },

    addSegment(at: number, duration: number) {
      const id = nextSegmentId.current++;
      apply((s) => ({
        ...s,
        segments: [...s.segments, { id, speaker: s.speakers[0].id, start: at, end: Math.min(duration, at + 2), text: '', rev: nextRev.current++ }].sort(
          (a, b) => a.start - b.start,
        ),
      }));
      return id;
    },

    undo() {
      const previous = past[past.length - 1];
      if (!previous) return;
      setPast(past.slice(0, -1));
      setFuture((f) => [state, ...f]);
      setState(previous);
      setIssues([]);
      setReflowStatus(undefined);
    },

    redo() {
      const [next, ...rest] = future;
      if (!next) return;
      setFuture(rest);
      setPast((p) => [...p, state]);
      setState(next);
      setIssues([]);
    },

    runSpellCheck() {
      setIssues(checkSpelling(state.segments, () => ++uid.current));
      setSpellStatus('done');
    },

    /** «Punteggiatura e frasi»: unisce i segmenti in blocchi più lunghi con regole su punteggiatura e tempi (nessun motore IA). Si annulla con Annulla. */
    reorganize() {
      const { segments, before, after } = reorganizeSegments(state.segments);
      const changed = segments.some((g, i) => g.text !== state.segments[i]?.text || g.end !== state.segments[i]?.end) || after !== before;
      setReflowStatus({ before, after, changed });
      if (!changed) return;
      // Ogni blocco cambiato riparte con un testo nuovo nel campo modificabile.
      const original = new Map(state.segments.map((g) => [g.id, g]));
      apply((s) => ({
        ...s,
        segments: segments.map((g) => (original.get(g.id)?.text === g.text ? g : { ...g, rev: nextRev.current++ })),
      }));
      setIssues([]);
    },

    acceptIssue(id: number) {
      const issue = issues.find((i) => i.uid === id);
      const index = state.segments.findIndex((g) => g.id === issue?.segmentId);
      if (!issue || index < 0) return;
      const g = state.segments[index];
      apply((s) => ({
        ...s,
        segments: s.segments.map((x, i) => (i === index ? { ...x, text: g.text.slice(0, issue.start) + issue.suggestion + g.text.slice(issue.end), rev: nextRev.current++ } : x)),
      }));
      // Le posizioni degli altri errori dello stesso segmento slittano della differenza di lunghezza.
      const delta = issue.suggestion.length - (issue.end - issue.start);
      setIssues((all) =>
        all
          .filter((i) => i.uid !== id)
          .map((i) => (i.segmentId === issue.segmentId && i.start > issue.start ? { ...i, start: i.start + delta, end: i.end + delta } : i)),
      );
    },

    ignoreIssue(id: number) {
      setIssues((all) => all.filter((i) => i.uid !== id));
    },
  };
}

export type EditorState = ReturnType<typeof useEditorState>;
