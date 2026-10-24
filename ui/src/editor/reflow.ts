import type { EditSegment } from './model';

/** Parametri della riorganizzazione, in secondi. */
export interface ReflowOptions {
  /** Un blocco più corto di così si unisce al successivo, se possibile. */
  minDuration: number;
  /** Un blocco non supera questa durata, salvo un singolo segmento che già la supera. */
  maxDuration: number;
  /** Una pausa più lunga di così separa sempre due blocchi. */
  maxGap: number;
}

export const defaultReflow: ReflowOptions = { minDuration: 12, maxDuration: 25, maxGap: 2 };

export interface ReflowResult {
  segments: EditSegment[];
  before: number;
  after: number;
}

const sentenceEnd = /[.!?…]["»”’)\]]*\s*$/u;
const startsLower = /^[\p{Ll}]/u;

const endsSentence = (text: string) => sentenceEnd.test(text.trim());

/** Prima lettera maiuscola, senza toccare il resto. */
const capitalize = (text: string) => text.replace(/^(\P{L}*)(\p{Ll})/u, (_, lead: string, c: string) => lead + c.toUpperCase());

/** Maiuscola dopo ogni fine di frase dentro il testo («ciao. come stai» → «ciao. Come stai»). */
const capitalizeAfterStops = (text: string) => text.replace(/([.!?…]["»”’)\]]*\s+)(\p{Ll})/gu, (_, stop: string, c: string) => stop + c.toUpperCase());

/**
 * Riorganizza i segmenti in blocchi più lunghi, senza motore IA: usa solo la punteggiatura che c'è e i tempi.
 *
 * - due segmenti si uniscono se hanno lo stesso parlante, la pausa tra loro è breve, il blocco non diventerebbe troppo lungo
 *   e (il blocco è ancora più corto del minimo oppure la frase non è finita);
 * - la frase che inizia dopo un punto fermo comincia con la maiuscola;
 * - un blocco che finisce davanti a una pausa lunga, a un cambio di parlante o alla fine del testo chiude con un punto
 *   se manca una punteggiatura finale. In mezzo a una frase tagliata per durata non si inventa nulla.
 *
 * I tempi restano quelli originali: un blocco inizia dove inizia il suo primo segmento e finisce dove finisce l'ultimo.
 */
export function reorganizeSegments(segments: EditSegment[], options: ReflowOptions = defaultReflow): ReflowResult {
  const blocks: EditSegment[] = [];
  for (const segment of segments) {
    const text = segment.text.trim();
    if (text.length === 0) {
      blocks.push({ ...segment, text });
      continue;
    }
    const last = blocks[blocks.length - 1];
    const join =
      last !== undefined &&
      last.text.length > 0 &&
      last.speaker === segment.speaker &&
      segment.start - last.end <= options.maxGap &&
      segment.end - last.start <= options.maxDuration &&
      (last.end - last.start < options.minDuration || !endsSentence(last.text) || startsLower.test(text));
    if (join) {
      blocks[blocks.length - 1] = { ...last, end: Math.max(last.end, segment.end), text: `${last.text} ${text}`, rev: segment.rev + last.rev + 1 };
    } else {
      blocks.push({ ...segment, text });
    }
  }

  let sentenceOpen = true; // all'inizio, e dopo ogni frase chiusa, la prossima parola è un inizio di frase
  const result = blocks.map((block, i) => {
    if (block.text.length === 0) return block;
    let text = capitalizeAfterStops(block.text);
    if (sentenceOpen) text = capitalize(text);
    const next = blocks.slice(i + 1).find((b) => b.text.length > 0);
    const closes = next === undefined || next.speaker !== block.speaker || next.start - block.end > options.maxGap;
    if (closes && !endsSentence(text) && !/[,;:]$/.test(text)) text = `${text}.`;
    sentenceOpen = endsSentence(text);
    return text === block.text ? block : { ...block, text, rev: block.rev + 1 };
  });

  return { segments: result, before: segments.length, after: result.length };
}
