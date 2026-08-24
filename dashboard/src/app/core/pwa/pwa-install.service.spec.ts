import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PwaInstallService } from './pwa-install.service';

type Handler = (event: unknown) => void;

interface FakeWindow {
  listeners: Record<string, Handler[]>;
  addEventListener: (type: string, cb: Handler) => void;
  dispatch: (type: string, event: unknown) => void;
  matchMedia: (query: string) => { matches: boolean };
  navigator: { userAgent: string; maxTouchPoints: number; standalone?: boolean };
  localStorage: { getItem: (k: string) => string | null; setItem: (k: string, v: string) => void };
}

function makeWindow(opts: { ua?: string; maxTouchPoints?: number; matches?: boolean } = {}): FakeWindow {
  const listeners: Record<string, Handler[]> = {};
  const store = new Map<string, string>();
  return {
    listeners,
    addEventListener: (type, cb) => {
      (listeners[type] ??= []).push(cb);
    },
    dispatch: (type, event) => {
      (listeners[type] ?? []).forEach((cb) => cb(event));
    },
    matchMedia: () => ({ matches: opts.matches ?? false }),
    navigator: {
      userAgent: opts.ua ?? 'Mozilla/5.0 (Linux; Android 13)',
      maxTouchPoints: opts.maxTouchPoints ?? 0,
    },
    localStorage: {
      getItem: (k) => store.get(k) ?? null,
      setItem: (k, v) => void store.set(k, v),
    },
  };
}

const IPHONE_UA = 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)';
const IPAD_UA = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15)';
const SAFARI_IPHONE_UA =
  'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1';
const CHROME_IPHONE_UA =
  'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/120.0.0.0 Mobile/15E148 Safari/604.1';

function build(win: FakeWindow): PwaInstallService {
  TestBed.configureTestingModule({
    providers: [PwaInstallService, { provide: DOCUMENT, useValue: { defaultView: win } }],
  });
  return TestBed.inject(PwaInstallService);
}

describe('PwaInstallService', () => {
  beforeEach(() => TestBed.resetTestingModule());

  it('Android: przechwytuje beforeinstallprompt i pozwala uruchomić natywny prompt', async () => {
    const win = makeWindow({ ua: 'Mozilla/5.0 (Linux; Android 13)' });
    const service = build(win);
    service.init();
    expect(service.available()).toBe(false);

    const evt = {
      preventDefault: vi.fn(),
      prompt: vi.fn().mockResolvedValue(undefined),
      userChoice: Promise.resolve({ outcome: 'accepted' as const }),
    };
    win.dispatch('beforeinstallprompt', evt);

    expect(evt.preventDefault).toHaveBeenCalledTimes(1);
    expect(service.canInstall()).toBe(true);
    expect(service.available()).toBe(true);

    await service.promptInstall();
    expect(evt.prompt).toHaveBeenCalledTimes(1);
    expect(service.canInstall()).toBe(false); // prompt jednorazowy
  });

  it('iOS: pokazuje instrukcję zamiast natywnego promptu', () => {
    const win = makeWindow({ ua: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)' });
    const service = build(win);
    service.init();
    expect(service.iosInstructions()).toBe(true);
    expect(service.canInstall()).toBe(false);
    expect(service.available()).toBe(true);
  });

  it('iPadOS (UA Maca + ekran dotykowy) traktuje jako iOS', () => {
    const win = makeWindow({ ua: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15)', maxTouchPoints: 5 });
    const service = build(win);
    service.init();
    expect(service.iosInstructions()).toBe(true);
  });

  it('gdy aplikacja działa już jako PWA (standalone) — nic nie proponuje', () => {
    const win = makeWindow({ ua: 'Mozilla/5.0 (Linux; Android 13)', matches: true });
    const service = build(win);
    service.init();
    win.dispatch('beforeinstallprompt', { preventDefault: vi.fn() });
    expect(service.available()).toBe(false);
  });

  it('appinstalled czyści stan instalacji', () => {
    const win = makeWindow({ ua: 'Mozilla/5.0 (Linux; Android 13)' });
    const service = build(win);
    service.init();
    win.dispatch('beforeinstallprompt', {
      preventDefault: vi.fn(),
      prompt: vi.fn(),
      userChoice: Promise.resolve({ outcome: 'accepted' as const }),
    });
    expect(service.canInstall()).toBe(true);

    win.dispatch('appinstalled', {});
    expect(service.canInstall()).toBe(false);
    expect(service.available()).toBe(false);
  });

  it('sharePosition: iPad → góra (Udostępnij w górnym pasku)', () => {
    const service = build(makeWindow({ ua: IPAD_UA, maxTouchPoints: 5 }));
    service.init();
    expect(service.sharePosition()).toBe('top');
  });

  it('sharePosition: iPhone → dół', () => {
    const service = build(makeWindow({ ua: IPHONE_UA }));
    service.init();
    expect(service.sharePosition()).toBe('bottom');
  });

  it('maybeAutoShowIosGuide: pokazuje przewodnik raz na iOS, potem no-op', () => {
    const service = build(makeWindow({ ua: IPHONE_UA }));
    service.init();
    expect(service.iosGuideOpen()).toBe(false);

    service.maybeAutoShowIosGuide();
    expect(service.iosGuideOpen()).toBe(true);

    service.closeIosGuide();
    service.maybeAutoShowIosGuide(); // już pokazane w tej sesji
    expect(service.iosGuideOpen()).toBe(false);
  });

  it('maybeAutoShowIosGuide: nie-iOS → nic nie pokazuje', () => {
    const service = build(makeWindow({ ua: 'Mozilla/5.0 (Linux; Android 13)' }));
    service.init();
    service.maybeAutoShowIosGuide();
    expect(service.iosGuideOpen()).toBe(false);
  });

  it('dismissIosGuideForever: zapamiętuje wybór i blokuje auto-pokaz w kolejnej sesji', () => {
    const win = makeWindow({ ua: IPHONE_UA });
    const service = build(win);
    service.init();
    service.openIosGuide();
    service.dismissIosGuideForever();

    expect(service.iosGuideOpen()).toBe(false);
    expect(win.localStorage.getItem('zm.iosInstallGuideDismissed')).toBe('1');

    // Nowa sesja (świeży serwis) z tym samym localStorage — auto już się nie pokaże.
    TestBed.resetTestingModule();
    const next = build(win);
    next.init();
    next.maybeAutoShowIosGuide();
    expect(next.iosGuideOpen()).toBe(false);
  });

  it('iosVariant: iOS Safari → "safari" (kroki Udostępnij)', () => {
    const service = build(makeWindow({ ua: SAFARI_IPHONE_UA }));
    service.init();
    expect(service.iosVariant()).toBe('safari');
  });

  it('iosVariant: iOS Chrome (CriOS) → "other" (kieruj do Safari)', () => {
    const service = build(makeWindow({ ua: CHROME_IPHONE_UA }));
    service.init();
    expect(service.iosVariant()).toBe('other');
  });

  it('iosVariant: nie-iOS → "other"', () => {
    const service = build(makeWindow({ ua: 'Mozilla/5.0 (Linux; Android 13)' }));
    service.init();
    expect(service.iosVariant()).toBe('other');
  });
});
