import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { of, Subject, throwError } from 'rxjs';
import { vi } from 'vitest';
import {
  EmployeesClient,
  MediaClient,
  ServiceCategoriesClient,
  ServicesClient,
  VatRatesClient,
} from '@core/api/api-client';
import { ServiceCatalogComponent } from './service-catalog.component';
import { ServiceForm } from './service-form.component';

const UNCATEGORIZED_DEFAULT_ORDER = 1_000_000;

describe('ServiceCatalogComponent — scalona lista sekcji (kategorie + „Bez kategorii")', () => {
  let fixture: ComponentFixture<ServiceCatalogComponent>;
  let component: ServiceCatalogComponent;
  let categoriesClient: {
    getServiceCategories: any;
    reorderServiceCategories: any;
    reorderCatalogSections: any;
    getUncategorizedOrder: any;
    deleteServiceCategory: any;
  };
  let servicesClient: { getServices: any; reorderServices: any; deleteService: any };

  beforeEach(async () => {
    categoriesClient = {
      getServiceCategories: vi.fn().mockReturnValue(
        of([
          { id: 'c1', name: 'Cat 1', orderIndex: 0 },
          { id: 'c2', name: 'Cat 2', orderIndex: 1 },
          { id: 'c3', name: 'Cat 3', orderIndex: 2 },
        ]),
      ),
      reorderServiceCategories: vi.fn().mockReturnValue(of({})),
      reorderCatalogSections: vi.fn().mockReturnValue(of({})),
      // domyślnie sekcja „Bez kategorii" na końcu
      getUncategorizedOrder: vi.fn().mockReturnValue(of({ orderIndex: UNCATEGORIZED_DEFAULT_ORDER })),
      deleteServiceCategory: vi.fn().mockReturnValue(of({})),
    };
    servicesClient = {
      getServices: vi.fn().mockReturnValue(
        of([
          { id: 's1', name: 'S1', price: { amount: 10, currency: 'PLN' } },
          { id: 's2', name: 'S2', price: { amount: 20, currency: 'PLN' } },
        ]),
      ),
      reorderServices: vi.fn().mockReturnValue(of({})),
      deleteService: vi.fn().mockReturnValue(of({})),
    };

    TestBed.configureTestingModule({
      imports: [ServiceCatalogComponent],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        { provide: ServiceCategoriesClient, useValue: categoriesClient },
        { provide: ServicesClient, useValue: servicesClient },
        { provide: ConfirmationService, useValue: { confirm: vi.fn() } },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    });
    // ServiceForm (w drawerze) re-providuje realne klienty w swoich `providers`.
    // Drawer trzyma body w DOM także gdy zamknięty, więc nadpisujemy je stubami,
    // by formularz nie strzelał realnym HTTP w teście katalogu.
    TestBed.overrideComponent(ServiceForm, {
      set: {
        providers: [
          { provide: VatRatesClient, useValue: { getVatRates: vi.fn().mockReturnValue(of([])) } },
          { provide: EmployeesClient, useValue: { getEmployees: vi.fn().mockReturnValue(of([])) } },
          {
            provide: ServiceCategoriesClient,
            useValue: { getServiceCategories: vi.fn().mockReturnValue(of([])) },
          },
          {
            provide: MediaClient,
            useValue: { uploadImage: vi.fn().mockReturnValue(of({})) },
          },
        ],
      },
    });
    await TestBed.compileComponents();
    fixture = TestBed.createComponent(ServiceCatalogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  function sections() {
    return (component as any).orderedSections() as Array<{ kind: string; id: string }>;
  }

  it('scalona lista zawiera kategorie + sekcję „Bez kategorii" na końcu (domyślny indeks)', () => {
    const s = sections();
    expect(s.map((x) => x.kind)).toEqual(['category', 'category', 'category', 'uncategorized']);
    expect(s.map((x) => x.id)).toEqual(['c1', 'c2', 'c3', '__uncategorized__']);
  });

  it('sortuje sekcje rosnąco po orderIndex; sekcja „Bez kategorii" przy małym indeksie ląduje wyżej', async () => {
    // kategorie na 0/10/20, sekcja „Bez kategorii" na 5 → ląduje między c1 a c2
    categoriesClient.getServiceCategories.mockReturnValue(
      of([
        { id: 'c1', name: 'Cat 1', orderIndex: 0 },
        { id: 'c2', name: 'Cat 2', orderIndex: 10 },
        { id: 'c3', name: 'Cat 3', orderIndex: 20 },
      ]),
    );
    categoriesClient.getUncategorizedOrder.mockReturnValue(of({ orderIndex: 5 }));
    const f = TestBed.createComponent(ServiceCatalogComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    const s = (f.componentInstance as any).orderedSections() as Array<{ kind: string; id: string }>;
    expect(s.map((x) => x.id)).toEqual(['c1', '__uncategorized__', 'c2', 'c3']);
  });

  it('onSectionReorder woła reorderCatalogSections z GUID-ami i undefined na pozycji „Bez kategorii"', () => {
    // przenieś sekcję „Bez kategorii" (index 3) na początek (index 0)
    component.onSectionReorder({ previousIndex: 3, currentIndex: 0 } as any);
    expect(categoriesClient.reorderCatalogSections).toHaveBeenCalledWith({
      orderedSections: [undefined, 'c1', 'c2', 'c3'],
    });
    expect(sections().map((x) => x.id)).toEqual(['__uncategorized__', 'c1', 'c2', 'c3']);
  });

  it('onSectionReorder kategorii buduje payload z samymi GUID-ami', () => {
    component.onSectionReorder({ previousIndex: 0, currentIndex: 2 } as any);
    expect(categoriesClient.reorderCatalogSections).toHaveBeenCalledWith({
      orderedSections: ['c2', 'c3', 'c1', undefined],
    });
  });

  it('onSectionReorder bez zmiany pozycji nie woła API', () => {
    component.onSectionReorder({ previousIndex: 1, currentIndex: 1 } as any);
    expect(categoriesClient.reorderCatalogSections).not.toHaveBeenCalled();
  });

  it('onSectionReorder rewertuje lokalną listę gdy request padnie', async () => {
    categoriesClient.reorderCatalogSections.mockReturnValue(throwError(() => new Error('boom')));
    const before = sections().map((x) => x.id);
    component.onSectionReorder({ previousIndex: 0, currentIndex: 3 } as any);
    await Promise.resolve();
    await Promise.resolve();
    expect(sections().map((x) => x.id)).toEqual(before);
  });

  it('onOrphanServiceReorder woła reorderServices z categoryId undefined', () => {
    component.onOrphanServiceReorder({ previousIndex: 1, currentIndex: 0 } as any);
    expect(servicesClient.reorderServices).toHaveBeenCalledWith({
      orderedServiceIds: ['s2', 's1'],
      categoryId: undefined,
    });
  });

  it('onServiceSaved inkrementuje refreshTick (propagacja reloadu do list usług w kategoriach)', () => {
    const before = (component as any).refreshTick();
    component.onServiceSaved({ id: 's1', isUpdate: true });
    expect((component as any).refreshTick()).toBe(before + 1);
  });

  it('usunięcie usługi inkrementuje refreshTick (po potwierdzeniu)', () => {
    const confirm = TestBed.inject(ConfirmationService) as any;
    const before = (component as any).refreshTick();
    component.handleDeleteService('s1');
    // ConfirmationService jest stubem — odpalamy callback accept ręcznie.
    const accept = confirm.confirm.mock.calls.at(-1)[0].accept;
    accept();
    expect((component as any).refreshTick()).toBe(before + 1);
  });

  it('sekcja „Bez kategorii" renderuje się jak karta (lita ramka, ikona, tytuł) z uchwytem, bez akcji usuń', () => {
    const orphanItem = fixture.nativeElement.querySelector('[data-testid="uncategorized-drag-item"]');
    expect(orphanItem).toBeTruthy();
    // Boczny uchwyt przeciągania pojawia się dopiero w trybie zmiany kolejności.
    (component as any).reorderMode.set(true);
    fixture.detectChanges();
    const orphanItemReorder = fixture.nativeElement.querySelector('[data-testid="uncategorized-drag-item"]');
    expect(orphanItemReorder.querySelector('[data-testid="uncategorized-drag-handle"]')).toBeTruthy();
    (component as any).reorderMode.set(false);
    fixture.detectChanges();

    const orphanCard = fixture.nativeElement.querySelector('[data-testid="orphan-services-card"]');
    expect(orphanCard).toBeTruthy();
    // lita karta — bez przerywanej ramki
    expect(orphanCard.className).not.toContain('border-dashed');
    const title = orphanCard.querySelector('h3');
    expect(title.textContent.trim()).toBe('Bez kategorii');
    expect(title.className).toContain('truncate');
    // Nagłówek sekcji nie oferuje akcji kategorii (edytuj/usuń) — w przeciwieństwie do
    // kart kategorii. Kebaby w wierszach usług są osobne i dozwolone, więc sprawdzamy
    // sam nagłówek (pierwszy div), nie całą kartę.
    const header = orphanCard.querySelector(':scope > div');
    expect(header.querySelector('p-tieredmenu')).toBeNull();
    expect(header.textContent).not.toContain('Usuń');
  });

  it('sekcja „Bez kategorii" pokazuje usługi od razu (model cennika, bez rozwijania)', () => {
    const orphanCard = fixture.nativeElement.querySelector('[data-testid="orphan-services-card"]');
    expect(orphanCard).toBeTruthy();
    // usługi widoczne bez żadnego toggla (2 orphany z beforeEach: s1, s2)
    expect(
      orphanCard.querySelectorAll('[data-testid="orphan-service-drag-item"]').length,
    ).toBeGreaterThan(0);
    // szybkie dodawanie w nagłówku sekcji, brak toggla rozwijania
    expect(orphanCard.querySelector('[data-testid="orphan-add-service-btn"]')).toBeTruthy();
    expect(orphanCard.querySelector('[data-testid="orphan-toggle-services"]')).toBeNull();
  });

  it('tryb zmiany kolejności jest domyślnie wyłączony, a toggle go przełącza', () => {
    expect((component as any).reorderMode()).toBe(false);
    (component as any).toggleReorder();
    expect((component as any).reorderMode()).toBe(true);
    (component as any).toggleReorder();
    expect((component as any).reorderMode()).toBe(false);
  });

  it('uchwyty przeciągania kategorii są ukryte domyślnie i pojawiają się w trybie zmiany kolejności', () => {
    expect(
      fixture.nativeElement.querySelectorAll('[data-testid="category-drag-handle"]').length,
    ).toBe(0);
    (component as any).reorderMode.set(true);
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelectorAll('[data-testid="category-drag-handle"]').length,
    ).toBeGreaterThan(0);
  });

  it('FAB i przyciski dodawania znikają w trybie zmiany kolejności', () => {
    expect(fixture.nativeElement.querySelector('[data-testid="catalog-fab-new-service"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="catalog-new-category-inline"]')).toBeTruthy();
    (component as any).reorderMode.set(true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="catalog-fab-new-service"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="catalog-new-category-inline"]')).toBeNull();
  });

  it('serviceCountFor zlicza usługi po categoryId (z allServices)', () => {
    // allServices w tym teście to [s1, s2] bez categoryId → 0 dla dowolnej kategorii.
    expect((component as any).serviceCountFor('c1')).toBe(0);
    expect((component as any).serviceCountFor(undefined)).toBe(0);
  });
});

describe('ServiceCatalogComponent — sekcja „Bez kategorii" tylko gdy są orphany', () => {
  function build(opts: { categories: any; services: any[]; uncategorizedOrder?: any }) {
    const categoriesClient = {
      getServiceCategories: vi.fn().mockReturnValue(opts.categories),
      reorderServiceCategories: vi.fn().mockReturnValue(of({})),
      reorderCatalogSections: vi.fn().mockReturnValue(of({})),
      getUncategorizedOrder: vi
        .fn()
        .mockReturnValue(opts.uncategorizedOrder ?? of({ orderIndex: UNCATEGORIZED_DEFAULT_ORDER })),
      deleteServiceCategory: vi.fn().mockReturnValue(of({})),
    };
    const servicesClient = {
      getServices: vi.fn().mockReturnValue(of(opts.services)),
      reorderServices: vi.fn().mockReturnValue(of({})),
      deleteService: vi.fn().mockReturnValue(of({})),
    };

    TestBed.configureTestingModule({
      imports: [ServiceCatalogComponent],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        { provide: ServiceCategoriesClient, useValue: categoriesClient },
        { provide: ServicesClient, useValue: servicesClient },
        { provide: ConfirmationService, useValue: { confirm: vi.fn() } },
        { provide: MessageService, useValue: { add: vi.fn() } },
      ],
    });
    TestBed.overrideComponent(ServiceForm, {
      set: {
        providers: [
          { provide: VatRatesClient, useValue: { getVatRates: vi.fn().mockReturnValue(of([])) } },
          { provide: EmployeesClient, useValue: { getEmployees: vi.fn().mockReturnValue(of([])) } },
          {
            provide: ServiceCategoriesClient,
            useValue: { getServiceCategories: vi.fn().mockReturnValue(of([])) },
          },
          {
            provide: MediaClient,
            useValue: { uploadImage: vi.fn().mockReturnValue(of({})) },
          },
        ],
      },
    });
    return TestBed.createComponent(ServiceCatalogComponent);
  }

  it('brak orphanów → scalona lista NIE zawiera sekcji „Bez kategorii"', async () => {
    const fixture = build({
      categories: of([{ id: 'c1', name: 'Cat 1', orderIndex: 0 }]),
      services: [{ id: 's1', name: 'S1', categoryId: 'c1', price: { amount: 10, currency: 'PLN' } }],
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const s = (fixture.componentInstance as any).orderedSections() as Array<{ kind: string }>;
    expect(s.every((x) => x.kind === 'category')).toBe(true);
    expect(fixture.nativeElement.querySelector('[data-testid="uncategorized-drag-item"]')).toBeNull();
  });

  it('usługa z categoryId spoza widocznych kategorii (martwa kategoria) trafia do orphanServices i tworzy sekcję', async () => {
    const fixture = build({
      categories: of([{ id: 'c1', name: 'Cat 1', orderIndex: 0 }]),
      services: [
        { id: 's1', name: 'S1', categoryId: 'c1', price: { amount: 10, currency: 'PLN' } },
        // duch: categoryId wskazuje na nieistniejącą/usuniętą kategorię
        { id: 'ghost', name: 'Ghost', categoryId: 'dead', price: { amount: 20, currency: 'PLN' } },
        // klasyczny orphan
        { id: 's3', name: 'S3', price: { amount: 30, currency: 'PLN' } },
      ],
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const ids = (fixture.componentInstance as any).orphanServices().map((s: any) => s.id);
    expect(ids).toContain('ghost');
    expect(ids).toContain('s3');
    expect(ids).not.toContain('s1');

    // renderuje się w sekcji „Bez kategorii"
    const orphanCard = fixture.nativeElement.querySelector('[data-testid="orphan-services-card"]');
    expect(orphanCard).toBeTruthy();
  });

  it('gdy kategorie jeszcze się ładują, usługa z categoryId NIE jest błędnie pokazywana jako orphan (brak migotania)', async () => {
    // categories nigdy nie emituje → rxResource pozostaje w stanie loading.
    // services emituje od razu (of), więc allServices.value() jest już wypełnione.
    const fixture = build({
      categories: new Subject<any>(),
      services: [
        { id: 's1', name: 'S1', categoryId: 'c1', price: { amount: 10, currency: 'PLN' } },
        { id: 's3', name: 'S3', price: { amount: 30, currency: 'PLN' } },
      ],
    });
    fixture.detectChanges();
    // Nie używamy whenStable() — wiszący Subject (categories) trzyma zone w stanie
    // unstable na zawsze. Wystarczy przepuścić microtaski, by `of(services)` rozwiązało
    // allServices, zostawiając categories w loading.
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    expect((fixture.componentInstance as any).categories.isLoading()).toBe(true);
    expect((fixture.componentInstance as any).allServices.value().length).toBe(2);
    const ids = (fixture.componentInstance as any).orphanServices().map((s: any) => s.id);
    // tylko prawdziwy orphan (bez categoryId); usługa z categoryId pozostaje "swoja"
    expect(ids).toEqual(['s3']);
  });

  it('zwykłe orphany (!categoryId) działają niezależnie od kategorii', async () => {
    const fixture = build({
      categories: of([{ id: 'c1', name: 'Cat 1', orderIndex: 0 }]),
      services: [
        { id: 's1', name: 'S1', categoryId: 'c1', price: { amount: 10, currency: 'PLN' } },
        { id: 's2', name: 'S2', price: { amount: 20, currency: 'PLN' } },
      ],
    });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const ids = (fixture.componentInstance as any).orphanServices().map((s: any) => s.id);
    expect(ids).toEqual(['s2']);
  });
});
