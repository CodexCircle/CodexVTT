import { useEffect, type RefObject } from 'react';

// Un solo ascoltatore del puntatore per tutte le mascotte della pagina.
const watched = new Set<Element & ElementCSSInlineStyle>();
let x = Number.NaN;
let y = Number.NaN;
let frame = 0;

const clamp = (v: number) => Math.max(-1, Math.min(1, v));

function update() {
  frame = 0;
  if (Number.isNaN(x)) return;
  for (const el of watched) {
    const r = el.getBoundingClientRect();
    if (!r.width) continue;
    el.style.setProperty('--look-x', clamp((x - (r.left + r.width / 2)) / (window.innerWidth / 2)).toFixed(3));
    el.style.setProperty('--look-y', clamp((y - (r.top + r.height * 0.35)) / (window.innerHeight / 2)).toFixed(3));
  }
}

function onPointer(e: PointerEvent) {
  x = e.clientX;
  y = e.clientY;
  if (!frame) frame = requestAnimationFrame(update);
}

/** Espone su `ref` la direzione del puntatore (--look-x, --look-y tra -1 e 1) per il CSS. */
export function useLook(ref: RefObject<(Element & ElementCSSInlineStyle) | null>) {
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    watched.add(el);
    if (watched.size === 1) window.addEventListener('pointermove', onPointer, { passive: true });
    update();
    return () => {
      watched.delete(el);
      if (watched.size === 0) window.removeEventListener('pointermove', onPointer);
    };
  }, [ref]);
}
