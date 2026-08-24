import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfirmationService, MessageService } from 'primeng/api';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ServiceCategoryDto, ServicesClient } from '@core/api/api-client';
import { ServicesList } from './services-list.component';

describe('ServicesList — reorder usług w kategorii', () => {
  let fixture: ComponentFixture<ServicesList>;
  let component: ServicesList;
  let servicesClient: { getServices: any; reorderServices: any; deleteService: any };

  beforeEach(async () => {
    servicesClient = {
      getServices: vi.fn().mockReturnValue(
        of([
          { id: 's1', name: 'S1', price: { amount: 10, currency: 'PLN' } },
          { id: 's2', name: 'S2', price: { amount: 20, currency: 'PLN' } },
          { id: 's3', name: 'S3', price: { amount: 30, currency: 'PLN' } },
        ]),
      ),
      reorderServices: vi.fn().mockReturnValue(of({})),
      deleteService: vi.fn().mockReturnValue(of({})),
    };

    await TestBed.configureTestingModule({
      imports: [ServicesList],
      providers: [
        { provide: ServicesClient, useValue: servicesClient },
        { provide: ConfirmationService, useValue: { confirm: vi.fn() } },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(ServicesList);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('category', { id: 'cat-1', name: 'Kat' } as ServiceCategoryDto);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('onServiceReorder (cdkDropListDropped) woła reorderServices z categoryId kategorii i nową kolejnością', () => {
    component.onServiceReorder({ previousIndex: 0, currentIndex: 2 } as any);
    expect(servicesClient.reorderServices).toHaveBeenCalledWith({
      orderedServiceIds: ['s2', 's3', 's1'],
      categoryId: 'cat-1',
    });
    expect((component as any).orderedServices().map((s: any) => s.id)).toEqual(['s2', 's3', 's1']);
  });

  it('drop bez zmiany pozycji (previousIndex === currentIndex) nie woła API', () => {
    component.onServiceReorder({ previousIndex: 1, currentIndex: 1 } as any);
    expect(servicesClient.reorderServices).not.toHaveBeenCalled();
  });

  it('rewertuje lokalną kolejność gdy reorder padnie', async () => {
    servicesClient.reorderServices.mockReturnValue(throwError(() => new Error('boom')));
    const before = (component as any).orderedServices().map((s: any) => s.id);
    component.onServiceReorder({ previousIndex: 0, currentIndex: 2 } as any);
    await Promise.resolve();
    await Promise.resolve();
    expect((component as any).orderedServices().map((s: any) => s.id)).toEqual(before);
  });

  it('zmiana refreshTick przeładowuje listę usług (getServices wołane ponownie)', async () => {
    // Inicjalne ładowanie (1x) już zaszło w beforeEach.
    expect(servicesClient.getServices).toHaveBeenCalledTimes(1);

    // Zmieniamy nazwę pierwszej usługi w danych zwracanych przez API i bumpujemy tick.
    servicesClient.getServices.mockReturnValue(
      of([
        { id: 's1', name: 'S1-RENAMED', price: { amount: 10, currency: 'PLN' } },
        { id: 's2', name: 'S2', price: { amount: 20, currency: 'PLN' } },
        { id: 's3', name: 'S3', price: { amount: 30, currency: 'PLN' } },
      ]),
    );
    fixture.componentRef.setInput('refreshTick', 1);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(servicesClient.getServices).toHaveBeenCalledTimes(2);
    const names = (component as any).orderedServices().map((s: any) => s.name);
    expect(names).toContain('S1-RENAMED');
  });

  it('pierwsze ustawienie refreshTick (wartość początkowa) NIE dubluje ładowania', () => {
    // Tylko inicjalny load z beforeEach — brak dodatkowego getServices przy montażu.
    expect(servicesClient.getServices).toHaveBeenCalledTimes(1);
  });
});
