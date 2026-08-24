import { Component, computed, effect, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ServiceCategoriesClient,
  ServiceCategoryDto,
  ServiceDto,
  ServicesClient,
} from '@core/api/api-client';
import { Router } from '@angular/router';
import { ServiceCategoryCardComponent } from '../ui/service-category-card.component';
import { ServiceCardComponent } from '../ui/service-card.component';
import { ButtonModule } from 'primeng/button';
import { rxResource } from '@angular/core/rxjs-interop';
import { ConfirmationService, MessageService } from 'primeng/api';
import { lastValueFrom } from 'rxjs';
import { ServiceFormDrawerComponent } from './service-form-drawer.component';
import { CategoryFormDrawerComponent } from './category-form-drawer.component';
import {
  CdkDrag,
  CdkDragDrop,
  CdkDragHandle,
  CdkDropList,
  moveItemInArray,
} from '@angular/cdk/drag-drop';

/** Domyślny indeks sekcji „Bez kategorii" (na końcu listy), gdy backend nie zwrócił innej pozycji. */
const UNCATEGORIZED_DEFAULT_ORDER = 1_000_000;

/** Element scalonej listy katalogu: kategoria (przeciągalna karta) albo sekcja „Bez kategorii". */
type CatalogSection =
  | { kind: 'category'; id: string; orderIndex: number; data: ServiceCategoryDto }
  | { kind: 'uncategorized'; id: string; orderIndex: number };

@Component({
  selector: 'app-service-catalog',
  standalone: true,
  imports: [
    CommonModule,
    ServiceCategoryCardComponent,
    ServiceCardComponent,
    ButtonModule,
    ServiceFormDrawerComponent,
    CategoryFormDrawerComponent,
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
  ],
  template: `
    <div class="admin-glass-card rounded-4xl px-3 py-4 sm:p-8">
      <div class="flex flex-row items-center justify-between mb-5 sm:mb-8 gap-3">
        <div class="min-w-0">
          <h2 class="text-2xl sm:text-4xl font-black tracking-tight text-surface-900 leading-none">
            Katalog
          </h2>
          <p class="hidden sm:block mt-2 text-surface-600 dark:text-surface-400 font-sans tracking-wide text-sm sm:text-base">
            Zarządzaj ofertą swoich usług i pakietów
          </p>
        </div>

        <div class="flex flex-row items-center gap-2 shrink-0">
          @if (!isEmpty()) {
            <button
              pButton
              type="button"
              data-testid="catalog-reorder-toggle"
              (click)="toggleReorder()"
              [label]="reorderMode() ? 'GOTOWE' : 'KOLEJNOŚĆ'"
              [icon]="reorderMode() ? 'pi pi-check' : 'pi pi-sort-alt'"
              [outlined]="!reorderMode()"
              severity="secondary"
              class="p-button-sm font-bold uppercase tracking-wider text-xs"
            ></button>
          }

          @if (!reorderMode()) {
            <button
              pButton
              data-testid="catalog-new-service-btn"
              data-tour="services-new"
              (click)="openCreateService()"
              label="NOWA USŁUGA"
              icon="pi pi-plus"
              class="hidden sm:inline-flex p-button-primary px-8 py-3 uppercase tracking-wider font-bold"
            ></button>
            <button
              pButton
              data-testid="catalog-new-category-btn"
              data-tour="services-new-category"
              (click)="handleAddCategory()"
              label="NOWA KATEGORIA"
              icon="pi pi-tag"
              [outlined]="true"
              severity="secondary"
              class="hidden sm:inline-flex px-6 py-3 uppercase tracking-wider font-bold"
            ></button>
          }
        </div>
      </div>

      @if (reorderMode()) {
        <div
          class="mb-5 flex items-center gap-3 rounded-2xl px-4 py-3 bg-primary-50/70 dark:bg-primary-900/25 border border-primary-100 dark:border-primary-900/40"
          data-testid="catalog-reorder-hint"
        >
          <i class="pi pi-sort-alt text-primary"></i>
          <span class="text-sm text-surface-700 dark:text-surface-300">
            Przeciągaj uchwyty, aby zmienić kolejność kategorii i usług w nich.
          </span>
        </div>
      }

      @if (isEmpty()) {
        <div
          class="flex flex-col items-center justify-center text-center py-16 px-6 rounded-3xl border border-dashed border-surface-300/80 dark:border-surface-200/70 bg-surface-50/40 dark:bg-surface-50/40"
          data-testid="catalog-empty-state"
        >
          <div class="w-16 h-16 rounded-2xl bg-primary-50 dark:bg-primary-900/30 grid place-items-center mb-4">
            <i class="pi pi-briefcase text-primary text-2xl"></i>
          </div>
          <h3 class="text-2xl font-black text-surface-900 mb-2">
            Dodaj swoją pierwszą usługę
          </h3>
          <p class="text-sm text-surface-600 dark:text-surface-400 max-w-md mb-6">
            Zacznij od jednej usługi — np. „Manicure hybrydowy" za 120 zł na 90 minut.
            Kategorie nie są wymagane.
          </p>
          <div class="flex flex-col sm:flex-row gap-3">
            <button
              pButton
              data-testid="catalog-empty-state-cta"
              data-tour="services-new"
              (click)="openCreateService()"
              label="DODAJ USŁUGĘ"
              icon="pi pi-plus"
              class="p-button-primary px-8 py-3 uppercase tracking-wider font-bold"
            ></button>
            <button
              pButton
              (click)="handleAddCategory()"
              label="Lub utwórz kategorię"
              [text]="true"
              class="p-button-secondary"
            ></button>
          </div>
        </div>
      } @else {
        <div
          class="grid grid-cols-1 gap-3 sm:gap-4 pb-20 sm:pb-0"
          cdkDropList
          (cdkDropListDropped)="onSectionReorder($event)"
        >
          @for (section of orderedSections(); track section.id) {
            @if (section.kind === 'category') {
              <div cdkDrag [cdkDragDisabled]="!reorderMode()" class="flex items-start gap-2" data-testid="category-drag-item">
                @if (reorderMode()) {
                  <button
                    type="button"
                    cdkDragHandle
                    data-testid="category-drag-handle"
                    aria-label="Przeciągnij, aby zmienić kolejność kategorii"
                    class="mt-2 shrink-0 grid place-items-center w-8 h-8 rounded-lg text-surface-400 hover:text-surface-600 bg-white/75 dark:bg-surface-50/75 border border-surface-200/70 dark:border-surface-200/70 shadow-sm cursor-grab active:cursor-grabbing touch-none"
                  >
                    <i class="pi pi-bars text-sm"></i>
                  </button>
                }
                <div class="flex-1 min-w-0">
                  <app-service-category-card
                    [category]="section.data"
                    [refreshTick]="refreshTick()"
                    [serviceCount]="serviceCountFor(section.data.id)"
                    [reorderMode]="reorderMode()"
                    (editCategory)="handleEditCategory($event)"
                    (deleteCategory)="handleDeleteCategory($event)"
                    (addService)="openCreateService(section.data.id)"
                    (editService)="openEditService($event)"
                  />
                </div>
              </div>
            } @else {
              <div cdkDrag [cdkDragDisabled]="!reorderMode()" class="flex items-start gap-2" data-testid="uncategorized-drag-item">
                @if (reorderMode()) {
                  <button
                    type="button"
                    cdkDragHandle
                    data-testid="uncategorized-drag-handle"
                    aria-label="Przeciągnij, aby zmienić kolejność sekcji"
                    class="mt-2 shrink-0 grid place-items-center w-8 h-8 rounded-lg text-surface-400 hover:text-surface-600 bg-white/75 dark:bg-surface-50/75 border border-surface-200/70 dark:border-surface-200/70 shadow-sm cursor-grab active:cursor-grabbing touch-none"
                  >
                    <i class="pi pi-bars text-sm"></i>
                  </button>
                }
                <section
                  class="flex-1 min-w-0 rounded-2xl border border-surface-200/70 dark:border-surface-200/60 bg-white/70 dark:bg-surface-50/40 overflow-hidden"
                  data-testid="orphan-services-card"
                >
                  <div class="flex items-center gap-2 px-3 py-2.5">
                    <h3 class="text-lg sm:text-xl font-black tracking-tight text-surface-900 m-0 truncate">Bez kategorii</h3>
                    <span class="shrink-0 text-sm font-bold text-surface-500 dark:text-surface-400 bg-surface-100 dark:bg-surface-100 rounded-full px-2.5 py-0.5">
                      {{ orphanServices().length }}
                    </span>

                    @if (!reorderMode()) {
                      <button
                        type="button"
                        data-testid="orphan-add-service-btn"
                        (click)="openCreateService(null)"
                        class="ml-auto shrink-0 flex items-center gap-1.5 px-3 py-2 rounded-xl text-sm font-bold text-primary hover:bg-primary-50 dark:hover:bg-primary-900/30 transition-colors cursor-pointer"
                      >
                        <i class="pi pi-plus text-xs"></i>
                        <span>Dodaj</span>
                      </button>
                    }
                  </div>

                  <div
                    class="border-t border-surface-100 dark:border-surface-200/50 px-1.5 py-1 divide-y divide-surface-100 dark:divide-surface-200/50"
                    cdkDropList
                    (cdkDropListDropped)="onOrphanServiceReorder($event)"
                  >
                    @for (service of orderedOrphanServices(); track service.id) {
                      <div cdkDrag [cdkDragDisabled]="!reorderMode()" class="flex items-stretch gap-1" data-testid="orphan-service-drag-item">
                        @if (reorderMode()) {
                          <button
                            type="button"
                            cdkDragHandle
                            data-testid="orphan-service-drag-handle"
                            aria-label="Przeciągnij, aby zmienić kolejność"
                            class="self-center shrink-0 grid place-items-center w-8 h-9 rounded-lg text-surface-400 hover:text-surface-600 cursor-grab active:cursor-grabbing touch-none"
                          >
                            <i class="pi pi-bars text-sm"></i>
                          </button>
                        }
                        <div class="flex-1 min-w-0">
                          <app-service-card
                            [service]="service"
                            [reorderMode]="reorderMode()"
                            (editService)="openEditService($event)"
                            (deleteService)="handleDeleteService($event)"
                          />
                        </div>
                      </div>
                    }
                  </div>
                </section>
              </div>
            }
          }

          @if (!reorderMode()) {
            <button
              type="button"
              data-testid="catalog-new-category-inline"
              data-tour="services-new-category"
              (click)="handleAddCategory()"
              class="mt-1 flex items-center justify-center gap-2 py-2.5 rounded-xl text-sm font-bold text-surface-500 dark:text-surface-400 hover:text-primary hover:bg-surface-50/80 dark:hover:bg-surface-100/40 transition-colors cursor-pointer"
            >
              <i class="pi pi-plus text-xs"></i>
              Nowa kategoria
            </button>
          }
        </div>
      }
    </div>

    <!-- FAB: szybkie dodanie usługi (tylko mobile, poza trybem zmiany kolejności) -->
    @if (!reorderMode()) {
      <button
        pButton
        type="button"
        data-testid="catalog-fab-new-service"
        data-tour="services-new"
        (click)="openCreateService()"
        label="DODAJ USŁUGĘ"
        icon="pi pi-plus"
        class="sm:hidden fixed right-4 bottom-24 z-30 rounded-full shadow-xl px-6 py-3 uppercase tracking-wider font-bold p-button-primary"
      ></button>
    }

    <app-service-form-drawer
      [isOpen]="drawerOpen()"
      [serviceId]="drawerServiceId()"
      [categoryHint]="drawerCategoryHint()"
      (closed)="onDrawerClosed()"
      (saved)="onServiceSaved($event)"
    />

    <app-category-form-drawer
      [isOpen]="categoryDrawerOpen()"
      [categoryId]="editingCategoryId()"
      (closed)="categoryDrawerOpen.set(false)"
      (saved)="onCategorySaved()"
    />
  `,
})
export class ServiceCatalogComponent {
  private router = inject(Router);
  private serviceCategoriesService = inject(ServiceCategoriesClient);
  private servicesClient = inject(ServicesClient);
  private confirmationService = inject(ConfirmationService);
  private message = inject(MessageService);

  categories = rxResource({
    stream: () => this.serviceCategoriesService.getServiceCategories(),
    defaultValue: [],
  });

  allServices = rxResource({
    stream: () => this.servicesClient.getServices(),
    defaultValue: [],
  });

  /**
   * Pozycja sekcji „Bez kategorii" w scalonej liście katalogu (zapisana na serwerze).
   * Backend zwraca duży indeks (~1 000 000), gdy nie ustawiono jawnie — sekcja ląduje
   * na końcu. Fallback w trakcie ładowania także trzyma ją na końcu.
   */
  private uncategorizedOrder = rxResource({
    stream: () => this.serviceCategoriesService.getUncategorizedOrder(),
    defaultValue: { orderIndex: UNCATEGORIZED_DEFAULT_ORDER },
  });

  protected uncategorizedOrderIndex = computed(
    () => this.uncategorizedOrder.value().orderIndex ?? UNCATEGORIZED_DEFAULT_ORDER,
  );

  /**
   * Sygnał odświeżenia propagowany do zagnieżdżonych `app-services-list` (przez
   * `service-category-card`). Inkrementowany po zapisie/usunięciu usługi — listy
   * usług w kategoriach mają własny `rxResource`, więc reload `allServices` ich nie dotyka.
   * `services-list` reaguje effectem (z pominięciem wartości początkowej).
   */
  protected refreshTick = signal(0);

  private bumpRefreshTick() {
    this.refreshTick.update((t) => t + 1);
  }

  /**
   * Tryb zmiany kolejności — domyślnie wyłączony. Poza nim uchwyty przeciągania są
   * ukryte, a `cdkDrag` wyłączone (`cdkDragDisabled`), by nie kolidować ze scrollem
   * na mobile. Włączenie odsłania uchwyty sekcji i usług.
   */
  protected reorderMode = signal(false);

  protected toggleReorder() {
    this.reorderMode.update((v) => !v);
  }

  /** Liczba usług per kategoria (z `allServices`) — do badge'a w nagłówku sekcji. */
  private serviceCountByCategory = computed(() => {
    const map = new Map<string, number>();
    for (const s of this.allServices.value()) {
      if (s.categoryId) map.set(s.categoryId, (map.get(s.categoryId) ?? 0) + 1);
    }
    return map;
  });

  protected serviceCountFor(id: string | undefined): number {
    return id ? this.serviceCountByCategory().get(id) ?? 0 : 0;
  }

  /** Zbiór id widocznych (aktywnych) kategorii — do wykrywania „duchów" wskazujących martwą kategorię. */
  private visibleCategoryIds = computed(
    () => new Set(this.categories.value().map((c) => c.id)),
  );

  // Orphan = usługa bez kategorii LUB usługa z categoryId spoza widocznych kategorii
  // (safety-net dla „duchów" po usunięciu kategorii na prod). Regułę „spoza widocznych"
  // stosujemy DOPIERO gdy kategorie są załadowane — inaczej (pusta lista w trakcie ładowania)
  // wszystkie usługi chwilowo migałyby jako orphany.
  orphanServices = computed(() => {
    const categoriesLoaded = !this.categories.isLoading();
    const visible = this.visibleCategoryIds();
    return this.allServices.value().filter((s) => {
      if (!s.categoryId) return true;
      return categoriesLoaded && !visible.has(s.categoryId);
    });
  });
  protected isEmpty = computed(
    () => this.categories.value().length === 0 && this.allServices.value().length === 0,
  );

  // ── reorder (drag & drop) ───────────────────────────────────────────────────
  // Lokalne, mutowalne kopie służą optimistic update przy DnD. Synchronizujemy je
  // z resource (źródło prawdy po reloadzie), a po drop wykonujemy revert przy błędzie.
  protected orderedOrphanServices = signal<ServiceDto[]>([]);

  /**
   * Scalona, posortowana lista „sekcji" katalogu: kategorie + (gdy są orphany)
   * jedna sekcja „Bez kategorii". Lokalny sygnał umożliwia optimistic update przy DnD;
   * synchronizujemy go z resource przez effect (źródło prawdy po reloadzie), a po drop
   * wykonujemy revert przy błędzie.
   */
  protected orderedSections = signal<CatalogSection[]>([]);

  /**
   * Buduje scaloną, posortowaną listę sekcji ze źródeł (kategorie + orphany + pozycja
   * sekcji „Bez kategorii"). Sortowanie rosnąco po `orderIndex`; remis kategorii
   * rozstrzygamy stabilnie po obecnej kolejności z backendu. Sekcja „Bez kategorii"
   * pojawia się TYLKO gdy są orphany; z domyślnym dużym indeksem ląduje na końcu.
   */
  private buildSections = computed<CatalogSection[]>(() => {
    const sections: CatalogSection[] = this.categories
      .value()
      .map((c, i) => ({
        kind: 'category' as const,
        id: c.id!,
        // brak orderIndex z backendu → zachowaj obecną kolejność (stabilny tie-break)
        orderIndex: c.orderIndex ?? i,
        data: c,
      }));

    if (this.orphanServices().length > 0) {
      sections.push({
        kind: 'uncategorized',
        id: '__uncategorized__',
        orderIndex: this.uncategorizedOrderIndex(),
      });
    }

    return sections
      .map((s, i) => ({ s, i }))
      .sort((a, b) => a.s.orderIndex - b.s.orderIndex || a.i - b.i)
      .map(({ s }) => s);
  });

  constructor() {
    effect(() => this.orderedSections.set([...this.buildSections()]));
    effect(() => this.orderedOrphanServices.set([...this.orphanServices()]));
  }

  /**
   * Drop na scalonej liście sekcji. Optimistic `moveItemInArray`, payload buduje listę,
   * gdzie kategoria → jej GUID, a sekcja „Bez kategorii" → `undefined`. Po sukcesie
   * odświeżamy źródła (kategorie + pozycja sekcji „Bez kategorii"), by indeksy z backendu
   * się zgadzały; przy błędzie revert + toast.
   */
  onSectionReorder(event: CdkDragDrop<CatalogSection[]>) {
    const { previousIndex, currentIndex } = event;
    if (previousIndex === currentIndex) return;
    const prev = this.orderedSections();
    const next = [...prev];
    moveItemInArray(next, previousIndex, currentIndex);
    this.orderedSections.set(next);
    // kategoria → GUID, sekcja „Bez kategorii" → undefined (backend rozpoznaje po pozycji).
    const orderedSections = next.map((s) => (s.kind === 'category' ? s.id : undefined));
    lastValueFrom(this.serviceCategoriesService.reorderCatalogSections({ orderedSections }))
      .then(() => {
        this.categories.reload();
        this.uncategorizedOrder.reload();
      })
      .catch((err) => {
        this.orderedSections.set(prev);
        this.message.add({
          severity: 'error',
          summary: 'Błąd',
          detail: 'Nie udało się zmienić kolejności.',
        });
        console.error('reorderCatalogSections failed:', err);
      });
  }

  onOrphanServiceReorder(event: CdkDragDrop<ServiceDto[]>) {
    const { previousIndex, currentIndex } = event;
    if (previousIndex === currentIndex) return;
    const prev = this.orderedOrphanServices();
    const next = [...prev];
    moveItemInArray(next, previousIndex, currentIndex);
    this.orderedOrphanServices.set(next);
    const orderedServiceIds = next.map((s) => s.id!).filter(Boolean);
    // categoryId null/undefined = usługi bez kategorii (orphans).
    lastValueFrom(this.servicesClient.reorderServices({ orderedServiceIds, categoryId: undefined }))
      .then(() => this.allServices.reload())
      .catch((err) => {
        this.orderedOrphanServices.set(prev);
        this.message.add({
          severity: 'error',
          summary: 'Błąd',
          detail: 'Nie udało się zmienić kolejności usług.',
        });
        console.error('reorderServices (orphans) failed:', err);
      });
  }

  // ── drawer state ───────────────────────────────────────────────────────────
  protected drawerOpen = signal(false);
  protected drawerServiceId = signal<string | undefined>(undefined);
  /** `undefined` = nie ustawiaj; `null` = bez kategorii (Bez kategorii toggle); string = kategoria. */
  protected drawerCategoryHint = signal<string | null | undefined>(undefined);

  /** Otwiera drawer w trybie tworzenia. categoryHint: undefined = brak hinta (default „bez"). */
  openCreateService(categoryHint: string | null | undefined = undefined) {
    this.drawerServiceId.set(undefined);
    this.drawerCategoryHint.set(categoryHint);
    this.drawerOpen.set(true);
  }

  openEditService(serviceId: string | undefined) {
    if (!serviceId) return;
    this.drawerServiceId.set(serviceId);
    this.drawerCategoryHint.set(undefined);
    this.drawerOpen.set(true);
  }

  onDrawerClosed() {
    this.drawerOpen.set(false);
    this.drawerServiceId.set(undefined);
    this.drawerCategoryHint.set(undefined);
  }

  onServiceSaved(_payload: { id: string | undefined; isUpdate: boolean }) {
    this.allServices.reload();
    this.categories.reload();
    // Listy usług w kategoriach mają własny resource — wymuś ich reload.
    this.bumpRefreshTick();
  }

  // ── kategorie (drawer na stronie, zamiast pełnostronicowego route'a) ─────────
  protected categoryDrawerOpen = signal(false);
  protected editingCategoryId = signal<string | undefined>(undefined);

  handleEditCategory(categoryId: string | undefined) {
    if (!categoryId) return;
    this.editingCategoryId.set(categoryId);
    this.categoryDrawerOpen.set(true);
  }

  handleAddCategory() {
    this.editingCategoryId.set(undefined);
    this.categoryDrawerOpen.set(true);
  }

  onCategorySaved() {
    this.categoryDrawerOpen.set(false);
    this.categories.reload();
    this.allServices.reload();
    this.bumpRefreshTick();
  }

  handleDeleteCategory(categoryId: string | undefined) {
    this.confirmationService.confirm({
      message: 'Czy na pewno chcesz usunąć tę kategorię wraz ze wszystkimi usługami?',
      header: 'Potwierdzenie usunięcia kategorii',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Tak',
      rejectLabel: 'Nie',
      accept: () => {
        if (!categoryId) return;
        this.serviceCategoriesService.deleteServiceCategory(categoryId).subscribe({
          next: () => {
            this.categories.reload();
            this.allServices.reload();
          },
          error: (err) => console.error('Błąd podczas usuwania kategorii:', err),
        });
      },
    });
  }

  // ── usuwanie usługi (orphan i z kategorii) ─────────────────────────────────
  handleDeleteService(serviceId: string | undefined) {
    this.confirmationService.confirm({
      message:
        'Usługa zostanie ukryta w nowych rezerwacjach. Historia wizyt pozostaje nietknięta.',
      header: 'Usunąć usługę?',
      icon: 'pi pi-info-circle',
      acceptLabel: 'Usuń',
      rejectLabel: 'Anuluj',
      accept: () => {
        if (!serviceId) return;
        this.servicesClient.deleteService(serviceId).subscribe({
          next: () => {
            this.allServices.reload();
            // Usługa mogła należeć do kategorii — odśwież listy w kartach kategorii.
            this.bumpRefreshTick();
          },
          error: (err) => console.error('Błąd podczas usuwania usługi:', err),
        });
      },
    });
  }
}
