import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { MessageService } from 'primeng/api';
import { of } from 'rxjs';
import { signal } from '@angular/core';
import { vi } from 'vitest';
import { EmployeesClient } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { GuidesClient } from '@core/api/api-client';
import { EmployeeAvailabilityDashboardComponent } from './employee-availability-dashboard.component';
import type { UserRole } from '@core/services/NavigationService';

/**
 * Karta „Szablony zmian" prowadzi do route `/admin/resources/shift-templates`,
 * chronionego polityką StaffManagement (Owner/Manager). Sprawdzamy, że:
 *  - w widoku admina karta jest zawsze,
 *  - w „Mojej dostępności" (self-mode) karta jest tylko dla Owner/Manager,
 *  - Employee w self-mode jej nie widzi (uniknięcie prowadzącego donikąd linku).
 */
describe('EmployeeAvailabilityDashboardComponent — widoczność karty „Szablony zmian"', () => {
  let fixture: ComponentFixture<EmployeeAvailabilityDashboardComponent>;
  let component: EmployeeAvailabilityDashboardComponent;
  const role = signal<UserRole>('owner');

  async function setup(url: string, currentRole: UserRole) {
    role.set(currentRole);

    await TestBed.configureTestingModule({
      imports: [EmployeeAvailabilityDashboardComponent],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        {
          provide: EmployeesClient,
          useValue: {
            getEmployee: vi
              .fn()
              .mockReturnValue(of({ id: 'e1', firstName: 'Jan', lastName: 'Kowalski', email: 'jan@test.pl' })),
            getEmployeeSchedules: vi.fn().mockReturnValue(of([])),
            setEmployeeSchedule: vi.fn().mockReturnValue(of({})),
          },
        },
        { provide: MessageService, useValue: { add: vi.fn() } },
        {
          provide: AuthSessionService,
          useValue: { currentRole: role.asReadonly() },
        },
        {
          // Hub osadza `app-guide-launcher`, który przez GuideService sięga po postęp
          // przewodników. Stub wystarczy — ten test dotyczy widoczności kafelka szablonów.
          provide: GuidesClient,
          useValue: {
            getCompletions: vi.fn().mockReturnValue(of([])),
            markCompleted: vi.fn().mockReturnValue(of(null)),
            resetCompletion: vi.fn().mockReturnValue(of(null)),
          },
        },
      ],
    }).compileComponents();

    const router = TestBed.inject(Router);
    vi.spyOn(router, 'url', 'get').mockReturnValue(url);

    fixture = TestBed.createComponent(EmployeeAvailabilityDashboardComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('id', 'e1');
    fixture.detectChanges();
    await fixture.whenStable();
  }

  afterEach(() => TestBed.resetTestingModule());

  function templatesCardLink(): HTMLAnchorElement | null {
    return fixture.nativeElement.querySelector('a[href="/admin/resources/shift-templates"]');
  }

  it('widok admina (Owner): karta szablonów jest widoczna', async () => {
    await setup('/admin/resources/employees/e1', 'owner');
    expect(component.isSelfMode()).toBe(false);
    expect(component.showShiftTemplatesCard()).toBe(true);
    expect(templatesCardLink()).not.toBeNull();
  });

  it('widok admina (Employee): karta szablonów też jest (route i tak chroni guard)', async () => {
    await setup('/admin/resources/employees/e1', 'employee');
    expect(component.showShiftTemplatesCard()).toBe(true);
    expect(templatesCardLink()).not.toBeNull();
  });

  it('self-mode + Owner: karta szablonów widoczna', async () => {
    await setup('/admin/my-availability/e1', 'owner');
    expect(component.isSelfMode()).toBe(true);
    expect(component.canManageShiftTemplates()).toBe(true);
    expect(component.showShiftTemplatesCard()).toBe(true);
    expect(templatesCardLink()).not.toBeNull();
  });

  it('self-mode + Manager: karta szablonów widoczna', async () => {
    await setup('/admin/my-availability/e1', 'manager');
    expect(component.showShiftTemplatesCard()).toBe(true);
    expect(templatesCardLink()).not.toBeNull();
  });

  it('self-mode + Employee: karta szablonów ukryta (link prowadziłby donikąd)', async () => {
    await setup('/admin/my-availability/e1', 'employee');
    expect(component.isSelfMode()).toBe(true);
    expect(component.canManageShiftTemplates()).toBe(false);
    expect(component.showShiftTemplatesCard()).toBe(false);
    expect(templatesCardLink()).toBeNull();
  });
});
