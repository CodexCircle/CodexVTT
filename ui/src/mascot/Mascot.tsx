import { useRef } from 'react';
import { mascotCharacters, type MascotCharacter } from '../bridge/protocol';
import type { Activity } from '../state/useCodexVtt';
import { shapes, type Art } from './art';
import { giulio } from './giulio';
import { tina } from './tina';
import { useLook } from './useLook';

/** Come ogni disegno (coordinate 1024×1024) entra nel riquadro 120×120 e dove stanno le cuffie. */
interface Character {
  art: Art;
  /** Da coordinate del disegno a coordinate della mascotte. */
  transform: string;
  /** Fondi che coprono i buchi sotto occhi e naso quando il viso si sposta. */
  underlays: readonly { cx: number; cy: number; rx: number; ry: number; fill: string }[];
  headphones: { band: string; left: number; right: number; top: number; width: number; height: number };
  /** Spostamento delle onde sonore accanto alla cuffia sinistra. */
  waves: string;
}

const characters: Record<MascotCharacter, Character> = {
  tina: {
    art: tina,
    transform: 'translate(52 10) scale(0.1795) translate(-458 -234)',
    underlays: [
      { cx: 515, cy: 335, rx: 118, ry: 46, fill: '#916f48' },
      { cx: 520, cy: 380, rx: 66, ry: 44, fill: '#d2b993' },
    ],
    headphones: { band: 'M35 31C35 1 90 1 90 31', left: 30, right: 86, top: 24, width: 9, height: 17 },
    waves: 'translate(17 -12)',
  },
  giulio: {
    art: giulio,
    transform: 'translate(56 10) scale(0.128) translate(-571 -103)',
    underlays: [
      { cx: 507, cy: 352, rx: 128, ry: 44, fill: '#dd663c' },
      { cx: 507, cy: 404, rx: 54, ry: 38, fill: '#dd663c' },
    ],
    headphones: { band: 'M18 40C18 4 78 4 78 40', left: 12, right: 76, top: 32, width: 9, height: 18 },
    waves: 'translate(2 -6)',
  },
};

/**
 * Mascotte (issue #15): Tina (lontra) o Giulio (panda rosso), da SVG di svgapp.ai.
 * Mentre trascrive ascolta l'audio con le cuffie e prende appunti con la matita su un taccuino.
 * Il viso segue il puntatore: useLook aggiorna --look-x/--look-y e il CSS sposta occhi e naso.
 * Decorativa (aria-hidden): lo stato è sempre comunicato anche dal testo dell'interfaccia.
 * Le animazioni sono solo CSS e si disattivano con prefers-reduced-motion.
 */
export function Mascot({
  activity,
  character,
  size = 72,
  testId = 'mascot',
  headphones = false,
  seated = false,
}: {
  activity: Activity;
  character?: MascotCharacter | null;
  /** Cuffie indossate anche quando non sta ancora trascrivendo (file pronto). */
  headphones?: boolean;
  /** Seduta su una superficie: niente ombra né fluttuazione. */
  seated?: boolean;
  size?: number;
  testId?: string;
}) {
  const listening = activity === 'working';
  // Un valore sconosciuto (per esempio una mascotte tolta) ricade su Tina.
  const who: MascotCharacter = character && mascotCharacters.includes(character) ? character : 'tina';
  const c = characters[who];
  const h = c.headphones;
  const ref = useRef<SVGSVGElement>(null);
  useLook(ref);

  return (
    <svg
      ref={ref}
      className={`mascot mascot--${activity} mascot-${who}${seated ? ' mascot--seated' : ''}`}
      width={size}
      height={size}
      viewBox="0 0 120 120"
      aria-hidden="true"
      focusable="false"
      data-testid={testId}
      data-activity={activity}
      data-character={who}
    >
      {!seated && <ellipse className="mascot__shadow" cx="54" cy="112" rx="26" ry="4" />}
      <g className="mascot__body">
        {listening && (
          <g className="mascot__waves" transform={c.waves}>
            <path d="M11 41q-5 8 0 16" />
            <path d="M5 37q-8 12 0 24" />
          </g>
        )}
        <g className="mascot__nod">
          <g className="mascot__lean">
            <g transform={c.transform}>
              {c.underlays.map((u, i) => (
                <ellipse key={i} {...u} />
              ))}
              {shapes(c.art.body)}
              <g className="mascot__look">
                <g className="mascot__blink">{shapes(c.art.eyes)}</g>
                {shapes(c.art.nose)}
              </g>
            </g>
            {(listening || headphones) && (
              <g className="mascot__headphones">
                <path className="mascot__band" d={h.band} />
                <rect x={h.left} y={h.top} width={h.width} height={h.height} rx={h.width / 2} />
                <rect x={h.right} y={h.top} width={h.width} height={h.height} rx={h.width / 2} />
              </g>
            )}
          </g>
        </g>
        {listening && (
          <g className="mascot__notes">
            <g transform="rotate(-6 87 84)">
              <rect className="mascot__pad" x="69" y="66" width="36" height="38" rx="4" />
              <path className="mascot__line mascot__line--1" d="M75 77h24" />
              <path className="mascot__line mascot__line--2" d="M75 85h20" />
              <path className="mascot__line mascot__line--3" d="M75 93h22" />
            </g>
            <g transform="translate(74 76)">
              <g className="mascot__pencil">
                <g transform="rotate(38)">
                  <rect className="mascot__pencil-body" x="-3.2" y="-30" width="6.4" height="22" rx="1" />
                  <rect className="mascot__pencil-eraser" x="-3.2" y="-35" width="6.4" height="5" rx="1.5" />
                  <path className="mascot__pencil-wood" d="M-3.2-8h6.4L0 0z" />
                  <path className="mascot__pencil-tip" d="M-1.1-2.4h2.2L0 0z" />
                </g>
                <circle className="mascot__hand" cx="9" cy="-11" r="5.5" />
              </g>
            </g>
          </g>
        )}
      </g>
    </svg>
  );
}
