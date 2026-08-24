import '@testing-library/svelte/vitest';

// jsdom nie implementuje scrollIntoView — komponenty (np. DateStrip) wołają je po zmianie dnia.
if (typeof Element !== 'undefined' && !Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {};
}
