import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MessageService } from 'primeng/api';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { GuidesClient } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { UserRole } from '@core/models/navigation.model';
import { GuideService } from './guide.service';
import { GuideDef } from './guide.types';

/**
 * Testy BRAM przewodnika — warunków sprawdzanych, zanim driver.js w ogóle wystartuje.
 *
 * Mechanika kroków zadaniowych (nasłuchy `click`/`appear`/`disappear`) celowo nie jest tu
 * pokrywana: opiera się na realnych wymiarach elementów, a jsdom zwraca z
 * `getBoundingClientRect` same zera, więc każdy element byłby „niewidoczny" i test
 * sprawdzałby atrapę zamiast zachowania. Tę warstwę weryfikujemy w przeglądarce, a przed
 * gnięciem selektorów chroni `guides.registry.spec.ts`.
 */
describe('GuideService — bramy uruchomienia', () => {
  const role = signal<UserRole | null>('owner');
  const employeeId = signal<string | null>('emp-1');
  let messages: { add: ReturnType<typeof vi.fn> };

  const ownerOnlyGuide: GuideDef = {
    id: 'owner-only',
    title: 'Tylko dla właściciela',
    summary: '',
    category: 'money',
    roles: ['owner'],
    icon: 'pi pi-wallet',
    entryRoute: '/admin/settings/deposits',
    steps: [{ kind: 'explain', popover: { title: 't', description: 'd' } }],
  };

  const selfServiceGuide: GuideDef = {
    id: 'needs-employee',
    title: 'Wymaga profilu pracownika',
    summary: '',
    category: 'availability',
    roles: ['owner', 'employee'],
    icon: 'pi pi-clock',
    entryRoute: '/admin/my-availability/:me',
    steps: [
      { kind: 'explain', route: '/admin/my-availability/:me', popover: { title: 't', description: 'd' } },
    ],
  };

  function setup(): GuideService {
    TestBed.configureTestingModule({
      providers: [
        GuideService,
        provideRouter([{ path: '**', children: [] }]),
        { provide: MessageService, useValue: messages },
        {
          provide: AuthSessionService,
          useValue: { currentRole: role.asReadonly(), currentEmployeeId: employeeId.asReadonly() },
        },
        {
          provide: GuidesClient,
          useValue: {
            getCompletions: vi.fn().mockReturnValue(of([])),
            markCompleted: vi.fn().mockReturnValue(of(null)),
            resetCompletion: vi.fn().mockReturnValue(of(null)),
          },
        },
      ],
    });
    return TestBed.inject(GuideService);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    role.set('owner');
    employeeId.set('emp-1');
    messages = { add: vi.fn() };
  });

  it('nie startuje przewodnika spoza ról zalogowanego użytkownika', async () => {
    role.set('employee');
    const service = setup();

    await service.start(ownerOnlyGuide);

    expect(service.running()).toBe(false);
    expect(messages.add).toHaveBeenCalled();
  });

  // Świadomie NIE testujemy tu ścieżki „przewodnik faktycznie rusza": `start()` ładuje
  // driver.js i wywołuje `drive()`, a ten w jsdom wywraca się na braku layoutu. Błąd jest
  // asynchroniczny, więc wyciekał do losowych innych plików testowych i wywracał je
  // (zaobserwowane: trzy fałszywe porażki w niepowiązanych specach). Uruchamianie
  // przewodników weryfikujemy w przeglądarce, a kotwic pilnuje `guides.registry.spec.ts`.

  it('odmawia, gdy trasa wymaga profilu pracownika, a konto go nie ma', async () => {
    employeeId.set(null);
    const service = setup();

    await service.start(selfServiceGuide);

    expect(service.running()).toBe(false);
    expect(messages.add).toHaveBeenCalled();
  });

  it('podmienia token :me na id pracownika', () => {
    employeeId.set('emp-42');
    const service = setup();

    expect(service.resolveRoute('/admin/my-availability/:me/schedules'))
      .toBe('/admin/my-availability/emp-42/schedules');
  });

  it('nie startuje, gdy rola sesji jest jeszcze nieznana', async () => {
    role.set(null);
    const service = setup();

    await service.start(ownerOnlyGuide);

    expect(service.running()).toBe(false);
  });
});
