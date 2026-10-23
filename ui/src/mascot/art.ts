import { createElement, type ReactNode } from 'react';

/** Un disegno ricalcato: forme SVG (tag + attributi) divise tra corpo, occhi e naso/bocca. */
type Shape = readonly [tag: 'path' | 'circle' | 'ellipse' | 'rect', attrs: Readonly<Record<string, string>>];
export interface Art {
  body: readonly Shape[];
  eyes: readonly Shape[];
  nose: readonly Shape[];
}

export function shapes(list: readonly Shape[]): ReactNode[] {
  return list.map(([tag, attrs], i) => createElement(tag, { key: i, ...attrs }));
}
