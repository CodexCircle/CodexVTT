import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import axe from 'axe-core';
import { describe, expect, it } from 'vitest';
import { App } from './App';
import { BridgeClient } from './bridge/client';
import { createMockTransport } from './bridge/mockTransport';
import { previewHistory } from './preview/seed';

/** Controllo automatico di accessibilità (issue #6): nessuna violazione seria o critica nella schermata iniziale. */
describe('accessibilità della schermata iniziale', () => {
  it('non ha violazioni axe di gravità seria o critica', async () => {
    const client = new BridgeClient(createMockTransport({ tick: 1, uiLanguage: 'it', installed: ['small'] }));
    const { container, findByRole } = render(<App client={client} />);
    await findByRole('button', { name: 'Trascrivi' }).catch(() => undefined);

    const result = await axe.run(container, { rules: { 'color-contrast': { enabled: false } } });
    const serious = result.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');

    expect(serious.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
  });
});

describe('accessibilità dell’editor', () => {
  it('non ha violazioni axe di gravità seria o critica con una trascrizione aperta', async () => {
    const client = new BridgeClient(createMockTransport({ tick: 1, uiLanguage: 'it', installed: ['small'], history: previewHistory.slice(0, 2) }));
    const { container } = render(<App client={client} />);
    const recent = await screen.findByRole('list', { name: 'Recenti' });
    await userEvent.click(within(recent).getAllByRole('button')[0]);
    await screen.findByRole('textbox', { name: /^Testo del segmento 1/ });

    const result = await axe.run(container, { rules: { 'color-contrast': { enabled: false } } });
    const serious = result.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');

    expect(serious.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
  });
});
