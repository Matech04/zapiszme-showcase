import { describe, it, expect, beforeEach } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CustomerCardComponent } from './customer-card.component';
import { CustomerDto } from '@core/api/api-client';

/**
 * Menu karty klienta miesza DWA uprawnienia backendu:
 *  - `canManage`          → usunięcie klienta = `BusinessManagement` (tylko właścicielka),
 *  - `canManageWhitelist` → whitelist        = `StaffManagement` (właścicielka + manager).
 * Wcześniej jedno `canManage` gatowało obie akcje: manager nie widział whitelisty, a pracownik
 * po zmianie polityki API zobaczyłby ją i dostał 403.
 */
describe('CustomerCardComponent', () => {
  let component: CustomerCardComponent;
  let fixture: ComponentFixture<CustomerCardComponent>;

  const mockCustomer: CustomerDto = {
    id: 'c-1',
    firstName: 'Anna',
    lastName: 'Klient',
    email: 'anna@example.com',
    isWhitelisted: false,
  } as CustomerDto;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CustomerCardComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomerCardComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('customer', mockCustomer);
    fixture.detectChanges();
  });

  const labels = () => component.menuItems().map((i) => i.label);

  const setPerms = (canManage: boolean, canManageWhitelist: boolean) => {
    fixture.componentRef.setInput('canManage', canManage);
    fixture.componentRef.setInput('canManageWhitelist', canManageWhitelist);
    fixture.detectChanges();
  };

  it('właścicielka: whitelist + Usuń', () => {
    setPerms(true, true);
    expect(labels()).toEqual(['Profil', 'Edytuj', 'Dodaj do whitelisty', 'Usuń']);
  });

  it('manager: whitelist, ale bez „Usuń"', () => {
    setPerms(false, true);
    expect(labels()).toEqual(['Profil', 'Edytuj', 'Dodaj do whitelisty']);
    expect(labels()).not.toContain('Usuń');
  });

  it('pracownik: ani whitelisty, ani „Usuń" — zostają Profil + Edytuj', () => {
    setPerms(false, false);
    expect(labels()).toEqual(['Profil', 'Edytuj']);
  });

  it('whitelist jest fail-closed domyślnie — konsument musi ją włączyć świadomie', () => {
    // Bez `setInput` na uprawnieniach: same wartości domyślne wejść.
    expect(labels()).not.toContain('Dodaj do whitelisty');
  });

  it('klient na whiteliście → opcja „Usuń z whitelisty"', () => {
    fixture.componentRef.setInput('customer', { ...mockCustomer, isWhitelisted: true });
    setPerms(true, true);
    expect(labels()).toContain('Usuń z whitelisty');
  });
});
