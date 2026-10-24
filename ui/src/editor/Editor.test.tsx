import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { App } from '../App';
import { BridgeClient } from '../bridge/client';
import { createMockTransport, type MockOptions } from '../bridge/mockTransport';
import type { TranscriptionResult } from '../bridge/protocol';
import { previewHistory } from '../preview/seed';
import { checkSpelling } from './spellcheck';
import type { EditSegment } from './model';

const withTypo: TranscriptionResult = {
  ...previewHistory[0],
  audioFileName: 'errori.mp3',
  segments: [
    { start: 0, end: 4, text: 'Non so perchè è andata così.' },
    { start: 5, end: 9, text: 'Dobbiamo sopratutto capire il motivo.' },
  ],
  text: 'Non so perchè è andata così. Dobbiamo sopratutto capire il motivo.',
};

async function openFirstRecent(options: MockOptions = {}) {
  const client = new BridgeClient(createMockTransport({ tick: 1, uiLanguage: 'it', installed: ['base'], history: [withTypo], ...options }));
  const user = userEvent.setup();
  render(<App client={client} />);
  const recent = await screen.findByRole('list', { name: 'Recenti' });
  await user.click(within(recent).getByRole('button', { name: /errori\.mp3/ }));
  await screen.findByRole('heading', { name: 'Trascrizione di errori.mp3' });
  return user;
}

describe('Editor a due pannelli', () => {
  it('mostra subito l’anteprima scegliendo un file dall’elenco, senza «Apri»', async () => {
    await openFirstRecent();

    const segments = screen.getAllByRole('textbox', { name: /^Testo del segmento/ });
    expect(segments).toHaveLength(2);
    expect(segments[0]).toHaveTextContent('Non so perchè è andata così.');
    expect(screen.getByRole('region', { name: /Trascrizione di errori\.mp3/ })).toBeInTheDocument();
  });

  it('segnala le modifiche non salvate e le toglie con Annulla modifica', async () => {
    const user = await openFirstRecent();
    expect(screen.getByText('Salvato nella cronologia')).toBeInTheDocument();

    const first = screen.getAllByRole('textbox', { name: /^Testo del segmento/ })[0];
    await user.click(first);
    await user.keyboard('{Control>}a{/Control}Ciao a tutti');
    await user.tab();
    expect(await screen.findByText('Modifiche non salvate')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Annulla modifica' }));
    expect(await screen.findByText('Salvato nella cronologia')).toBeInTheDocument();
    expect(screen.getAllByRole('textbox', { name: /^Testo del segmento/ })[0]).toHaveTextContent('Non so perchè è andata così.');
  });

  it('assegna i parlanti a mano e li conta nel pannello delle proprietà', async () => {
    const user = await openFirstRecent();
    const props = screen.getByRole('complementary', { name: 'Proprietà globali' });
    expect(within(props).getByRole('textbox', { name: 'Nome dello speaker 0' })).toHaveValue('Speaker 0');
    expect(within(props).getByText('2 seg.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Aggiungi speaker' }));
    expect(within(props).getAllByRole('textbox', { name: /^Nome dello speaker/ })).toHaveLength(2);

    await user.click(screen.getAllByRole('button', { name: 'Speaker 0' })[0]);
    await user.click(screen.getByRole('menuitem', { name: 'Speaker 1' }));
    await waitFor(() => expect(within(props).getAllByText('1 seg.')).toHaveLength(2));
  });

  it('trova gli errori più comuni e applica la correzione', async () => {
    const user = await openFirstRecent();

    await user.click(screen.getByRole('button', { name: 'Esegui controllo ortografico' }));
    expect(await screen.findByText('2 possibili errori trovati')).toBeInTheDocument();

    await user.click(screen.getAllByRole('button', { name: 'Accetta' })[0]);
    await waitFor(() => expect(screen.getAllByRole('textbox', { name: /^Testo del segmento/ })[0]).toHaveTextContent('Non so perché è andata così.'));
    await user.click(screen.getByRole('button', { name: 'Ignora' }));
    expect(screen.queryByRole('button', { name: 'Accetta' })).not.toBeInTheDocument();
  });

  it('«Punteggiatura e frasi» riorganizza i segmenti in blocchi più lunghi e si annulla', async () => {
    const user = await openFirstRecent();
    expect(screen.getAllByRole('textbox', { name: /^Testo del segmento/ })).toHaveLength(2);

    await user.click(screen.getByRole('button', { name: 'Punteggiatura e frasi' }));

    const segments = screen.getAllByRole('textbox', { name: /^Testo del segmento/ });
    expect(segments).toHaveLength(1);
    expect(segments[0]).toHaveTextContent('Non so perchè è andata così. Dobbiamo sopratutto capire il motivo.');
    expect(screen.getByText('Segmenti riorganizzati: da 2 a 1.')).toBeInTheDocument();
    expect(await screen.findByText('Modifiche non salvate')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Annulla modifica' }));
    expect(await screen.findByText('Salvato nella cronologia')).toBeInTheDocument();
    expect(screen.getAllByRole('textbox', { name: /^Testo del segmento/ })).toHaveLength(2);
  });

  it('il lettore avvia e ferma il cursore, e la timeline si può ingrandire', async () => {
    const user = await openFirstRecent();

    await user.click(screen.getByRole('button', { name: 'Avvia' }));
    expect(screen.getByRole('button', { name: 'Pausa' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Pausa' }));

    await user.click(screen.getByRole('button', { name: 'Velocità di riproduzione' }));
    expect(screen.getByRole('button', { name: 'Velocità di riproduzione' })).toHaveTextContent('1.3x');
    const zoom = screen.getByRole('slider', { name: 'Zoom della timeline' });
    await user.click(screen.getByRole('button', { name: 'Ingrandisci' }));
    expect(zoom).toHaveValue('50');
  });

  it('unisce due segmenti consecutivi', async () => {
    const user = await openFirstRecent();

    await user.click(screen.getByRole('button', { name: 'Unisci con il successivo' }));

    const segments = screen.getAllByRole('textbox', { name: /^Testo del segmento/ });
    expect(segments).toHaveLength(1);
    expect(segments[0]).toHaveTextContent('Non so perchè è andata così. Dobbiamo sopratutto capire il motivo.');
  });

  it('l’esportazione dalla barra segnala che usa il testo originale se ci sono modifiche', async () => {
    const user = await openFirstRecent();
    await user.click(screen.getByRole('button', { name: 'Unisci con il successivo' }));

    await user.click(screen.getByRole('button', { name: 'Esporta' }));

    expect(screen.getByText(/Si esporta il testo originale/)).toBeInTheDocument();
  });

  it('cambia tema dal pulsante in alto', async () => {
    const client = new BridgeClient(createMockTransport({ tick: 1, uiLanguage: 'it', installed: ['base'] }));
    const user = userEvent.setup();
    render(<App client={client} />);

    await user.click(await screen.findByRole('button', { name: 'Cambia tema chiaro o scuro' }));

    await waitFor(() => expect(document.documentElement.dataset.theme).toBe('dark'));
    await user.click(screen.getByRole('button', { name: 'Cambia tema chiaro o scuro' }));
    await waitFor(() => expect(document.documentElement.dataset.theme).toBe('light'));
  });
});

describe('controllo ortografico locale', () => {
  const seg = (id: number, text: string): EditSegment => ({ id, speaker: 'S0', start: 0, end: 1, text, rev: 0 });

  it('rispetta le maiuscole e non tocca le parole corrette', () => {
    let n = 0;
    const issues = checkSpelling([seg(1, 'Perchè no? Poiché sì, un pò.'), seg(2, 'Tutto corretto qui.')], () => ++n);
    expect(issues.map((i) => [i.original, i.suggestion])).toEqual([
      ['Perchè', 'Perché'],
      ['un pò', 'un po’'],
    ]);
  });
});
