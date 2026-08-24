import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SwUpdate, VersionEvent } from '@angular/service-worker';
import { ConfirmationService } from 'primeng/api';
import { Subject } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PwaUpdateService } from './pwa-update.service';

describe('PwaUpdateService', () => {
  let versionUpdates: Subject<VersionEvent>;
  let unrecoverable: Subject<unknown>;
  let sw: {
    isEnabled: boolean;
    versionUpdates: Subject<VersionEvent>;
    unrecoverable: Subject<unknown>;
    activateUpdate: ReturnType<typeof vi.fn>;
    checkForUpdate: ReturnType<typeof vi.fn>;
  };
  let confirmation: { confirm: ReturnType<typeof vi.fn> };
  let reload: ReturnType<typeof vi.fn>;
  /** Handler `visibilitychange` zarejestrowany przez serwis — pozwala odegrać powrót z tła. */
  let onVisibility: (() => void) | undefined;
  let visibilityState: DocumentVisibilityState;

  function build(isEnabled: boolean, standalone = true) {
    versionUpdates = new Subject<VersionEvent>();
    unrecoverable = new Subject<unknown>();
    reload = vi.fn();
    onVisibility = undefined;
    visibilityState = 'visible';
    sw = {
      isEnabled,
      versionUpdates,
      unrecoverable,
      activateUpdate: vi.fn().mockResolvedValue(true),
      checkForUpdate: vi.fn().mockResolvedValue(false),
    };
    confirmation = { confirm: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        PwaUpdateService,
        { provide: SwUpdate, useValue: sw },
        { provide: ConfirmationService, useValue: confirmation },
        {
          provide: DOCUMENT,
          useValue: {
            location: { reload },
            get visibilityState() {
              return visibilityState;
            },
            addEventListener: (type: string, handler: () => void) => {
              if (type === 'visibilitychange') onVisibility = handler;
            },
            defaultView: {
              matchMedia: () => ({ matches: standalone }),
              navigator: { standalone },
            },
          },
        },
      ],
    });
    return TestBed.inject(PwaUpdateService);
  }

  beforeEach(() => vi.clearAllMocks());

  it('does nothing when the service worker is disabled', () => {
    const service = build(false);
    service.init();

    versionUpdates.next({ type: 'VERSION_READY' } as VersionEvent);
    expect(confirmation.confirm).not.toHaveBeenCalled();
  });

  it('prompts to reload when a new version is ready', () => {
    const service = build(true);
    service.init();

    versionUpdates.next({ type: 'VERSION_DETECTED' } as VersionEvent);
    expect(confirmation.confirm).not.toHaveBeenCalled();

    versionUpdates.next({ type: 'VERSION_READY' } as VersionEvent);
    expect(confirmation.confirm).toHaveBeenCalledTimes(1);
    expect(confirmation.confirm.mock.calls[0][0]).toMatchObject({ acceptLabel: 'Odśwież' });
  });

  it('does not prompt on a plain website (not standalone)', () => {
    const service = build(true, false);
    service.init();

    versionUpdates.next({ type: 'VERSION_READY' } as VersionEvent);
    expect(confirmation.confirm).not.toHaveBeenCalled();
  });

  it('activates the update and reloads when accepted', async () => {
    const service = build(true);
    service.init();
    versionUpdates.next({ type: 'VERSION_READY' } as VersionEvent);

    const accept = confirmation.confirm.mock.calls[0][0].accept as () => void;
    accept();
    await Promise.resolve();
    await Promise.resolve();

    expect(sw.activateUpdate).toHaveBeenCalledTimes(1);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('hard-reloads on an unrecoverable state', () => {
    const service = build(true);
    service.init();

    unrecoverable.next({ reason: 'boom' });
    expect(reload).toHaveBeenCalledTimes(1);
  });

  /**
   * PWA na iOS bywa tygodniami zawieszana i wznawiana bez pełnego startu, więc ani rejestracja SW,
   * ani nawigacja nie następują — bez tej sondy telefon zostawał na starym buildzie mimo nowego
   * serwera. Objawem były błędy ładowania chunków, bo stary `index.html` wskazuje na hashe,
   * których już nie ma.
   */
  describe('sonda wersji przy powrocie z tła', () => {
    it('pyta o aktualizację, gdy aplikacja wraca na pierwszy plan', () => {
      const service = build(true);
      service.init();

      expect(sw.checkForUpdate).not.toHaveBeenCalled();

      onVisibility?.();
      expect(sw.checkForUpdate).toHaveBeenCalledTimes(1);
    });

    it('nie pyta, gdy aplikacja schodzi w tło', () => {
      const service = build(true);
      service.init();

      visibilityState = 'hidden';
      onVisibility?.();

      expect(sw.checkForUpdate).not.toHaveBeenCalled();
    });

    it('throttluje serie przełączeń — jedno sprawdzenie, nie dziesięć', () => {
      const service = build(true);
      service.init();

      for (let i = 0; i < 10; i++) onVisibility?.();

      expect(sw.checkForUpdate).toHaveBeenCalledTimes(1);
    });

    it('nie rejestruje sondy, gdy service worker jest wyłączony', () => {
      const service = build(false);
      service.init();

      expect(onVisibility).toBeUndefined();
    });

    it('połyka odrzucenie sondy — offline nie jest błędem do pokazania', async () => {
      const service = build(true);
      sw.checkForUpdate.mockRejectedValue(new Error('offline'));
      service.init();

      onVisibility?.();
      await Promise.resolve();

      expect(reload).not.toHaveBeenCalled();
      expect(confirmation.confirm).not.toHaveBeenCalled();
    });
  });
});
