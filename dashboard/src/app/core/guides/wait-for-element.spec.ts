import { describe, expect, it, afterEach } from 'vitest';
import { isElementVisible, waitForElement } from './wait-for-element';

describe('waitForElement', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('zwraca element natychmiast, gdy już istnieje w DOM', async () => {
    const el = document.createElement('div');
    el.setAttribute('data-tour', 'foo');
    document.body.appendChild(el);

    const found = await waitForElement('[data-tour="foo"]');
    expect(found).toBe(el);
  });

  it('czeka aż element pojawi się dynamicznie', async () => {
    const promise = waitForElement('[data-tour="late"]', 1000);

    setTimeout(() => {
      const el = document.createElement('div');
      el.setAttribute('data-tour', 'late');
      document.body.appendChild(el);
    }, 20);

    const found = await promise;
    expect(found).not.toBeNull();
    expect(found?.getAttribute('data-tour')).toBe('late');
  });

  it('zwraca null po timeoucie, gdy element się nie pojawi (fallback)', async () => {
    const found = await waitForElement('[data-tour="never"]', 50);
    expect(found).toBeNull();
  });
});

describe('isElementVisible', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('zwraca false dla nieobecnego selektora', () => {
    expect(isElementVisible('[data-tour="ghost"]')).toBe(false);
  });

  it('zwraca false dla elementu z display:none', () => {
    const el = document.createElement('div');
    el.setAttribute('data-tour', 'hidden');
    el.style.display = 'none';
    document.body.appendChild(el);
    expect(isElementVisible('[data-tour="hidden"]')).toBe(false);
  });
});
