import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { firstValueFrom, of } from 'rxjs';
import { AuthSessionService } from './auth-session.service';
import { OnboardingStateService } from './onboarding-state.service';
import { nextStepToRoute, setupGuard } from './setup.guard';
import { ONBOARDING_STEPS } from '@features/onboarding/onboarding-steps';

/**
 * Guard na `/setup/**`. Kluczowy wyjątek: krok „Gotowe" pokazuje się dopiero PO oznaczeniu
 * onboardingu jako ukończonego, więc odsyłanie ukończonych do panelu zabierało właścicielce
 * ekran z jej publicznym linkiem, gdy tylko odświeżyła stronę.
 */
describe('setupGuard', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  const routeFor = (childPath: string | null): ActivatedRouteSnapshot =>
    ({ firstChild: childPath ? { routeConfig: { path: childPath } } : null }) as ActivatedRouteSnapshot;

  const run = (opts: { session: unknown; completed: boolean; child: string | null }) => {
    const adminTree = { redirectedTo: '/admin/schedule' };
    const loginTree = { redirectedTo: '/login' };
    const router = {
      createUrlTree: vi.fn((commands: string[]) =>
        commands[0] === '/login' ? loginTree : adminTree,
      ),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthSessionService, useValue: { hydrate: () => of(opts.session) } },
        {
          provide: OnboardingStateService,
          useValue: { ensure: () => of({ onboardingCompleted: opts.completed }) },
        },
        { provide: Router, useValue: router },
      ],
    });

    const result = TestBed.runInInjectionContext(() =>
      setupGuard(routeFor(opts.child), {} as never),
    );
    return { result, adminTree, loginTree };
  };

  it('bez sesji odsyła do logowania', async () => {
    const { result, loginTree } = run({ session: null, completed: false, child: 'profile' });
    await expect(firstValueFrom(result as never)).resolves.toBe(loginTree);
  });

  it('nieukończony onboarding przechodzi', async () => {
    const { result } = run({ session: { userId: 'u1' }, completed: false, child: 'profile' });
    await expect(firstValueFrom(result as never)).resolves.toBe(true);
  });

  it('ukończony onboarding jest odsyłany do panelu', async () => {
    const { result, adminTree } = run({ session: { userId: 'u1' }, completed: true, child: 'profile' });
    await expect(firstValueFrom(result as never)).resolves.toBe(adminTree);
  });

  it('„Gotowe" przechodzi mimo ukończonego onboardingu — inaczej F5 zabiera link salonu', async () => {
    const { result } = run({ session: { userId: 'u1' }, completed: true, child: 'done' });
    await expect(firstValueFrom(result as never)).resolves.toBe(true);
  });
});

/**
 * Mapa `nextStep` z backendu na krok kreatora. Backend nie widzi kroku „Zapisy" (tryb potwierdzania
 * ma wartość domyślną, więc nie da się odróżnić wyboru od jego braku), dlatego niedomknięty grafik
 * cofa na zapisy — pierwszy z trójki zapisy → terminy → godziny.
 */
describe('nextStepToRoute', () => {
  it('wznawia od zapisów, gdy grafik nie jest domknięty', () => {
    expect(nextStepToRoute('Rules')).toBe('/setup/rules');
  });

  it('„Schedule" znaczy wąsko: grafik zapisany, kreator niedomknięty', () => {
    // Nie /setup/slot-mode: ten stan powstaje, gdy `complete()` padło na ostatnim przycisku,
    // więc właścicielce brakuje wyłącznie domknięcia — nie całej trójki od nowa.
    expect(nextStepToRoute('Schedule')).toBe('/setup/schedule');
  });

  it('każda trasa wskazuje na istniejący krok kreatora', () => {
    const paths = ONBOARDING_STEPS.map((s) => `/setup/${s.path}`);
    for (const step of ['Profile', 'Industry', 'Rules', 'Schedule']) {
      expect(paths).toContain(nextStepToRoute(step));
    }
  });

  it('ukończony onboarding kieruje do panelu, nieznany krok na początek kreatora', () => {
    expect(nextStepToRoute('Completed')).toBe('/admin/schedule');
    expect(nextStepToRoute('CośNowego')).toBe('/setup/profile');
  });

  it('konto po dezaktywacji NIE trafia do kreatora zakładania salonu', () => {
    // Zwolniona pracownica ma konto bez aktywnego rekordu Employee, więc onboardingGuard odbija
    // ją na /setup. Bez własnej trasy lądowała na /setup/profile — czyli dostawała propozycję
    // założenia własnego salonu, której i tak nie może zrealizować (mutacje = BusinessManagement).
    expect(nextStepToRoute('InactiveAccount')).toBe('/setup/konto-nieaktywne');
    expect(nextStepToRoute('InactiveAccount')).not.toBe('/setup/profile');
  });
});
