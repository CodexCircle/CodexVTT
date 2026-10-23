/**
 * "Copertina" decorativa di un file audio: un riquadro con colori e forma d'onda
 * ricavati dal nome, così ogni trascrizione è riconoscibile a colpo d'occhio.
 */
// Coppie di colori definite in styles.css (--cover-Na/--cover-Nb), uguali nei due temi.
const PALETTES = 6;

function hash(text: string): number {
  let h = 2166136261;
  for (let i = 0; i < text.length; i++) h = Math.imul(h ^ text.charCodeAt(i), 16777619);
  return h >>> 0;
}

export function AudioCover({ name, label, size = 'md' }: { name: string; label?: string; size?: 'sm' | 'md' | 'lg' }) {
  const h = hash(name);
  const n = h % PALETTES;
  const bars = Array.from({ length: 14 }, (_, i) => 18 + ((h >>> (i % 24)) % 9) * 6 + (i % 3) * 4);
  const ext = label ?? name.split('.').pop()?.toUpperCase() ?? '';
  return (
    <div
      className={`cover cover--${size}`}
      style={{ background: `linear-gradient(160deg, var(--cover-${n}a), var(--cover-${n}b))` }}
      aria-hidden="true"
    >
      <svg viewBox="0 0 100 100" preserveAspectRatio="none">
        {bars.map((height, i) => (
          <rect key={i} x={6 + i * 6.5} y={60 - height / 2} width="3.4" height={height} rx="1.7" />
        ))}
      </svg>
      <span className="cover__label">{ext}</span>
    </div>
  );
}
