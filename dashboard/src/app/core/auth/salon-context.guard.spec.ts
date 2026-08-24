import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { UserRole } from '@core/services/NavigationService';
import { firstValueFrom, of } from 'rxjs';
import { salonContextGuard } from './salon-context.guard';
import { AuthSessionService } from './auth-session.service';

describe('salonContextGuard', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  const runGuard = (
    role: UserRole | null,
    employeeId: string | null,
    outcomeKind: 'authenticated' | 'unavailable' = 'authenticated',
  ) => {
    const urlTree = { redirectedTo: '/admin/system/tenants' };
    const authMock = {
      hydrate: vi.fn().mockReturnValue(of({ kind: outcomeKind, session: { userId: 'user-id' } })),
      currentRole: vi.fn().mockReturnValue(role),
      currentEmployeeId: vi.fn().mockReturnValue(employeeId),
    };
    const routerMock = { createUrlTree: vi.fn().mockReturnValue(urlTree) };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthSessionService, useValue: authMock },
        { provide: Router, useValue: routerMock },
      ],
    });

    const result = TestBed.runInInjectionContext(() =>
      salonContextGuard({} as never, { url: '/admin/schedule' } as never),
    );

    return { result, urlTree, authMock, routerMock };
  };

  it.each(['owner', 'manager', 'employee', 'kiosk'] as UserRole[])(
    'wpuszcza rolę salonową: %s',
    async (role) => {
      const { result, routerMock } = runGuard(role, 'employee-id');

      await expect(firstValueFrom(result as never)).resolves.toBe(true);
      expect(routerMock.createUrlTree).not.toHaveBeenCalled();
    },
  );

  it('odsyła admina platformy BEZ własnego pracownika do sekcji systemowej', async () => {
    const { result, urlTree, routerMock } = runGuard('systemAdmin', null);

    await expect(firstValueFrom(result as never)).resolves.toBe(urlTree);
    expect(routerMock.createUrlTree).toHaveBeenCalledWith(['/admin/system/tenants']);
  });

  it('wpuszcza admina, który MA własnego pracownika — to jego kalendarz', async () => {
    const { result, routerMock } = runGuard('systemAdmin', 'employee-id');

    await expect(firstValueFrom(result as never)).resolves.toBe(true);
    expect(routerMock.createUrlTree).not.toHaveBeenCalled();
  });

  it('czeka na hydratację — nieznana rola nie jest traktowana jak admin', async () => {
    // Sedno poprawki: przed hydratacją `currentRole()` zwraca null. Guard nie może wtedy ani
    // odesłać (bo to nie musi być admin), ani przepuścić na ślepo — dlatego opiera się na
    // `hydrate()`, które emituje dopiero ze znaną sesją.
    const { result, authMock, routerMock } = runGuard('owner', 'employee-id');

    await expect(firstValueFrom(result as never)).resolves.toBe(true);
    expect(authMock.hydrate).toHaveBeenCalledOnce();
    expect(routerMock.createUrlTree).not.toHaveBeenCalled();
  });
});
