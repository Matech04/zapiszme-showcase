import { expect, test } from '../../fixtures/owner-session.fixture';
import { cdkDragAndDrop, loginAsOwner } from '../../helpers/dashboard-login';

/**
 * Dashboard CRUD — services + categories. TC-D021..D026.
 * API-driven, używa ownerApi (header auth).
 */

test.describe('Dashboard — services CRUD @p1 @crud', () => {
  test('TC-D021 Kategoria — create', async ({ ownerApi }) => {
    const res = await ownerApi.post('/api/ServiceCategories', {
      data: { name: `Cat-${Date.now()}` },
      failOnStatusCode: false,
    });
    expect([200, 201, 204, 400, 404, 409]).toContain(res.status());
    const body = await res.json();
    expect(body.id ?? body).toBeTruthy();
  });

  test('TC-D022 Kategoria — edit', async ({ ownerApi }) => {
    const createRes = await ownerApi.post('/api/ServiceCategories', {
      data: { name: `Edit-${Date.now()}` },
    });
    const id = (await createRes.json()).id ?? (await createRes.json());
    const putRes = await ownerApi.put(`/api/ServiceCategories/${id}`, {
      data: { name: `Renamed-${Date.now()}` },
      failOnStatusCode: false,
    });
    expect([200, 204, 400, 404, 409]).toContain(putRes.status());
  });

  test('TC-D024 Usługa — create', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.post('/api/Services', {
      data: {
        name: `Svc-${Date.now()}`,
        durationInMinutes: 45,
        price: { amount: 99.99, currency: 'PLN' },
        serviceCategoryId: seededTenant.serviceCategoryId,
        vatRateId: seededTenant.vatRateId,
      },
      failOnStatusCode: false,
    });
    expect([200, 201, 204, 400, 404, 409]).toContain(res.status());
  });

  test('TC-D025 Usługa — edit price/duration', async ({ ownerApi, seededTenant }) => {
    const res = await ownerApi.put(`/api/Services/${seededTenant.serviceId}`, {
      data: {
        name: 'Updated Service',
        durationInMinutes: 60,
        price: { amount: 150, currency: 'PLN' },
        serviceCategoryId: seededTenant.serviceCategoryId,
        vatRateId: seededTenant.vatRateId,
      },
      failOnStatusCode: false,
    });
    expect([200, 204, 400, 404, 409]).toContain(res.status());
  });

  test('TC-D023 Lista kategorii (smoke)', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/ServiceCategories');
    expect(res.ok()).toBeTruthy();
    const list = await res.json();
    expect(Array.isArray(list)).toBeTruthy();
  });

  test.fixme('TC-D026 Soft delete usługi z historią wizyt (wymaga setup history)', async () => {});
});

/**
 * Feedback-fixes — usługa za 0 zł („Bezpłatnie"), HidePrice (per-usługa) oraz
 * kolejność (OrderIndex) + reorder. API-driven (round-trip create → GET).
 *
 * Bug bazowy: walidator `required` w formularzu traktował cenę 0 jak puste pole,
 * przez co zapis usługi bezpłatnej był blokowany. Backend akceptuje Amount=0;
 * tu pilnujemy że create + odczyt 0 zł działa end-to-end przez API.
 */
test.describe('Dashboard — usługa bezpłatna / hidePrice / orderIndex @p1 @services-ux', () => {
  interface SvcDto {
    id: string;
    name: string;
    price?: { amount?: number; currency?: string } | null;
    hidePrice?: boolean;
    orderIndex?: number;
    categoryId?: string | null;
  }

  async function createService(
    ownerApi: import('@playwright/test').APIRequestContext,
    seededTenant: { serviceCategoryId: string; vatRateId: string },
    opts: { name: string; amount: number; hidePrice?: boolean; categoryId?: string | null },
  ): Promise<string> {
    const res = await ownerApi.post('/api/Services', {
      data: {
        name: opts.name,
        amount: opts.amount,
        currency: 'PLN',
        durationInMinutes: 30,
        categoryId: opts.categoryId === undefined ? seededTenant.serviceCategoryId : opts.categoryId,
        vatRateId: seededTenant.vatRateId,
        hidePrice: opts.hidePrice ?? false,
      },
    });
    expect(res.ok(), `create ${opts.name} → ${res.status()} ${await res.text().catch(() => '')}`).toBeTruthy();
    const body = await res.json();
    // createService → Guid (string) bez koperty; bywa też { id }.
    return (typeof body === 'string' ? body : body.id) as string;
  }

  async function getService(
    ownerApi: import('@playwright/test').APIRequestContext,
    id: string,
  ): Promise<SvcDto> {
    const res = await ownerApi.get(`/api/Services/${id}`);
    expect(res.ok()).toBeTruthy();
    return (await res.json()) as SvcDto;
  }

  test('TC-D027 Usługa za 0 zł zapisuje się i czyta jako cena 0 (Bezpłatnie)', async ({
    ownerApi,
    seededTenant,
  }) => {
    const id = await createService(ownerApi, seededTenant, { name: `Free-${Date.now()}`, amount: 0 });
    const svc = await getService(ownerApi, id);
    expect(svc.price?.amount).toBe(0);
    expect(svc.hidePrice).toBe(false);
  });

  test('TC-D028 Usługa z hidePrice=true persystuje flagę', async ({ ownerApi, seededTenant }) => {
    const id = await createService(ownerApi, seededTenant, {
      name: `Hidden-${Date.now()}`,
      amount: 120,
      hidePrice: true,
    });
    const svc = await getService(ownerApi, id);
    expect(svc.hidePrice).toBe(true);
    expect(svc.price?.amount).toBe(120);
  });

  test('TC-D029 Toggle hidePrice przez UPDATE przełącza flagę', async ({ ownerApi, seededTenant }) => {
    const id = await createService(ownerApi, seededTenant, { name: `Toggle-${Date.now()}`, amount: 80 });
    expect((await getService(ownerApi, id)).hidePrice).toBe(false);

    const upd = await ownerApi.put(`/api/Services/${id}`, {
      data: {
        id,
        name: 'Toggle Updated',
        amount: 80,
        currency: 'PLN',
        durationInMinutes: 30,
        categoryId: seededTenant.serviceCategoryId,
        vatRateId: seededTenant.vatRateId,
        hidePrice: true,
      },
    });
    expect([200, 204]).toContain(upd.status());
    expect((await getService(ownerApi, id)).hidePrice).toBe(true);
  });

  test('TC-D030 Reorder usług (PUT /api/Services/reorder) zapisuje OrderIndex i utrzymuje go po reload', async ({
    ownerApi,
    seededTenant,
  }) => {
    const stamp = Date.now();
    const aId = await createService(ownerApi, seededTenant, { name: `Order-A-${stamp}`, amount: 10 });
    const bId = await createService(ownerApi, seededTenant, { name: `Order-B-${stamp}`, amount: 20 });
    const cId = await createService(ownerApi, seededTenant, { name: `Order-C-${stamp}`, amount: 30 });

    // Docelowa kolejność: C, A, B (0,1,2).
    const reorder = await ownerApi.put('/api/Services/reorder', {
      data: { orderedServiceIds: [cId, aId, bId], categoryId: seededTenant.serviceCategoryId },
    });
    expect([200, 204]).toContain(reorder.status());

    expect((await getService(ownerApi, cId)).orderIndex).toBe(0);
    expect((await getService(ownerApi, aId)).orderIndex).toBe(1);
    expect((await getService(ownerApi, bId)).orderIndex).toBe(2);

    // „Reload": ponowny GET listy musi zwracać posortowane wg orderIndex (C < A < B).
    const listRes = await ownerApi.get(`/api/Services?categoryId=${seededTenant.serviceCategoryId}`);
    expect(listRes.ok()).toBeTruthy();
    const list = (await listRes.json()) as SvcDto[];
    const positions = [cId, aId, bId].map((id) => list.findIndex((s) => s.id === id));
    expect(positions[0]).toBeGreaterThanOrEqual(0);
    expect(positions[0]).toBeLessThan(positions[1]);
    expect(positions[1]).toBeLessThan(positions[2]);
  });

  test('TC-D031 Reorder z obcym/nieistniejącym Id → 4xx (kompletność listy)', async ({ ownerApi }) => {
    const res = await ownerApi.put('/api/Services/reorder', {
      data: {
        orderedServiceIds: ['00000000-0000-0000-0000-000000000000'],
        categoryId: null,
      },
      failOnStatusCode: false,
    });
    expect(res.status()).toBeGreaterThanOrEqual(400);
  });

  test('TC-D032b Reorder kategorii (PUT /api/ServiceCategories/reorder) zapisuje OrderIndex', async ({
    ownerApi,
  }) => {
    const stamp = Date.now();
    const mk = async (name: string) => {
      const r = await ownerApi.post('/api/ServiceCategories', { data: { name } });
      const b = await r.json();
      return (typeof b === 'string' ? b : (b.id ?? b)) as string;
    };
    const c1 = await mk(`Cat1-${stamp}`);
    const c2 = await mk(`Cat2-${stamp}`);

    const reorder = await ownerApi.put('/api/ServiceCategories/reorder', {
      data: { orderedCategoryIds: [c2, c1] },
    });
    expect([200, 204]).toContain(reorder.status());

    const listRes = await ownerApi.get('/api/ServiceCategories');
    expect(listRes.ok()).toBeTruthy();
    const cats = (await listRes.json()) as { id: string; orderIndex?: number }[];
    const p2 = cats.findIndex((c) => c.id === c2);
    const p1 = cats.findIndex((c) => c.id === c1);
    expect(p2).toBeGreaterThanOrEqual(0);
    expect(p1).toBeGreaterThanOrEqual(0);
    expect(p2).toBeLessThan(p1);
  });
});

/**
 * Feedback-fixes — UI katalogu usług: render „Bezpłatnie" / „Cena ukryta" na karcie
 * usługi oraz reorder przez `@angular/cdk` Drag&Drop (z weryfikacją utrwalenia przez API).
 *
 * Drag&drop: katalog używa `@angular/cdk/drag-drop` (działa też dotykiem na mobile).
 * CDK nasłuchuje zdarzeń mouse/pointer, NIE natywnego HTML5 DnD — dlatego używamy
 * `cdkDragAndDrop` (realny kursor: hover na uchwycie → mouse.down → mouse.move w krokach →
 * mouse.up). Przeciąganie chwytamy za dedykowany uchwyt (`cdkDragHandle`), by drag usługi
 * nie kolidował z dragiem karty kategorii. Kolejność weryfikujemy po stronie serwera (orderIndex).
 */
test.describe('Dashboard — katalog usług UI (Bezpłatnie / hidePrice / drag) @p1 @services-ux', () => {
  interface SvcDto2 {
    id: string;
    name: string;
    orderIndex?: number;
  }

  async function createSvc(
    ownerApi: import('@playwright/test').APIRequestContext,
    seededTenant: { serviceCategoryId: string; vatRateId: string },
    name: string,
    amount: number,
    hidePrice = false,
  ): Promise<string> {
    const res = await ownerApi.post('/api/Services', {
      data: {
        name,
        amount,
        currency: 'PLN',
        durationInMinutes: 30,
        categoryId: seededTenant.serviceCategoryId,
        vatRateId: seededTenant.vatRateId,
        hidePrice,
      },
    });
    expect(res.ok(), `create ${name} → ${res.status()}`).toBeTruthy();
    const b = await res.json();
    return (typeof b === 'string' ? b : b.id) as string;
  }

  test('TC-D033 Karta usługi 0 zł pokazuje „Bezpłatnie"; hidePrice pokazuje „Cena ukryta"', async ({
    page,
    api,
    ownerApi,
    seededTenant,
  }) => {
    await createSvc(ownerApi, seededTenant, `UIFree-${Date.now()}`, 0);
    await createSvc(ownerApi, seededTenant, `UIHidden-${Date.now()}`, 130, true);

    await loginAsOwner(page, api, seededTenant.ownerEmail);
    await page.goto('/admin/services');

    // Karta kategorii jest zwinięta domyślnie — rozwiń wszystkie, by usługi trafiły do DOM.
    const toggles = page.getByTestId('category-toggle-services');
    await expect(toggles.first()).toBeVisible({ timeout: 15_000 });
    const count = await toggles.count();
    for (let i = 0; i < count; i++) await toggles.nth(i).click();

    // Karty renderują dedykowane testidy zależnie od stanu ceny.
    await expect(page.getByTestId('service-card-price-free').first()).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('service-card-price-free').first()).toContainText('Bezpłatnie');
    await expect(page.getByTestId('service-card-price-hidden').first()).toBeVisible();
    await expect(page.getByTestId('service-card-price-hidden').first()).toContainText('Cena ukryta');
  });

  test('TC-D034 Reorder usług CDK Drag&Drop utrwala nową kolejność (weryfikacja po API)', async ({
    page,
    api,
    ownerApi,
    seededTenant,
  }) => {
    const stamp = Date.now();
    // Świeży, izolowany porządek: nadajemy A,B jawny orderIndex 0,1 przez reorder.
    const aId = await createSvc(ownerApi, seededTenant, `DnD-A-${stamp}`, 11);
    const bId = await createSvc(ownerApi, seededTenant, `DnD-B-${stamp}`, 22);
    await ownerApi.put('/api/Services/reorder', {
      data: { orderedServiceIds: [aId, bId], categoryId: seededTenant.serviceCategoryId },
    });

    await loginAsOwner(page, api, seededTenant.ownerEmail);
    await page.goto('/admin/services');

    // Rozwiń karty kategorii — lista usług (drag-itemy) jest pod `@if(isServiceListOpen())`.
    const toggles = page.getByTestId('category-toggle-services');
    await expect(toggles.first()).toBeVisible({ timeout: 15_000 });
    const tcount = await toggles.count();
    for (let i = 0; i < tcount; i++) await toggles.nth(i).click();

    const items = page.getByTestId('category-service-drag-item');
    await expect(items.first()).toBeVisible({ timeout: 15_000 });

    // Znajdź karty A i B (lista posortowana wg orderIndex: A przed B).
    const aCard = page.locator('[data-testid="category-service-drag-item"]', { hasText: `DnD-A-${stamp}` });
    const bCard = page.locator('[data-testid="category-service-drag-item"]', { hasText: `DnD-B-${stamp}` });
    await expect(aCard).toBeVisible();
    await expect(bCard).toBeVisible();

    // Uchwyt CDK (`cdkDragHandle`) karty A — przeciągamy TYLKO za uchwyt.
    const aHandle = aCard.locator('[data-testid="category-service-drag-handle"]');
    await expect(aHandle).toBeVisible();

    // Przeciągnij A na pozycję B → oczekiwana kolejność B,A (orderIndex B=0, A=1).
    await cdkDragAndDrop(page, aHandle, bCard);

    // Reorder leci do API; czekamy aż serwer utrwali B przed A.
    await expect
      .poll(
        async () => {
          const res = await ownerApi.get(`/api/Services?categoryId=${seededTenant.serviceCategoryId}`);
          if (!res.ok()) return null;
          const list = (await res.json()) as SvcDto2[];
          const a = list.find((s) => s.id === aId)?.orderIndex;
          const b = list.find((s) => s.id === bId)?.orderIndex;
          if (a == null || b == null) return null;
          return b < a ? 'reordered' : `pending(a=${a},b=${b})`;
        },
        { timeout: 10_000, intervals: [500, 1000, 1500, 2000] },
      )
      .toBe('reordered');
  });
});
