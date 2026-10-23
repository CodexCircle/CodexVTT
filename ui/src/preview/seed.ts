import type { TranscriptionResult } from '../bridge/protocol';

/** Dati di esempio per la preview statica della UI (`npm run build:preview`). */
const sample = (
  audioFileName: string,
  minutes: number,
  daysAgo: number,
  modelId: string,
  displayName: string,
  text: string,
): TranscriptionResult => ({
  audioFileName,
  audioDuration: minutes * 60,
  detectedLanguage: 'it',
  model: {
    modelId,
    displayName,
    file: `ggml-${modelId}.bin`,
    sha256: 'e3b0c44298fc1c149afbf4c8996fb924',
    runtime: 'Cpu',
  },
  segments: text.split(/(?<=\.)\s+/).map((sentence, i) => ({
    start: i * 6.5,
    end: i * 6.5 + 6,
    text: sentence,
  })),
  elapsed: minutes * 9,
  completedAt: new Date(Date.now() - daysAgo * 86_400_000).toISOString(),
  text,
});

export const previewHistory: TranscriptionResult[] = [
  sample(
    'lezione-storia-moderna.m4a',
    52,
    0.2,
    'large-v3-turbo-q5_0',
    'Large v3 Turbo (q5_0)',
    'Oggi riprendiamo dal congresso di Vienna. Le potenze vincitrici ridisegnano la mappa d’Europa. Il principio di legittimità guida le restaurazioni.',
  ),
  sample(
    'intervista-podcast-ep12.mp3',
    38,
    1,
    'large-v3-turbo-q5_0',
    'Large v3 Turbo (q5_0)',
    'Benvenuti a un nuovo episodio. Con noi c’è un’ospite che lavora sull’intelligenza artificiale locale. Parliamo di privacy e di modelli che girano sul proprio PC.',
  ),
  sample(
    'memo-vocale-spesa.ogg',
    2,
    2,
    'base',
    'Base',
    'Ricordati di prendere il pane, il latte e le uova. Passare anche in farmacia prima delle sette.',
  ),
  sample(
    'riunione-progetto-q3.wav',
    47,
    4,
    'small',
    'Small',
    'Iniziamo con lo stato dei lavori. Il rilascio è previsto per fine mese. Restano da chiudere tre segnalazioni sull’installer.',
  ),
  sample(
    'tesi-relatore-feedback.flac',
    21,
    6,
    'small',
    'Small',
    'Il secondo capitolo è solido. Nel terzo servono più fonti primarie. Le conclusioni vanno accorciate.',
  ),
  sample(
    'conferenza-design.m4a',
    64,
    9,
    'large-v3-turbo-q5_0',
    'Large v3 Turbo (q5_0)',
    'Il buon design è invisibile. Parliamo di interfacce che si fanno da parte per lasciare spazio ai contenuti.',
  ),
];

export const previewTitles: Record<string, string> = {
  'lezione-storia-moderna.m4a': 'Congresso di Vienna',
  'intervista-podcast-ep12.mp3': 'Podcast · episodio 12',
  'memo-vocale-spesa.ogg': 'Lista della spesa',
  'riunione-progetto-q3.wav': 'Riunione Q3',
  'tesi-relatore-feedback.flac': 'Feedback sulla tesi',
};

export const previewCollections = [
  { name: 'Università', color: 'green' as const, files: ['lezione-storia-moderna.m4a', 'tesi-relatore-feedback.flac'] },
  { name: 'Lavoro', color: 'bordeaux' as const, files: ['riunione-progetto-q3.wav', 'conferenza-design.m4a'] },
  { name: 'Podcast', color: 'ochre' as const, files: ['intervista-podcast-ep12.mp3'] },
];
