import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import Counter from './Counter.svelte';

describe('Counter', () => {
  it('startuje od zera i zwiększa licznik po kliknięciu', async () => {
    render(Counter);

    const btn = screen.getByRole('button', { name: /Kliknięć: 0/ });
    await btn.click();

    expect(screen.getByRole('button', { name: /Kliknięć: 1/ })).toBeTruthy();
  });
});
