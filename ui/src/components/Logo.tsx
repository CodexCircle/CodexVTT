import { useId } from 'react';

/**
 * Logo di CodexVTT: un blocco di appunti color crema con l'angolo che si solleva,
 * appoggiato su due strati dentro un quadrato arrotondato. I colori vengono dal tema (token --logo-*).
 */
export function Logo({ size = 44 }: { size?: number }) {
  const id = useId().replace(/:/g, '');
  const clip = `${id}-clip`;
  const sheetClip = `${id}-sheet`;
  const curl = `${id}-curl`;
  const shade = `${id}-shade`;
  const lift = `${id}-lift`;
  // Foglio in coordinate locali 60×44, ruotato di -14° e spostato in alto a destra.
  const sheet = 'M8 0H52a8 8 0 0 1 8 8V36a8 8 0 0 1-8 8H8a8 8 0 0 1-8-8V8a8 8 0 0 1 8-8z';
  // Zona scoperta dall'angolo sollevato: si vede il foglio sotto, un po' più scuro.
  const under = 'M0 31C8 31 15 37 15 44H8a8 8 0 0 1-8-8z';
  return (
    <svg className="logo" width={size} height={size} viewBox="0 0 64 64" aria-hidden="true" focusable="false">
      <defs>
        <clipPath id={clip}>
          <rect x="4" y="4" width="56" height="56" rx="16" />
        </clipPath>
        <clipPath id={sheetClip}>
          <path d={sheet} />
        </clipPath>
        <linearGradient id={curl} x1="0" y1="1" x2="1" y2="0">
          <stop offset="0" className="logo__curl-1" />
          <stop offset="0.55" className="logo__curl-2" />
          <stop offset="1" className="logo__curl-3" />
        </linearGradient>
        <linearGradient id={shade} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" className="logo__sheet-1" />
          <stop offset="1" className="logo__sheet-2" />
        </linearGradient>
        <filter id={lift} x="-20%" y="-20%" width="140%" height="140%">
          <feDropShadow className="logo__lift" dx="-0.8" dy="1.4" stdDeviation="1.2" floodOpacity="0.45" />
        </filter>
      </defs>
      <rect x="4" y="5.5" width="56" height="56" rx="16" fill="#000" opacity="0.18" />
      <g clipPath={`url(#${clip})`}>
        <rect className="logo__bg" x="4" y="4" width="56" height="56" />
        <g transform="translate(20 -8) rotate(-14)">
          <rect x="-6" y="-10" width="76" height="60" rx="10" className="logo__layer" filter={`url(#${lift})`} />
          <g filter={`url(#${lift})`}>
            <path d={sheet} fill={`url(#${shade})`} />
          </g>
          <g clipPath={`url(#${sheetClip})`} className="logo__line" strokeOpacity="0.16" strokeWidth="1">
            <path d="M0 8.5h60M0 16.5h60M0 24.5h60M0 32.5h60M0 40.5h60" />
          </g>
          <path d={under} className="logo__under" opacity="0.45" />
          {/* l'angolo che si solleva */}
          <path
            d="M0 31C8 31 15 37 15 44C19 40 22 33 17 28.5C12.5 24.5 5 26 0 31z"
            fill={`url(#${curl})`}
            filter={`url(#${lift})`}
          />
        </g>
      </g>
    </svg>
  );
}
