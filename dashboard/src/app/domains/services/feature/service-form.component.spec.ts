import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { MessageService } from 'primeng/api';
import {
  EmployeesClient,
  ServiceCategoriesClient,
  ServicesClient,
  VatRatesClient,
} from '@core/api/api-client';
import { ServiceForm } from './service-form.component';

describe('ServiceForm — cena 0 i hidePrice', () => {
  let fixture: ComponentFixture<ServiceForm>;
  let component: ServiceForm;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ServiceForm],
      providers: [
        { provide: VatRatesClient, useValue: { getVatRates: vi.fn().mockReturnValue(of([])) } },
        { provide: EmployeesClient, useValue: { getEmployees: vi.fn().mockReturnValue(of([])) } },
        {
          provide: ServiceCategoriesClient,
          useValue: { getServiceCategories: vi.fn().mockReturnValue(of([])) },
        },
        {
          provide: ServicesClient,
          useValue: {
            getService: vi.fn().mockReturnValue(of({})),
            getServices: vi.fn().mockReturnValue(of([])),
            createService: vi.fn().mockReturnValue(of('new-id')),
            updateService: vi.fn().mockReturnValue(of({})),
          },
        },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ServiceForm);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('cena = 0 przechodzi walidację (usługa bezpłatna)', () => {
    (component as any).serviceModel.update((m: any) => ({ ...m, name: 'X', amount: 0 }));
    fixture.detectChanges();
    const amountField = (component as any).serviceForm.amount();
    expect(amountField.valid()).toBe(true);
    expect(amountField.errors().length).toBe(0);
  });

  it('cena ujemna jest niepoprawna', () => {
    (component as any).serviceModel.update((m: any) => ({ ...m, name: 'X', amount: -5 }));
    fixture.detectChanges();
    const amountField = (component as any).serviceForm.amount();
    expect(amountField.valid()).toBe(false);
  });

  it('cena null/undefined jest niepoprawna', () => {
    (component as any).serviceModel.update((m: any) => ({ ...m, name: 'X', amount: null }));
    fixture.detectChanges();
    const amountField = (component as any).serviceForm.amount();
    expect(amountField.valid()).toBe(false);
  });

  it('setHidePrice ustawia flagę w modelu', () => {
    expect((component as any).serviceModel().hidePrice).toBe(false);
    (component as any).setHidePrice(true);
    expect((component as any).serviceModel().hidePrice).toBe(true);
  });
});

// Notka o „martwej" kategorii (ghost service). Pełna ścieżka auto-resetu zależy od
// ustabilizowania rxResource kategorii, czego ten harness vitest nie domyka
// deterministycznie (whenStable/makrotask wyzwalają realny klient, microtask nie
// ustawia resource). Pełny przepływ pokrywa build + e2e + weryfikacja w przeglądarce;
// tu testujemy deterministycznie wiring sygnału notki.
describe('ServiceForm — notka o usuniętej kategorii (czyszczenie)', () => {
  let component: any;
  let fixture: ComponentFixture<ServiceForm>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ServiceForm],
      providers: [
        { provide: VatRatesClient, useValue: { getVatRates: vi.fn().mockReturnValue(of([])) } },
        { provide: EmployeesClient, useValue: { getEmployees: vi.fn().mockReturnValue(of([])) } },
        {
          provide: ServiceCategoriesClient,
          useValue: { getServiceCategories: vi.fn().mockReturnValue(of([{ id: 'c1', name: 'A' }])) },
        },
        {
          provide: ServicesClient,
          useValue: {
            getService: vi.fn().mockReturnValue(of({})),
            getServices: vi.fn().mockReturnValue(of([])),
            createService: vi.fn().mockReturnValue(of('new-id')),
            updateService: vi.fn().mockReturnValue(of({})),
          },
        },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ServiceForm);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('domyślnie notka jest ukryta', () => {
    expect(component.categoryResetNotice()).toBe(false);
  });

  it('przełączenie trybu kategorii czyści notkę', () => {
    component.categoryResetNotice.set(true);
    component.setCategorized(false);
    expect(component.categoryResetNotice()).toBe(false);
  });

  it('wybór kategorii z listy czyści notkę', () => {
    component.categoryResetNotice.set(true);
    component.onCategoryChange('c1');
    expect(component.categoryResetNotice()).toBe(false);
  });
});
