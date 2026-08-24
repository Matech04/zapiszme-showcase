import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EmployeeForm } from './employee-form.component';
import { AuthClient, EmployeesClient } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { Router } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { of, throwError } from 'rxjs';
import { By } from '@angular/platform-browser';
import { describe, it, expect, beforeEach, vi } from 'vitest';

describe('EmployeeForm', () => {
  let component: EmployeeForm;
  let fixture: ComponentFixture<EmployeeForm>;

  // Mocks
  let employeesClientMock: any;
  let authClientMock: any;
  let routerMock: any;
  let messageServiceMock: any;

  const mockEmployee = {
    id: '123',
    firstName: 'Jan',
    lastName: 'Kowalski',
    email: 'jan@example.com'
  };

  beforeEach(async () => {
    employeesClientMock = {
      updateEmployee: vi.fn().mockReturnValue(of({})),
      getEmployee: vi.fn().mockReturnValue(of(mockEmployee)),
      deleteEmployee: vi.fn().mockReturnValue(of({})),
    };
    authClientMock = {
      inviteEmployee: vi.fn().mockReturnValue(of({})),
    };

    routerMock = {
      navigate: vi.fn()
    };

    messageServiceMock = {
      add: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [EmployeeForm],
      providers: [
        { provide: EmployeesClient, useValue: employeesClientMock },
        { provide: AuthClient, useValue: authClientMock },
        // Realny AuthSessionService wciąga DemoClient/NavigationService/CsrfTokenService (NG0201).
        // Komponent używa tylko currentRole() — do rozstrzygnięcia isOwner().
        { provide: AuthSessionService, useValue: { currentRole: () => 'owner' } },
        { provide: Router, useValue: routerMock },
        { provide: MessageService, useValue: messageServiceMock },
        // <p-confirmDialog> w template (kompozycja form-layout): runtime ma go z app.config.ts,
        // testowy TestBed musi dostarczyć osobno.
        ConfirmationService,
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(EmployeeForm);
    component = fixture.componentInstance;
  });

  it('should create the component', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  describe('Data Loading (via effect in createFormActions)', () => {
    it('should load employee data when id input changes', async () => {
      // Ustawienie inputu wyzwala effect w createFormActions
      fixture.componentRef.setInput('id', '123');
      fixture.detectChanges();

      // Czekamy na wykonanie effect i subskrypcję adaptera
      await fixture.whenStable();
      fixture.detectChanges();

      expect(employeesClientMock.getEmployee).toHaveBeenCalledWith('123');
      expect(component.employeeModel().firstName).toBe('Jan');
    });
  });

  describe('Form Actions', () => {
    it('should call inviteEmployee and show success message on valid submit', async () => {
      fixture.detectChanges();

      // Wypełniamy model poprawnymi danymi
      component.employeeModel.set({
        firstName: 'Adam',
        lastName: 'Nowak',
        email: 'adam@nowak.pl',
        displayName: 'Adam Nowak',
        role: 'Employee',
        specialization: '',
      });
      fixture.detectChanges();

      // Wywołujemy handleSubmit bezpośrednio lub przez przycisk
      await component['FormActions'].handleSubmit();

      expect(authClientMock.inviteEmployee).toHaveBeenCalledWith({
        email: 'adam@nowak.pl',
        displayName: 'Adam Nowak',
        firstName: 'Adam',
        lastName: 'Nowak',
        role: 'Employee',
      });
      // Komponent generuje dynamiczny komunikat (z e-mailem zaproszonego), porównujemy
      // tylko severity + fragmentem ("zaproszenie") aby nie powiązać testu z brzmieniem.
      expect(messageServiceMock.add).toHaveBeenCalledWith(expect.objectContaining({
        severity: 'success',
        detail: expect.stringContaining('zaproszenie'),
      }));
      expect(routerMock.navigate).toHaveBeenCalledWith(['/admin/team']);
    });

    it('should show error message when API call fails', async () => {
      fixture.detectChanges();
      authClientMock.inviteEmployee.mockReturnValue(throwError(() => new Error('API Error')));

      component.employeeModel.set({
        firstName: 'Adam',
        lastName: 'Nowak',
        email: 'adam@nowak.pl',
        displayName: 'Adam Nowak',
        role: 'Employee',
        specialization: '',
      });
      fixture.detectChanges();

      try {
        await component['FormActions'].handleSubmit();
      } catch (e) {
        // Spodziewamy się błędu, który jest rzucany dalej w createFormActions
      }

      expect(messageServiceMock.add).toHaveBeenCalledWith(expect.objectContaining({
        severity: 'error',
        summary: 'Błąd'
      }));
    });

    it('should navigate back on cancel without toast', () => {
      fixture.detectChanges();

      component['FormActions'].handleCancel();

      expect(messageServiceMock.add).not.toHaveBeenCalled();
      expect(routerMock.navigate).toHaveBeenCalledWith(['/admin/team']);
    });
  });

  describe('Validation Integration', () => {
    it('should not call API if the form is invalid', async () => {
      fixture.detectChanges();

      // Zostawiamy puste pola (invalid state)
      await component['FormActions'].handleSubmit();

      expect(authClientMock.inviteEmployee).not.toHaveBeenCalled();
    });
  });
});