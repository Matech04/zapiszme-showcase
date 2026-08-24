import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { UserRole } from '@core/services/NavigationService';
import { teamViewGuard } from './team-view.guard';
import { AuthSessionService } from './auth-session.service';
import { SalonSettingsClient, StaffCalendarVisibilityPolicy } from '@core/api/api-client';
import { firstValueFrom, of, throwError } from 'rxjs';

describe('teamViewGuard', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  const runGuard = (
    role: UserRole,
    policy?: StaffCalendarVisibilityPolicy,
    settingsError = false,
  ) => {
    const urlTree = { redirectedTo: '/admin/schedule' };
    const authMock = {
      hydrate: vi.fn().mockReturnValue(of({ kind: 'authenticated', session: { userId: 'user-id' } })),
      currentRole: vi.fn().mockReturnValue(role),
    };
    const settingsMock = {
      get: vi
        .fn()
        .mockReturnValue(
          settingsError
            ? throwError(() => new Error('boom'))
            : of({ staffCalendarVisibilityPolicy: policy }),
        ),
    };
    const routerMock = { createUrlTree: vi.fn().mockReturnValue(urlTree) };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthSessionService, useValue: authMock },
        { provide: SalonSettingsClient, useValue: settingsMock },
        { provide: Router, useValue: routerMock },
      ],
    });

    const result = TestBed.runInInjectionContext(() =>
      teamViewGuard({} as never, {} as never),
    );

    return { result, urlTree, settingsMock, routerMock };
  };

  it.each(['owner', 'manager'] as UserRole[])(
    'allows %s without fetching salon settings',
    async (role) => {
      const { result, settingsMock, routerMock } = runGuard(role);

      await expect(firstValueFrom(result as never)).resolves.toBe(true);
      expect(settingsMock.get).not.toHaveBeenCalled();
      expect(routerMock.createUrlTree).not.toHaveBeenCalled();
    },
  );

  it.each([
    StaffCalendarVisibilityPolicy.TeamReadOnly,
    StaffCalendarVisibilityPolicy.TeamFull,
  ])('allows employee when policy = %s', async (policy) => {
    const { result, settingsMock } = runGuard('employee', policy);

    await expect(firstValueFrom(result as never)).resolves.toBe(true);
    expect(settingsMock.get).toHaveBeenCalledOnce();
  });

  it('redirects employee under OwnCalendarOnly', async () => {
    const { result, urlTree, routerMock } = runGuard(
      'employee',
      StaffCalendarVisibilityPolicy.OwnCalendarOnly,
    );

    await expect(firstValueFrom(result as never)).resolves.toBe(urlTree);
    expect(routerMock.createUrlTree).toHaveBeenCalledWith(['/admin/schedule']);
  });

  it('redirects employee when salon settings fetch fails', async () => {
    const { result, urlTree } = runGuard('employee', undefined, true);

    await expect(firstValueFrom(result as never)).resolves.toBe(urlTree);
  });

  it.each(['kiosk', 'systemAdmin'] as UserRole[])('redirects %s users', async (role) => {
    const { result, urlTree, settingsMock } = runGuard(role);

    await expect(firstValueFrom(result as never)).resolves.toBe(urlTree);
    expect(settingsMock.get).not.toHaveBeenCalled();
  });
});
