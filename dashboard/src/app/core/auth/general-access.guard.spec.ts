import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { UserRole } from '@core/services/NavigationService';
import { generalAccessGuard } from './general-access.guard';
import { AuthSessionService } from './auth-session.service';
import { firstValueFrom, of } from 'rxjs';
import { MessageService } from 'primeng/api';

describe('generalAccessGuard', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  const runGuardForRole = (role: UserRole) => {
    const urlTree = { redirectedTo: '/admin/schedule' };
    const authMock = {
      hydrate: vi.fn().mockReturnValue(of({ kind: 'authenticated', session: { userId: 'user-id' } })),
      currentRole: vi.fn().mockReturnValue(role),
    };
    const routerMock = {
      createUrlTree: vi.fn().mockReturnValue(urlTree),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthSessionService, useValue: authMock },
        { provide: Router, useValue: routerMock },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    });

    const result = TestBed.runInInjectionContext(() =>
      generalAccessGuard({} as never, {} as never),
    );

    return { result, urlTree, authMock, routerMock };
  };

  it.each(['owner', 'manager', 'employee'] as UserRole[])('allows %s users', async (role) => {
    const { result, authMock, routerMock } = runGuardForRole(role);

    await expect(firstValueFrom(result as never)).resolves.toBe(true);
    expect(authMock.hydrate).toHaveBeenCalledOnce();
    expect(routerMock.createUrlTree).not.toHaveBeenCalled();
  });

  it.each(['kiosk', 'systemAdmin'] as UserRole[])(
    'redirects %s users to schedule',
    async (role) => {
      const { result, urlTree, routerMock } = runGuardForRole(role);

      await expect(firstValueFrom(result as never)).resolves.toBe(urlTree);
      expect(routerMock.createUrlTree).toHaveBeenCalledWith(['/admin/schedule']);
    },
  );
});
