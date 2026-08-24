import { describe, it, expect, beforeEach, vi } from "vitest";
import { of } from "rxjs";
import { EmployeeCardComponent } from "./employee-card.component";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { AuthClient, EmployeeDto } from "@core/api/api-client";
import { MessageService } from "primeng/api";
import { By } from "@angular/platform-browser";

describe('EmployeeCardComponent', () => {
  let component: EmployeeCardComponent;
  let fixture: ComponentFixture<EmployeeCardComponent>;
  let authClient: { resendEmployeeInvite: ReturnType<typeof vi.fn> };
  let messageService: { add: ReturnType<typeof vi.fn> };

  const mockEmployee: EmployeeDto = {
    id: '1234567890abcdef',
    firstName: 'Jan',
    lastName: 'Kowalski',
    email: 'jan.kowalski@example.com'
  };

  beforeEach(async () => {
    authClient = { resendEmployeeInvite: vi.fn(() => of({} as any)) };
    messageService = { add: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [EmployeeCardComponent],
      providers: [
        { provide: AuthClient, useValue: authClient },
        { provide: MessageService, useValue: messageService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EmployeeCardComponent);
    component = fixture.componentInstance;

    fixture.componentRef.setInput('employee', mockEmployee);

    fixture.detectChanges();
  })

  it("should initiate component", () => {
    expect(component).toBeTruthy();
  })

  it("should display name and last name of employee", () => {
    const nameElement = fixture.debugElement.query(By.css('[data-testid="employee-name"]'));

    expect(nameElement).toBeTruthy();
    expect(nameElement.nativeElement.textContent.trim()).toBe('Jan Kowalski');
  })

  it("should display info when there is no email", () => {
    const employeeWithoutEmail: EmployeeDto = {
      ...mockEmployee,
      email: undefined
    };

    fixture.componentRef.setInput('employee', employeeWithoutEmail);
    fixture.detectChanges();

    const emailElement = fixture.debugElement.query(By.css('[data-testid="employee-email"]'));
    expect(emailElement.nativeElement.textContent.trim()).toBe('Brak adresu email');
  })

  it("should emit manage", () => {
    const manageSpy = vi.spyOn(component.manage, 'emit');
    const manageBtn = fixture.debugElement.query(By.css('[data-testid="button-manage"]'));
    expect(manageBtn).toBeTruthy();
    manageBtn.triggerEventHandler('click', new MouseEvent('click'));

    expect(manageSpy).toHaveBeenCalledTimes(1);
    expect(manageSpy).toHaveBeenCalledWith('1234567890abcdef');
  });

  it("should emit edit", () => {
    const editSpy = vi.spyOn(component.edit, 'emit');

    // Pobieramy wygenerowaną opcję menu z Twojego sygnału computed()
    const editMenuItem = component.menuItems().find(item => item.label === 'Edytuj');

    // Uruchamiamy komendę przypisaną do tego przycisku (symulacja kliknięcia w menu)
    if (editMenuItem && editMenuItem.command) {
      editMenuItem.command({} as any);
    }

    // Sprawdzamy czy wyemitowało się samo ID pracownika
    expect(editSpy).toHaveBeenCalledWith('1234567890abcdef');
  });

  it("should emit delete", () => {
    const deleteSpy = vi.spyOn(component.delete, 'emit');

    const deleteMenuItem = component.menuItems().find(item => item.label === 'Usuń');

    if (deleteMenuItem && deleteMenuItem.command) {
      deleteMenuItem.command({} as any);
    }

    expect(deleteSpy).toHaveBeenCalledWith('1234567890abcdef');
  });

  it("nie pokazuje przycisku ponownego zaproszenia dla aktywnego konta", () => {
    fixture.componentRef.setInput('employee', { ...mockEmployee, invitePending: false });
    fixture.detectChanges();

    const resendBtn = fixture.debugElement.query(By.css('[data-testid="button-resend-invite"]'));
    expect(resendBtn).toBeNull();
  });

  it("pokazuje przycisk ponownego zaproszenia dla niepotwierdzonego konta", () => {
    fixture.componentRef.setInput('employee', { ...mockEmployee, invitePending: true });
    fixture.detectChanges();

    const resendBtn = fixture.debugElement.query(By.css('[data-testid="button-resend-invite"]'));
    expect(resendBtn).toBeTruthy();
  });

  it("ukrywa przycisk ponownego zaproszenia w trybie tylko-do-odczytu (pracownik)", () => {
    fixture.componentRef.setInput('employee', { ...mockEmployee, invitePending: true });
    fixture.componentRef.setInput('canManage', false);
    fixture.detectChanges();

    const resendBtn = fixture.debugElement.query(By.css('[data-testid="button-resend-invite"]'));
    expect(resendBtn).toBeNull();
  });

  it("resendInvite wywołuje klienta i pokazuje toast sukcesu", () => {
    fixture.componentRef.setInput('employee', { ...mockEmployee, invitePending: true });
    fixture.detectChanges();

    component.resendInvite();

    expect(authClient.resendEmployeeInvite).toHaveBeenCalledWith('1234567890abcdef');
    expect(messageService.add).toHaveBeenCalledWith(
      expect.objectContaining({ severity: 'success' }),
    );
  });

})



