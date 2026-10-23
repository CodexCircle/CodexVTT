import { render } from '@testing-library/react';
import axe from 'axe-core';
import { describe, expect, it } from 'vitest';
import { App } from './App';
import { BridgeClient } from './bridge/client';
import { createMockTransport } from './bridge/mockTransport';

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
