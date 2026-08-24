import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BackButtonCloseService } from './back-button-close.service';

function makeWindow() {
  const listeners: Record<string, Array<() => void>> = {};
  let state: unknown = null;
  const win = {
    history: {
      get state() {
        return state;
      },
      pushState: vi.fn((s: unknown) => {
        state = s;
      }),
      back: vi.fn(() => {
        // W realnej przeglądarce back() zdejmuje wpis i emituje popstate — symulujemy w teście ręcznie.
        state = null;
      }),
    },
    addEventListener: (type: string, cb: () => void) => {
      (listeners[type] ??= []).push(cb);
    },
    firePopstate: () => (listeners['popstate'] ?? []).forEach((cb) => cb()),
  };
  return win;
}

type FakeWindow = ReturnType<typeof makeWindow>;

function build(win: FakeWindow): BackButtonCloseService {
  TestBed.configureTestingModule({
    providers: [BackButtonCloseService, { provide: DOCUMENT, useValue: { defaultView: win } }],
  });
  return TestBed.inject(BackButtonCloseService);
}

describe('BackButtonCloseService', () => {
  beforeEach(() => TestBed.resetTestingModule());

  it('push() dokłada wpis historii; „wstecz" zamyka nakładkę', () => {
    const win = makeWindow();
    const service = build(win);
    const close = vi.fn();

    service.push(close);
    expect(win.history.pushState).toHaveBeenCalledTimes(1);

    win.firePopstate(); // naciśnięcie „wstecz"
    expect(close).toHaveBeenCalledTimes(1);
  });

  it('zamyka nakładki w kolejności LIFO', () => {
    const win = makeWindow();
    const service = build(win);
    const closeA = vi.fn();
    const closeB = vi.fn();

    service.push(closeA);
    service.push(closeB);

    win.firePopstate();
    expect(closeB).toHaveBeenCalledTimes(1);
    expect(closeA).not.toHaveBeenCalled();

    win.firePopstate();
    expect(closeA).toHaveBeenCalledTimes(1);
  });

  it('pop() (zamknięcie z UI) sprząta wpis i nie zamyka nakładki ponownie', () => {
    const win = makeWindow();
    const service = build(win);
    const close = vi.fn();

    service.push(close); // ustawia history.state = { __overlay: 1 }
    service.pop(close);

    expect(win.history.back).toHaveBeenCalledTimes(1);

    // back() w przeglądarce wyemitowałby popstate — powinien być zignorowany (suppress).
    win.firePopstate();
    expect(close).not.toHaveBeenCalled();
  });

  it('pop() nie cofa historii, jeśli użytkownik nawigował dalej (wpis nie jest na wierzchu)', () => {
    const win = makeWindow();
    const service = build(win);
    const close = vi.fn();

    service.push(close);
    win.history.pushState({ page: 'inna' }); // symulacja nawigacji po otwarciu (np. routerLink)
    service.pop(close);

    // Wpis nakładki jest „pod" nową stroną — nie ruszamy historii.
    expect(win.history.back).not.toHaveBeenCalled();
  });
});
