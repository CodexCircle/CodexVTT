import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { BridgeClient } from './bridge/client';
import { createMockTransport, type MockOptions } from './bridge/mockTransport';
import { previewHistory } from './preview/seed';

function setup(options: MockOptions = {}) {
  const client = new BridgeClient(createMockTransport({ tick: 1, uiLanguage: 'it', ...options }));
  const user = userEvent.setup();
  render(<App client={client} />);
  return { user };
}

describe('App', () => {
  const fetchSpy = vi.fn();

  beforeEach(() => {
    fetchSpy.mockReset();
    vi.stubGlobal('fetch', fetchSpy);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('asks for a model when none is installed', async () => {
    const { user } = setup();

    await user.click(await screen.findByRole('button', { name: 'Scegli un modello' }));

    expect(await screen.findByRole('heading', { name: 'Modelli' })).toBeInTheDocument();
    expect(screen.getByText(/Consigliato: Large v3 Turbo/)).toBeInTheDocument();
  });

  it('shows requirements and confirms before downloading, then installs', async () => {
    const { user } = setup();
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));

    const small = await screen.findByRole('listitem', { name: 'Small' });
    expect(within(small).getByText(/488 MB su disco/)).toBeInTheDocument();
    await user.click(within(small).getByRole('button', { name: 'Scarica…' }));

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText(/huggingface\.co/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Nessun audio o testo viene inviato/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Scarica' }));

    expect(await screen.findByText('Modello Small installato e verificato.')).toBeInTheDocument();
    await waitFor(() => expect(within(screen.getByRole('listitem', { name: 'Small' })).getByText('Pronto')).toBeInTheDocument());
  });

  it('asks for confirmation before removing a model', async () => {
    const { user } = setup({ installed: ['base'] });
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));
    const base = await screen.findByRole('listitem', { name: 'Base' });

    await user.click(within(base).getByRole('button', { name: 'Rimuovi' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText(/dovrai scaricarlo o importarlo/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Annulla' }));
    expect(within(screen.getByRole('listitem', { name: 'Base' })).getByText('Pronto')).toBeInTheDocument();

    await user.click(within(screen.getByRole('listitem', { name: 'Base' })).getByRole('button', { name: 'Rimuovi' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Rimuovi' }));
    await waitFor(() => expect(within(screen.getByRole('listitem', { name: 'Base' })).queryByText('Pronto')).not.toBeInTheDocument());
  });

  it('flags models that exceed the PC capabilities', async () => {
    const { user } = setup();
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));

    const large = await screen.findByRole('listitem', { name: 'Large v3' });
    expect(
      within(large).getByText(
        'Attenzione: troppo pesante per il processore di questo PC: richiede circa 3 volte il calcolo di Large v3 Turbo (q5_0), il più grande adatto.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByText('8 core logici: il modello più pesante adatto a questo processore è Large v3 Turbo (q5_0).')).toBeInTheDocument();
  });

  it('queues several files, runs them one after the other and opens each result', async () => {
    const { user } = setup({ installed: ['base'], batchFiles: ['lezione1.mp3', 'lezione2.mp3', 'lezione3.mp3'], batchUnreadable: ['rotto.xyz'] });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    const list = await screen.findByRole('list', { name: 'Coda di trascrizione' });
    expect(screen.getByText('4 file in coda')).toBeInTheDocument();
    expect(within(screen.getByRole('listitem', { name: 'rotto.xyz' })).getByText('Non riuscito')).toBeInTheDocument();
    expect(within(screen.getByRole('listitem', { name: 'rotto.xyz' })).getByText(/Formato audio non supportato/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Avvia coda' }));

    expect(await screen.findByText('Coda finita: 3 completati, 1 non riusciti.')).toBeInTheDocument();
    for (const file of ['lezione1.mp3', 'lezione2.mp3', 'lezione3.mp3']) {
      expect(within(within(list).getByRole('listitem', { name: file })).getByText('Completato')).toBeInTheDocument();
    }

    await user.click(screen.getByRole('button', { name: 'Apri la trascrizione di lezione2.mp3' }));
    expect(await screen.findByRole('heading', { name: 'Trascrizione di lezione2.mp3' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Cronologia' }));
    for (const file of ['lezione1.mp3', 'lezione2.mp3', 'lezione3.mp3']) {
      expect(await screen.findByRole('listitem', { name: file })).toBeInTheDocument();
    }
  });

  it('lets the user take files off the queue before starting and add more', async () => {
    const { user } = setup({ installed: ['base'], batchFiles: ['a.mp3', 'b.mp3'] });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    await user.click(await screen.findByRole('button', { name: 'Togli a.mp3 dalla coda' }));
    expect(screen.queryByRole('listitem', { name: 'a.mp3' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Aggiungi file…' }));
    await waitFor(() => expect(screen.getAllByRole('listitem', { name: /\.mp3$/ })).toHaveLength(3));

    await user.click(screen.getByRole('button', { name: 'Svuota coda' }));
    expect(screen.queryByRole('list', { name: 'Coda di trascrizione' })).not.toBeInTheDocument();
  });

  it('stops the queue when cancelled and resumes from the interrupted file', async () => {
    const { user } = setup({ installed: ['base'], batchFiles: ['a.mp3', 'b.mp3'], tick: 5, jobStep: 10 });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    await user.click(await screen.findByRole('button', { name: 'Avvia coda' }));
    await user.click(await screen.findByRole('button', { name: 'Annulla' }));

    expect(await screen.findByText('Trascrizione annullata.')).toBeInTheDocument();
    expect(within(screen.getByRole('listitem', { name: 'a.mp3' })).getByText('In coda')).toBeInTheDocument();
    expect(within(screen.getByRole('listitem', { name: 'b.mp3' })).getByText('In coda')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Avvia coda' })).toBeInTheDocument();
  });

  it('shows «In pausa» in the file list while the job is paused, like the file view does', async () => {
    const { user } = setup({ installed: ['base'], batchFiles: ['a.mp3', 'b.mp3'], tick: 20, jobStep: 5 });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    await user.click(await screen.findByRole('button', { name: 'Avvia coda' }));
    const pause = await screen.findByRole('button', { name: 'Pausa' });
    await waitFor(() => expect(pause).toBeEnabled());
    await user.click(pause);

    const row = screen.getByRole('listitem', { name: 'a.mp3' });
    await waitFor(() => expect(within(row).getByText(/^In pausa/)).toBeInTheDocument());
    expect(within(row).queryByText(/^In corso/)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Riprendi' }));
    await waitFor(() => expect(within(screen.getByRole('listitem', { name: 'a.mp3' })).queryByText(/^In pausa/)).not.toBeInTheDocument());
  });

  it('shows «Cartella non testata» after choosing a folder and only saves it when the test passes', async () => {
    const { user } = setup({ installed: ['base'] });
    await user.click(await screen.findByRole('button', { name: 'Impostazioni' }));

    await user.click(await screen.findByRole('button', { name: 'Cambia cartella…' }));
    expect(await screen.findByText('Cartella non testata')).toBeInTheDocument();
    expect(screen.getByText(/Trascrizioni$/)).toBeInTheDocument();
    expect(screen.getByText(/history$/)).toBeInTheDocument(); // ancora la cartella precedente

    await user.click(screen.getByRole('button', { name: 'Testa cartella' }));
    await waitFor(() => expect(screen.queryByText('Cartella non testata')).not.toBeInTheDocument());
    expect(screen.getAllByText(/Trascrizioni$/).length).toBeGreaterThan(0);
  });

  it('warns and keeps the previous folder when the folder test fails', async () => {
    const { user } = setup({ installed: ['base'], folderTestError: 'cannot_write' });
    await user.click(await screen.findByRole('button', { name: 'Impostazioni' }));

    await user.click(await screen.findByRole('button', { name: 'Cambia cartella…' }));
    await user.click(screen.getByRole('button', { name: 'Testa cartella' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Non si può scrivere in questa cartella.');
    expect(screen.getByText('Test non riuscito')).toBeInTheDocument();
    expect(screen.getByText(/history$/)).toBeInTheDocument();
    expect(screen.getByText(/Trascrizioni$/)).toBeInTheDocument(); // solo la scelta in attesa, non salvata
  });

  it('explains a Windows block with the likely cause and the app to allow', async () => {
    const { user } = setup({ installed: ['base'], folderTestError: 'blocked_by_windows' });
    await user.click(await screen.findByRole('button', { name: 'Impostazioni' }));

    await user.click(await screen.findByRole('button', { name: 'Cambia cartella…' }));
    await user.click(screen.getByRole('button', { name: 'Testa cartella' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Windows ha negato la scrittura in questa cartella.');
    expect(screen.getByText(/«Accesso alle cartelle controllato» è attivo/)).toBeInTheDocument();
    expect(screen.getByText(/Consenti app tramite Accesso alle cartelle controllato/)).toBeInTheDocument();
    expect(screen.getByText(/CodexVtt\.App\.exe$/)).toBeInTheDocument();
  });

  it('lets the user pick a color theme and applies it to the document', async () => {
    const { user } = setup({ installed: ['base'] });
    await user.click(await screen.findByRole('button', { name: 'Impostazioni' }));

    const group = await screen.findByRole('group', { name: 'Tema' });
    for (const name of ['Predefinito', 'Sabbia', 'Natale', 'Halloween', 'Ciliegia']) {
      expect(within(group).getByRole('radio', { name })).toBeInTheDocument();
    }
    await user.click(within(group).getByRole('radio', { name: 'Natale' }));
    await waitFor(() => expect(document.documentElement.dataset.palette).toBe('natale'));
    await user.click(within(group).getByRole('radio', { name: 'Predefinito' }));
    await waitFor(() => expect(document.documentElement.dataset.palette).toBeUndefined());
  });

  it('judges the models by the video memory when Whisper uses the graphics card', async () => {
    const { user } = setup({ gpus: ['NVIDIA GeForce RTX 3060'], gpuVramGb: 12 });
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));

    expect(await screen.findByText(/Whisper usa la scheda video \(12 GB di memoria video\)/)).toBeInTheDocument();
    expect(screen.queryByText(/il modello più pesante adatto a questo processore/)).not.toBeInTheDocument();
    const medium = screen.getByRole('listitem', { name: 'Medium' });
    expect(within(medium).queryByText(/troppo pesante per il processore/)).not.toBeInTheDocument();
  });

  it('shows the PC and model details in an «i» tooltip on hover and on keyboard focus', async () => {
    const { user } = setup({ gpus: ['NVIDIA GeForce RTX 3060'], gpuVramGb: 12 });
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));
    const small = await screen.findByRole('listitem', { name: 'Small' });
    const info = within(small).getByRole('button', { name: 'Dettagli di Small' });
    const tip = within(small).getByRole('tooltip', { hidden: true });
    expect(info).toHaveAttribute('aria-describedby', tip.id);
    expect(tip).toHaveAttribute('data-open', 'false');

    await user.hover(info);
    expect(tip).toHaveAttribute('data-open', 'true');
    expect(tip).toHaveTextContent('Processore: 8 core logici');
    expect(tip).toHaveTextContent('Memoria video: 12 GB');
    expect(tip).toHaveTextContent('Memoria richiesta');
    expect(tip).toHaveTextContent('Affidabilità');
    await user.unhover(info);
    expect(tip).toHaveAttribute('data-open', 'false');

    act(() => info.focus());
    expect(tip).toHaveAttribute('data-open', 'true');
    await user.keyboard('{Escape}');
    expect(tip).toHaveAttribute('data-open', 'false');
  });

  it('lists the engines each model uses', async () => {
    const { user } = setup({ gpus: ['NVIDIA GeForce RTX 3060'], gpuVramGb: 12 });
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));
    const small = await screen.findByRole('listitem', { name: 'Small' });
    const engines = within(small).getByRole('list', { name: 'Motori usati' });
    expect(within(engines).getByText('Trascrizione: Whisper sulla scheda video')).toBeInTheDocument();
    expect(within(engines).getByText(/Controllo ortografico/)).toBeInTheDocument();
    expect(within(engines).getByText(/Punteggiatura e frasi/)).toBeInTheDocument();
    const parakeet = screen.getByRole('listitem', { name: /Parakeet/ });
    expect(within(parakeet).getByText('Trascrizione: sherpa-onnx sul processore')).toBeInTheDocument();
  });

  it('runs the Test bench on every installed model and lists the measured speed', async () => {
    const { user } = setup({ installed: ['base', 'small'] });
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));
    const bench = await screen.findByRole('region', { name: 'Test bench dei modelli' });
    expect(within(bench).getByText('Nessuna misura ancora.')).toBeInTheDocument();
    expect(within(bench).getByText(/file di riferimento ufficiale/)).toBeInTheDocument();

    await user.click(within(bench).getByRole('button', { name: 'Scegli un file e avvia il test' }));

    expect(await screen.findByText('Test bench completato.')).toBeInTheDocument();
    const table = within(bench).getByRole('table', { name: 'Risultati del test' });
    expect(within(within(table).getByRole('row', { name: /Base/ })).getByText(/32×? il tempo reale|32 il tempo reale|32×/)).toBeInTheDocument();
    expect(within(within(table).getByRole('row', { name: /Small/ })).getByText(/10,7/)).toBeInTheDocument();
  });

  it('shows address, hash and license before downloading the official reference file, and downloads only after confirming', async () => {
    const { user } = setup({ installed: ['base'] });
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));
    const bench = await screen.findByRole('region', { name: 'Test bench dei modelli' });

    await user.click(within(bench).getByRole('button', { name: 'Scarica il file ufficiale e avvia' }));
    const dialog = await screen.findByRole('dialog', { name: 'Scaricare il file di riferimento?' });
    expect(within(dialog).getByText(/https:\/\/archive\.org\/download\//)).toBeInTheDocument();
    expect(within(dialog).getByText(/4d0f7bfe60934ce8/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Pubblico dominio/)).toBeInTheDocument();

    await user.click(within(dialog).getByRole('button', { name: 'Annulla' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(within(bench).getByText('Nessuna misura ancora.')).toBeInTheDocument();

    await user.click(within(bench).getByRole('button', { name: 'Scarica il file ufficiale e avvia' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Scarica e avvia' }));

    expect(await screen.findByText('Test bench completato.')).toBeInTheDocument();
    expect(await within(bench).findByRole('button', { name: 'Usa il file ufficiale e avvia' })).toBeInTheDocument();
  });

  it('can cancel the Test bench and keeps what was already measured', async () => {
    const { user } = setup({ installed: ['base', 'small'], tick: 20 });
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));
    const bench = await screen.findByRole('region', { name: 'Test bench dei modelli' });

    await user.click(within(bench).getByRole('button', { name: 'Scegli un file e avvia il test' }));
    await user.click(await within(bench).findByRole('button', { name: 'Annulla il test' }));

    expect(await screen.findByText(/Test bench annullato/)).toBeInTheDocument();
    expect(within(bench).getByRole('button', { name: 'Scegli un file e avvia il test' })).toBeEnabled();
  });

  it('lists saved Test bench measurements as valid', async () => {
    const { user } = setup({ installed: ['base'], benchmark: { base: 8 } });
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));
    const bench = await screen.findByRole('region', { name: 'Test bench dei modelli' });

    const row = await within(bench).findByRole('row', { name: /Base/ });
    expect(within(row).getByText('Valida')).toBeInTheDocument();
  });

  it('estimates the time of the whole queue from the Test bench before starting', async () => {
    const { user } = setup({ installed: ['base'], batchFiles: ['a.mp3', 'b.mp3'], benchmark: { base: 8 } });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));

    const estimate = await screen.findByTestId('queue-estimate');
    await waitFor(() => expect(estimate).toHaveTextContent(/Tempo stimato: circa/));
    expect(estimate).toHaveTextContent(/Test bench del/);
  });

  it('does not invent a time estimate without a valid Test bench measurement', async () => {
    const { user } = setup({ installed: ['base'], batchFiles: ['a.mp3', 'b.mp3'] });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));

    expect(await screen.findByTestId('queue-estimate')).toHaveTextContent('Per stimare il tempo, esegui il Test bench nella sezione Modelli.');
  });

  it('transcribes a file and shows the model actually used', async () => {
    const { user } = setup({ installed: ['base'] });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    expect((await screen.findAllByText('riunione.mp3')).length).toBeGreaterThan(0);
    await user.selectOptions(screen.getByLabelText('Lingua'), 'it');
    await user.click(screen.getByRole('button', { name: 'Avvia trascrizione' }));

    expect(await screen.findByRole('heading', { name: 'Trascrizione di riunione.mp3' })).toBeInTheDocument();
    expect(screen.getByTestId('model-used')).toHaveTextContent('Modello usato: Base (ggml-base.bin, runtime Cpu)');
    const segments = screen.getAllByRole('textbox', { name: /^Testo del segmento/ });
    expect(segments.map((s) => s.textContent).join(' ')).toBe(
      'Buongiorno a tutti e grazie per essere qui. Oggi parliamo del rilascio della prima versione di CodexVTT. Tutto il lavoro avviene in locale, sul vostro computer.',
    );

    await user.click(screen.getByRole('button', { name: 'Esporta SRT' }));
    expect(await screen.findByText('Salvato: riunione.srt')).toBeInTheDocument();
  });

  it('saves every transcription to history and reopens it', async () => {
    const { user } = setup({ installed: ['base'] });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    await user.click(await screen.findByRole('button', { name: 'Avvia trascrizione' }));
    expect(await screen.findByText('Trascrizione salvata nella cronologia.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Cronologia' }));
    expect(screen.getByText('C:\\Users\\utente\\Documents\\CodexVTT\\history')).toBeInTheDocument();
    const item = await screen.findByRole('listitem', { name: 'riunione.mp3' });
    expect(within(item).getByText(/Buongiorno a tutti/)).toBeInTheDocument();

    await user.click(within(item).getByRole('button', { name: 'Apri riunione.mp3' }));
    expect(await screen.findByRole('heading', { name: 'Trascrizione di riunione.mp3' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Esporta' }));
    await user.click(screen.getByRole('menuitem', { name: 'Esporta TXT' }));
    expect(await screen.findByText('Salvato: riunione.txt')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Cronologia' }));
    await user.click(await screen.findByRole('button', { name: 'Altre azioni per riunione.mp3' }));
    await user.click(screen.getByRole('menuitem', { name: 'Elimina' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Elimina' }));
    expect(await screen.findByText('Nessuna trascrizione salvata finora.')).toBeInTheDocument();
  });

  it('names transcripts and orders them in collections', async () => {
    const { user } = setup({ history: previewHistory.slice(0, 3) });
    await user.click(await screen.findByRole('button', { name: 'Cronologia' }));
    const names = () => screen.getAllByRole('listitem').map((li) => li.getAttribute('aria-label'));

    await user.click(await screen.findByRole('button', { name: 'Altre azioni per memo-vocale-spesa.ogg' }));
    await user.click(screen.getByRole('menuitem', { name: 'Rinomina…' }));
    await user.type(within(screen.getByRole('dialog')).getByLabelText('Nome'), 'Lista della spesa');
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Salva' }));
    expect(await screen.findByRole('listitem', { name: 'Lista della spesa' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Nuova raccolta' }));
    await user.type(within(screen.getByRole('dialog')).getByLabelText('Nome della raccolta'), 'Università');
    await user.click(within(screen.getByRole('dialog')).getByLabelText('Verde'));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Salva' }));
    expect(await screen.findByText(/Questa raccolta è vuota/)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Raccolta da mostrare'), 'all');
    for (const name of ['lezione-storia-moderna.m4a', 'intervista-podcast-ep12.mp3']) {
      await user.click(await screen.findByRole('button', { name: `Altre azioni per ${name}` }));
      await user.click(screen.getByRole('menuitem', { name: 'Raccolta…' }));
      await user.click(within(screen.getByRole('dialog')).getByLabelText('Università'));
      await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Salva' }));
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    }

    await user.selectOptions(screen.getByLabelText('Raccolta da mostrare'), 'Università');
    await waitFor(() => expect(names()).toEqual(['lezione-storia-moderna.m4a', 'intervista-podcast-ep12.mp3']));
    await user.click(screen.getByRole('button', { name: 'Altre azioni per lezione-storia-moderna.m4a' }));
    await user.click(screen.getByRole('menuitem', { name: 'Sposta dopo' }));
    await waitFor(() => expect(names()).toEqual(['intervista-podcast-ep12.mp3', 'lezione-storia-moderna.m4a']));
  });

  it('can change the history folder', async () => {
    const { user } = setup();
    await user.click(await screen.findByRole('button', { name: 'Cronologia' }));

    await user.click(screen.getByRole('button', { name: 'Cambia cartella…' }));

    expect(await screen.findByText('D:\\Trascrizioni')).toBeInTheDocument();
  });

  it('switches the whole interface to English', async () => {
    const { user } = setup({ installed: ['base'] });
    await user.click(await screen.findByRole('button', { name: 'Impostazioni' }));

    await user.selectOptions(screen.getByLabelText('Lingua dell’interfaccia'), 'en');

    expect(await screen.findByRole('heading', { name: 'Settings' })).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('en');
    await user.click(screen.getByRole('button', { name: 'Models' }));
    expect(await screen.findByText('Recommended: Large v3 Turbo (q5_0), the most accurate among the compatible ones.')).toBeInTheDocument();
    expect(screen.getByText('16 GB total RAM: using models that need at most 8 GB.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Home' }));
    await user.click(screen.getByRole('button', { name: 'Add a file to get started' }));
    await user.click(await screen.findByRole('button', { name: 'Start transcription' }));
    expect(await screen.findByTestId('model-used')).toHaveTextContent('Model used: Base (ggml-base.bin, runtime Cpu)');
  });

  it('uses the system language when none is chosen', async () => {
    const client = new BridgeClient(createMockTransport({ tick: 1 }));
    render(<App client={client} />);

    // jsdom riporta en-US: senza scelta esplicita l'interfaccia è in inglese.
    expect(await screen.findByRole('button', { name: 'Choose a model' })).toBeInTheDocument();
  });

  it('cancels a running transcription', async () => {
    const client = new BridgeClient(createMockTransport({ tick: 200, installed: ['base'], uiLanguage: 'it' }));
    const user = userEvent.setup();
    render(<App client={client} />);

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    await user.click(await screen.findByRole('button', { name: 'Avvia trascrizione' }));
    expect(await screen.findByRole('progressbar', { name: 'Avanzamento della trascrizione' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Annulla' }));

    expect(await screen.findByText('Trascrizione annullata.', {}, { timeout: 2000 })).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });

  it('shows errors with a corrective action', async () => {
    const { user } = setup({ installed: ['base'], failures: { 'audio.pickMany': 'audio_codec_missing' } });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Manca il codec per questo formato.');
    expect(alert).toHaveTextContent('Microsoft Store');
    expect(alert).toHaveTextContent('audio_codec_missing');
  });

  it('shows the total of transcribed audio in settings', async () => {
    const { user } = setup({ history: previewHistory.slice(0, 2) });
    await user.click(await screen.findByRole('button', { name: 'Impostazioni' }));

    const stats = await screen.findByRole('region', { name: 'Audio trascritto' });
    expect(within(stats).getByText('2 trascrizioni salvate')).toBeInTheDocument();
  });

  it('applies the theme chosen in settings', async () => {
    const { user } = setup();
    await user.click(await screen.findByRole('button', { name: 'Impostazioni' }));

    await user.selectOptions(await screen.findByLabelText('Aspetto'), 'dark');
    await waitFor(() => expect(document.documentElement.dataset.theme).toBe('dark'));

    await user.selectOptions(screen.getByLabelText('Aspetto'), 'system');
    await waitFor(() => expect(document.documentElement.dataset.theme).toBeUndefined());
  });

  it('never uses the network', async () => {
    const { user } = setup({ installed: ['base'] });
    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    await user.click(await screen.findByRole('button', { name: 'Avvia trascrizione' }));
    await screen.findByRole('heading', { name: 'Trascrizione di riunione.mp3' });

    expect(fetchSpy).not.toHaveBeenCalled();
  });
});

describe('App in modalità server', () => {
  function setupServer(options: MockOptions = {}) {
    const download = vi.fn();
    const files = {
      pickAudio: vi.fn(async () => ({
        picked: [{ audioId: 'batch-up', info: { fileName: 'caricato.mp3', format: 'mp3', duration: 60, sampleRate: 44100, channels: 2, sizeBytes: 1000 } }],
        failed: [],
      })),
      download,
    };
    const client = new BridgeClient(createMockTransport({ tick: 1, uiLanguage: 'it', host: 'server', ...options }), files);
    const user = userEvent.setup();
    render(<App client={client} />);
    return { user, files };
  }

  it('carica i file dal browser e scarica gli export invece dei dialog nativi', async () => {
    const { user, files } = setupServer({ installed: ['base'] });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    expect((await screen.findAllByText('caricato.mp3')).length).toBeGreaterThan(0);
    expect(files.pickAudio).toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Avvia trascrizione' }));
    await screen.findByRole('heading', { name: /Trascrizione di/ });

    await user.click(screen.getByRole('button', { name: 'Esporta SRT' }));
    expect(files.download).toHaveBeenCalledWith(expect.objectContaining({ format: 'srt', historyId: expect.any(String) }));
    expect(await screen.findByText(/Download avviato/)).toBeInTheDocument();
  });

  it('nasconde le azioni che richiedono il PC del server', async () => {
    const { user } = setupServer({ installed: ['base'] });

    await user.click(await screen.findByRole('button', { name: 'Modelli' }));
    expect(screen.queryByRole('button', { name: /Importa/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Impostazioni' }));
    expect(screen.queryByRole('button', { name: /Apri la cartella|Apri i log/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cambia cartella/ })).not.toBeInTheDocument();
  });
});

describe('Aggiornamenti da GitHub', () => {
  async function openSettings(options: MockOptions = {}) {
    const { user } = setup({ installed: ['base'], ...options });
    await user.click(await screen.findByRole('button', { name: 'Impostazioni' }));
    return user;
  }

  it('non contatta GitHub finché non lo chiede l’utente e dice quando si è aggiornati', async () => {
    const user = await openSettings();
    const card = await screen.findByRole('group', { name: 'Aggiornamenti' });
    expect(within(card).getByText('Hai l’ultima versione (0.1.0).')).toBeInTheDocument();
    expect(within(card).getByLabelText('Controlla gli aggiornamenti all’avvio')).not.toBeChecked();

    await user.click(within(card).getByRole('button', { name: 'Controlla aggiornamenti' }));

    // Stesso testo nella scheda e nell'avviso che conferma il controllo.
    await waitFor(() => expect(screen.getAllByText('Hai l’ultima versione (0.1.0).')).toHaveLength(2));
  });

  it('propone la nuova versione e mostra l’avanzamento dell’installazione', async () => {
    const user = await openSettings({ updates: { state: 'available', currentVersion: '0.1.0', latestVersion: '0.2.0', notes: 'Nuova coda batch' } });
    const card = await screen.findByRole('group', { name: 'Aggiornamenti' });

    expect(within(card).getByText('È disponibile la versione 0.2.0 (hai la 0.1.0).')).toBeInTheDocument();
    expect(within(card).getByText('Nuova coda batch')).toBeInTheDocument();
    await user.click(within(card).getByRole('button', { name: 'Installa 0.2.0 e riavvia' }));

    expect(await within(card).findByText('Scarico l’aggiornamento… 100%')).toBeInTheDocument();
  });

  it('ricorda la scelta del controllo automatico', async () => {
    const user = await openSettings();
    const card = await screen.findByRole('group', { name: 'Aggiornamenti' });

    await user.click(within(card).getByLabelText('Controlla gli aggiornamenti all’avvio'));

    await waitFor(() => expect(within(card).getByLabelText('Controlla gli aggiornamenti all’avvio')).toBeChecked());
  });

  it('spiega perché non ci sono aggiornamenti in una copia non installata e non li mostra nel server', async () => {
    await openSettings({ updates: { state: 'unsupported', currentVersion: '0.1.0' } });
    expect(await screen.findByText(/non è stata installata con l’installer/)).toBeInTheDocument();
  });

  it('nella versione server la scheda non c’è', async () => {
    await openSettings({ host: 'server' });
    await screen.findByRole('heading', { name: 'Impostazioni' });
    expect(screen.queryByRole('group', { name: 'Aggiornamenti' })).not.toBeInTheDocument();
  });
});

describe('Nuovi modelli e GPU', () => {
  it('mostra anteprima, motore, affidabilità dichiarata e attribuzione nella scheda Modelli', async () => {
    const { user } = setup({ installed: ['base'] });
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));

    const parakeet = await screen.findByRole('listitem', { name: 'Parakeet TDT 0.6B v3 (int8)' });
    expect(within(parakeet).getByText('Anteprima')).toBeInTheDocument();
    expect(within(parakeet).getByText(/Trascrizione: sherpa-onnx/)).toBeInTheDocument();
    // L'affidabilità sta nella «i» del modello, non più come testo fisso nella scheda.
    const tip = within(parakeet).getByRole('tooltip', { hidden: true });
    expect(tip).toHaveTextContent('Affidabilità ~97%');
    expect(tip).toHaveTextContent('non misurato da CodexVTT');
    expect(within(parakeet).getByText(/by NVIDIA, licensed under CC-BY-4\.0/)).toBeInTheDocument();

    const base = screen.getByRole('listitem', { name: 'Base' });
    expect(within(base).getByText(/Trascrizione: Whisper/)).toBeInTheDocument();
    expect(within(base).getByRole('tooltip', { hidden: true })).toHaveTextContent('Affidabilità non ancora misurata');
    expect(within(base).queryByText('Anteprima')).not.toBeInTheDocument();

    const canary = screen.getByRole('listitem', { name: 'Canary 1B v2 (int8)' });
    expect(within(canary).getByText(/richiede di scegliere la lingua/)).toBeInTheDocument();
  });

  it('un modello che non rileva la lingua non parte con «Rilevamento automatico»', async () => {
    const { user } = setup({ installed: ['canary-1b-v2-int8'] });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    const start = await screen.findByRole('button', { name: 'Avvia trascrizione' });
    expect(start).toBeDisabled();
    expect(screen.getByText(/richiede di scegliere la lingua/)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Lingua'), 'it');
    expect(screen.getByRole('button', { name: 'Avvia trascrizione' })).toBeEnabled();
  });

  it('la scheda video è usata di default e si può spegnere dalle impostazioni', async () => {
    const { user } = setup({ installed: ['base'] });
    await user.click(await screen.findByRole('button', { name: 'Impostazioni' }));

    const gpu = await screen.findByLabelText('Usa la scheda video (GPU) per Whisper quando disponibile');
    expect(gpu).toBeChecked();
    await user.click(gpu);

    await waitFor(() => expect(screen.getByLabelText('Usa la scheda video (GPU) per Whisper quando disponibile')).not.toBeChecked());
  });
});
describe('Scheda video rilevata', () => {
  it('mostra la scheda trovata oppure dice che si usa la CPU', async () => {
    const withGpu = setup({ installed: ['base'], gpus: ['NVIDIA GeForce RTX 4070'] });
    await withGpu.user.click(await screen.findByRole('button', { name: 'Modelli' }));
    expect(await screen.findByText('NVIDIA GeForce RTX 4070')).toBeInTheDocument();
  });

  it('senza scheda compatibile lo dice', async () => {
    const { user } = setup({ installed: ['base'] });
    await user.click(await screen.findByRole('button', { name: 'Modelli' }));
    expect(await screen.findByText('nessuna compatibile con Vulkan: Whisper usa la CPU')).toBeInTheDocument();
  });
});
describe('Tempo rimanente', () => {
  it('mostra una stima del tempo rimanente mentre trascrive, dopo il primo avanzamento', async () => {
    const { user } = setup({ installed: ['base'], tick: 60, jobStep: 20 });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    await user.click(await screen.findByRole('button', { name: 'Avvia trascrizione' }));

    // Prima non c'è nessuna stima, poi compare quella del core («circa N min»).
    expect(await screen.findByTestId('eta')).toHaveTextContent('Calcolo del tempo rimanente…');
    await waitFor(() => expect(screen.getByTestId('eta')).toHaveTextContent(/Tempo rimanente: circa \d+ min/));
  });

  it('per una coda mostra anche il tempo per tutti i file', async () => {
    const { user } = setup({ installed: ['base'], batchFiles: ['a.mp3', 'b.mp3', 'c.mp3'], tick: 60, jobStep: 20 });

    await user.click(await screen.findByRole('button', { name: 'Inserisci un file per iniziare' }));
    await user.click(await screen.findByRole('button', { name: 'Avvia coda' }));

    await waitFor(() => expect(screen.getByTestId('eta')).toHaveTextContent(/Per tutta la coda: circa \d+ min/));
  });
});
