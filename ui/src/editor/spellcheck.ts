import type { EditSegment } from './model';

/** Un possibile errore trovato nel testo di un segmento. */
export interface SpellIssue {
  uid: number;
  segmentId: number;
  start: number;
  end: number;
  original: string;
  suggestion: string;
}

// Controllo locale e senza rete: un piccolo dizionario degli errori più comuni dell'italiano scritto.
// Non sostituisce un correttore vero: serve a ripulire i refusi tipici della trascrizione.
const corrections: [string, string][] = [
  ['perchè', 'perché'],
  ['poichè', 'poiché'],
  ['finchè', 'finché'],
  ['benchè', 'benché'],
  ['affinchè', 'affinché'],
  ['cosicchè', 'cosicché'],
  ['sopratutto', 'soprattutto'],
  ['propio', 'proprio'],
  ['obbiettivo', 'obiettivo'],
  ['coscenza', 'coscienza'],
  ['daccordo', 'd’accordo'],
  ['avvolte', 'a volte'],
  ['cmq', 'comunque'],
  ['qual\'è', 'qual è'],
  ['qual’è', 'qual è'],
  ['un pò', 'un po’'],
  ['pò', 'po’'],
  ['piu\'', 'più'],
  ['perche\'', 'perché'],
];

const escape = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const rules = corrections.map(([wrong, right]) => ({
  pattern: new RegExp(`(?<![\\p{L}'’])${escape(wrong)}(?![\\p{L}])`, 'giu'),
  right,
}));

const matchCase = (original: string, suggestion: string) =>
  original[0] !== original[0].toLowerCase() ? suggestion[0].toUpperCase() + suggestion.slice(1) : suggestion;

export function checkSpelling(segments: EditSegment[], nextUid: () => number): SpellIssue[] {
  const issues: SpellIssue[] = [];
  for (const g of segments) {
    for (const { pattern, right } of rules) {
      pattern.lastIndex = 0;
      for (const m of g.text.matchAll(pattern)) {
        const start = m.index ?? 0;
        issues.push({ uid: nextUid(), segmentId: g.id, start, end: start + m[0].length, original: m[0], suggestion: matchCase(m[0], right) });
      }
    }
  }
  // Un solo errore per posizione (« un pò » contiene anche «pò»): vale il più lungo.
  return issues
    .sort((a, b) => a.segmentId - b.segmentId || a.start - b.start || b.end - a.end)
    .filter((issue, i, all) => !all.slice(0, i).some((o) => o.segmentId === issue.segmentId && o.start < issue.end && issue.start < o.end));
}
