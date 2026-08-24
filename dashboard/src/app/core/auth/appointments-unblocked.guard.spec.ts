import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BookingPauseStore } from '@core/services/booking-pause.store';
import { appointmentsUnblockedGuard } from './appointments-unblocked.guard';
import { AuthSessionService } from './auth-session.service';

describe('appointmentsUnblockedGuard', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  const run = (blocked: boolean, roles: string[]) => {
    const urlTree = { redirectedTo: '/admin/schedule' };
    const storeMock = {
      ensureLoaded: vi.fn().mockResolvedValue(undefined),
      appointmentsBlocked: vi.fn().mockReturnValue(blocked),
    };
    const authMock = {
      session: vi.fn().mockReturnValue({ roles }),
    };
    const routerMock = {
      createUrlTree: vi.fn().mockReturnValue(urlTree),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: BookingPauseStore, useValue: storeMock },
        { provide: AuthSessionService, useValue: authMock },
        { provide: Router, useValue: routerMock },
      ],
    });

    const result = TestBed.runInInjectionContext(() =>
      appointmentsUnblockedGuard({} as never, {} as never),
    ) as Promise<unknown>;

    return { result, urlTree, storeMock, routerMock };
  };

  it('przepuszcza, gdy zarządzanie wizytami nie jest zablokowane', async () => {
    const { result, routerMock } = run(false, ['Owner']);
    await expect(result).resolves.toBe(true);
    expect(routerMock.createUrlTree).not.toHaveBeenCalled();
  });

  it('przekierowuje ownera na kalendarz, gdy zablokowane', async () => {
    const { result, urlTree, routerMock } = run(true, ['Owner']);
    await expect(result).resolves.toBe(urlTree);
    expect(routerMock.createUrlTree).toHaveBeenCalledWith(['/admin/schedule']);
  });

  it('przekierowuje managera na kalendarz, gdy zablokowane', async () => {
    const { result, urlTree } = run(true, ['Manager']);
    await expect(result).resolves.toBe(urlTree);
  });

  it('przepuszcza pracownika (widok-only), gdy zablokowane', async () => {
    const { result, routerMock } = run(true, ['Employee']);
    await expect(result).resolves.toBe(true);
    expect(routerMock.createUrlTree).not.toHaveBeenCalled();
  });

  it('czeka na ensureLoaded przed decyzją', async () => {
    const { result, storeMock } = run(false, ['Owner']);
    await result;
    expect(storeMock.ensureLoaded).toHaveBeenCalledOnce();
  });
});
