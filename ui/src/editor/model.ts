import type { TranscriptionResult } from '../bridge/protocol';

/** Segmento in modifica: parte dal risultato del core, le correzioni restano nella UI finché non c'è un comando per salvarle. */
export interface EditSegment {
  id: number;
  speaker: string;
  start: number;
  end: number;
  text: string;
  /** Cambia quando il testo è riscritto dall'esterno (unione, correzione): il campo modificabile riparte da zero. */
  rev: number;
}

export interface EditSpeaker {
  id: string;
  /** Assente = nome predefinito «Speaker N», tradotto dalla UI. */
  name?: string;
  color: [string, string];
}

export const speakerColors: [string, string][] = [
  ['#2f8f9d', '#1d4f6e'],
  ['#e0834a', '#b4472b'],
  ['#8a63d2', '#4b2f8a'],
  ['#4aa564', '#25683b'],
  ['#d4577f', '#8c2b50'],
];

export function firstSpeaker(): EditSpeaker {
  return { id: 'S0', color: speakerColors[0] };
}

export function segmentsFromResult(result: TranscriptionResult): EditSegment[] {
  return result.segments.map((s, i) => ({ id: i + 1, speaker: 'S0', start: s.start, end: s.end, text: s.text.trim(), rev: 0 }));
}

/** Durata della timeline: quella dell'audio, o la fine dell'ultimo segmento se è più lunga. */
export function timelineDuration(result: TranscriptionResult, segments: EditSegment[]): number {
  return Math.max(result.audioDuration, ...segments.map((s) => s.end), 1);
}

/** «01:05.32» o «1:01:05» per file lunghi: i centesimi servono solo a distinguere segmenti vicini. */
export function formatStamp(seconds: number): string {
  const t = Math.max(0, seconds);
  const h = Math.floor(t / 3600);
  const m = Math.floor((t % 3600) / 60);
  const s = Math.floor(t % 60);
  const mm = String(m).padStart(2, '0');
  const ss = String(s).padStart(2, '0');
  if (h > 0) return `${h}:${mm}:${ss}`;
  return `${mm}:${ss}.${String(Math.floor((t - Math.floor(t)) * 100)).padStart(2, '0')}`;
}

/** Etichetta del righello: «0:05», «1:02:00». */
export function formatRuler(seconds: number): string {
  const t = Math.round(seconds);
  const h = Math.floor(t / 3600);
  const m = Math.floor((t % 3600) / 60);
  const s = t % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
}

/** Indice del segmento che contiene l'istante, o -1. */
export function segmentAt(segments: EditSegment[], t: number): number {
  return segments.findIndex((g) => t >= g.start && t <= g.end + 0.05);
}
