import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EmployeeListComponent } from './employee-list.component';
import { AuthClient, EmployeesClient, EmployeeDto } from '@core/api/api-client';
import { Router } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { of } from 'rxjs';
import { By } from '@angular/platform-browser';
import { describe, it, expect, beforeEach, vi } from 'vitest';

describe('EmployeeListComponent', () => {
  let component: EmployeeListComponent;
  let fixture: ComponentFixture<EmployeeListComponent>;

  let employeesClientMock: any;
  let routerMock: any;
  let confirmationServiceMock: any;

  const mockEmployees: EmployeeDto[] = [
    { id: '1', firstName: 'Jan', lastName: 'Kowalski', email: 'jan@barber.com' },
    { id: '2', firstName: 'Adam', lastName: 'Nowak', email: 'adam@barber.com' }
  ];

  beforeEach(async () => {
    employeesClientMock = {
      getEmployees: vi.fn().mockReturnValue(of(mockEmployees)),
      deleteEmployee: vi.fn().mockReturnValue(of({}))
    };

    routerMock = {
      navigate: vi.fn(),
      url: '/admin/team',
    };

    confirmationServiceMock = {
      confirm: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [EmployeeListComponent],
      providers: [
        { provide: EmployeesClient, useValue: employeesClientMock },
        { provide: Router, useValue: routerMock },
        { provide: ConfirmationService, useValue: confirmationServiceMock },
        // Renderowane karty (app-employee-card) wstrzykują AuthClient + MessageService (przycisk „Wyślij ponownie").
        { provide: AuthClient, useValue: { resendEmployeeInvite: vi.fn().mockReturnValue(of({})) } },
        { provide: MessageService, useValue: { add: vi.fn() } },
        // Owner → canManage()=true, więc „DODAJ SPECJALISTĘ" + akcje kart się renderują.
        { provide: AuthSessionService, useValue: { currentRole: () => 'owner' } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(EmployeeListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create the component', () => {
    expect(component).toBeTruthy();
  });

  describe('Data Display', () => {
    it('should render the correct number of employee cards', async () => {
      // Czekamy aż rxResource skończy pracę
      await fixture.whenStable();
      fixture.detectChanges();

      const cards = fixture.debugElement.queryAll(By.css('[data-testid="employee-card-item"]'));
      expect(cards.length).toBe(2);
    });

    it('should display the correct count in the "All" badge', async () => {
      await fixture.whenStable();
      fixture.detectChanges();

      const countLabel = fixture.debugElement.query(By.css('[data-testid="employees-count"]'));
      expect(countLabel.nativeElement.textContent.trim()).toBe('2');
    });

    it('should display the empty state when no employees are returned', async () => {
      // Symulujemy brak danych
      employeesClientMock.getEmployees.mockReturnValue(of([]));
      component.employees.reload();

      // Kluczowe: czekamy na stabilizację po reloadzie
      await fixture.whenStable();
      fixture.detectChanges();

      const emptyState = fixture.debugElement.query(By.css('[data-testid="empty-state"]'));
      expect(emptyState).toBeTruthy();
    });
  });

  describe('Navigation', () => {
    it('should navigate to the new employee form when "ADD SPECIALIST" is clicked', () => {
      const addBtn = fixture.debugElement.query(By.css('[data-testid="add-specialist-btn"]'));

      addBtn.triggerEventHandler('onClick', null);

      expect(routerMock.navigate).toHaveBeenCalledWith(['admin/resources/employees/new'], {
        queryParams: { returnUrl: '/admin/team' },
      });
    });

    it('should navigate to the edit form when receiving the edit event from a card', () => {
      component.handleEditEmployee('123');

      expect(routerMock.navigate).toHaveBeenCalledWith(['admin/resources/employees/edit/123'], {
        queryParams: { returnUrl: '/admin/team' },
      });
    });
  });

  describe('Employee Deletion', () => {
    it('should open a confirmation dialog when attempting to delete an employee', () => {
      component.handleDeleteEmployee('1');

      expect(confirmationServiceMock.confirm).toHaveBeenCalled();
      const confirmArgs = confirmationServiceMock.confirm.mock.calls[0][0];
      expect(confirmArgs.header).toBe('Potwierdzenie usunięcia pracownika');
    });

    it('should call the delete service and reload the list upon confirmation', () => {
      const reloadSpy = vi.spyOn(component.employees, 'reload');

      component.handleDeleteEmployee('1');

      const confirmArgs = confirmationServiceMock.confirm.mock.calls[0][0];
      confirmArgs.accept();

      expect(employeesClientMock.deleteEmployee).toHaveBeenCalledWith('1');
      expect(reloadSpy).toHaveBeenCalled();
    });
  });
});