import { TestBed } from '@angular/core/testing';
import { Router, RouterStateSnapshot } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { firstValueFrom, of } from 'rxjs';
import { authGuard } from './auth.guard';
import { AuthSessionService } from './auth-session.service';
import { OFFLINE_PATH } from './offline-route';
import type { SessionOutcome } from './auth-session.service';

describe('authGuard', () => {
  const urlTree = { marker: 'url-tree' };

  beforeEach(() => TestBed.resetTestingModule());

  const run = (outcome: SessionOutcome, url = '/admin/schedule') => {
    const createUrlTree = vi.fn().mockReturnValue(urlTree);
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthSessionService, useValue: { hydrate: () => of(outcome) } },
        { provide: Router, useValue: { createUrlTree } },
      ],
    });
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url } as RouterStateSnapshot),
    );
    return { result, createUrlTree };
  };

  it('przepuszcza zalogowanego', async () => {
    const { result, createUrlTree } = run({ kind: 'authenticated', session: { userId: 'u1' } as never });

    await expect(firstValueFrom(result as never)).resolves.toBe(true);
    expect(createUrlTree).not.toHaveBeenCalled();
  });

  it('odsyła na /login, gdy serwer potwierdził brak sesji (401)', async () => {
    const { result, createUrlTree } = run({ kind: 'anonymous' });

    await expect(firstValueFrom(result as never)).resolves.toBe(urlTree);
    expect(createUrlTree).toHaveBeenCalledWith(['/login']);
  });

  /**
   * Sedno zgłoszenia „wylogowuje mnie cały czas": brak sieci NIE jest wylogowaniem, więc `/login`
   * byłoby kłamstwem (a bez sieci i tak nie da się zalogować).
   */
  it('przy braku łączności idzie na /offline z powrotnym URL — nie na /login', async () => {
    const { result, createUrlTree } = run({ kind: 'unavailable' }, '/admin/customers');

    await expect(firstValueFrom(result as never)).resolves.toBe(urlTree);
    expect(createUrlTree).toHaveBeenCalledWith([OFFLINE_PATH], {
      queryParams: { returnUrl: '/admin/customers' },
    });
    expect(createUrlTree).not.toHaveBeenCalledWith(['/login']);
  });
});
