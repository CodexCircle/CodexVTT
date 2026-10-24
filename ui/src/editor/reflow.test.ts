import { describe, expect, it } from 'vitest';
import type { EditSegment } from './model';
import { reorganizeSegments } from './reflow';

const seg = (id: number, start: number, end: number, text: string, speaker = 'S0'): EditSegment => ({ id, speaker, start, end, text, rev: 0 });

describe('reorganizeSegments', () => {
  it('unisce le frasi corte vicine in un blocco più lungo, con i tempi originali', () => {
    // Il caso reale: un segmento di 8 secondi dove ne servivano 13.
    const result = reorganizeSegments([
      seg(1, 0, 8, 'Siamo entrati nel mondo della linguistica.'),
      seg(2, 8.2, 13, 'Se ricordate siamo partiti dal concetto indoeuropeo.'),
    ]);

    expect(result.segments).toHaveLength(1);
    expect(result.segments[0]).toMatchObject({
      start: 0,
      end: 13,
      text: 'Siamo entrati nel mondo della linguistica. Se ricordate siamo partiti dal concetto indoeuropeo.',
    });
    expect([result.before, result.after]).toEqual([2, 1]);
  });

  it('non unisce oltre la durata massima né attraverso una pausa lunga', () => {
    const result = reorganizeSegments([
      seg(1, 0, 14, 'Prima frase lunga.'),
      seg(2, 14, 30, 'Seconda frase lunga.'), // 30 s > 25: blocco nuovo
      seg(3, 40, 44, 'Dopo una pausa.'), // pausa di 10 s
    ]);

    expect(result.segments.map((s) => [s.start, s.end])).toEqual([[0, 14], [14, 30], [40, 44]]);
  });

  it('non mescola parlanti diversi', () => {
    const result = reorganizeSegments([seg(1, 0, 4, 'Buongiorno a tutti.', 'S0'), seg(2, 4, 8, 'Buongiorno a lei.', 'S1')]);

    expect(result.segments).toHaveLength(2);
  });

  it('unisce una frase spezzata a metà anche se il blocco è già abbastanza lungo', () => {
    const result = reorganizeSegments([seg(1, 0, 13, 'Il concetto di cui parliamo oggi è'), seg(2, 13, 18, 'molto più antico di quanto si pensi.')]);

    expect(result.segments).toHaveLength(1);
    expect(result.segments[0].text).toBe('Il concetto di cui parliamo oggi è molto più antico di quanto si pensi.');
  });

  it('mette la maiuscola dopo il punto e chiude con un punto davanti a una pausa lunga', () => {
    const result = reorganizeSegments([seg(1, 0, 5, 'ciao a tutti. oggi parliamo di lingue'), seg(2, 30, 34, 'grazie')]);

    expect(result.segments[0].text).toBe('Ciao a tutti. Oggi parliamo di lingue.');
    expect(result.segments[1].text).toBe('Grazie.');
  });

  it('non inventa un punto in mezzo a una frase tagliata per durata', () => {
    const result = reorganizeSegments([seg(1, 0, 20, 'una frase che continua senza fine'), seg(2, 20, 40, 'e prosegue ancora')]);

    expect(result.segments[0].text).toBe('Una frase che continua senza fine');
    expect(result.segments[1].text).toBe('e prosegue ancora.');
  });

  it('lascia stare i segmenti vuoti e non altera la lista di partenza', () => {
    const input = [seg(1, 0, 3, 'Ciao.'), seg(2, 3, 5, '  ')];
    const copy = JSON.parse(JSON.stringify(input));

    const result = reorganizeSegments(input);

    expect(input).toEqual(copy);
    expect(result.segments.map((s) => s.text)).toEqual(['Ciao.', '']);
  });
});
