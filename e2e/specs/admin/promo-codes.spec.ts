import { expect, test } from '../../fixtures/admin-session.fixture';

/**
 * Sprint M — kody promocyjne. TC-A036..A040.
 *
 * Admin CRUD + public validate. Endpoints:
 *  - POST /api/admin/promocodes (create)
 *  - GET  /api/admin/promocodes (list)
 *  - POST /api/admin/promocodes/:id/deactivate
 *  - PATCH /api/admin/promocodes/:id/validity
 *  - POST /api/promo/validate (anonymous, rate-limited)
 *
 * PromoCodeKind: 0=AdminIssued, 1=Influencer, 2=Referral, 3=FoundingMember
 * PromoDiscountType: 0=PriceOverride, 1=PercentOff, 2=FreeMonths, 3=TrialExtension
 * PromoCodeAppliesTo: 0=NewTenantsOnly, 1=ExistingTenants, 2=Both
 */

const UNIQUE = () => Math.random().toString(36).slice(2, 8).toUpperCase();

test.describe('Admin — promo codes CRUD @p0 @billing @promo', () => {
  test('TC-A036 Admin tworzy PriceOverride code', async ({ adminApi }) => {
    const code = `E2E-PRICE-${UNIQUE()}`;
    const res = await adminApi.post('/api/admin/promocodes', {
      data: {
        code,
        kind: 0, // AdminIssued
        discountType: 0, // PriceOverride
        discountValue: 49.0,
        durationMonths: null,
        maxTotalUses: 10,
        maxUsesPerTenant: 1,
        validFrom: null,
        validUntil: null,
        appliesTo: 0, // NewTenantsOnly
        metadata: '{"source":"e2e-test"}',
      },
    });
    expect(res.ok()).toBeTruthy();
    const id = await res.json();
    expect(typeof id).toBe('string');
    expect(id).toMatch(/^[0-9a-f-]{36}$/);

    // List i znajdź
    const list = await adminApi.get('/api/admin/promocodes');
    expect(list.ok()).toBeTruthy();
    const items = (await list.json()) as Array<{ code: string; isActive: boolean }>;
    expect(items.some((p) => p.code === code)).toBe(true);
  });

  test('TC-A037 Admin tworzy PercentOff code z duration', async ({ adminApi }) => {
    const code = `E2E-PCT-${UNIQUE()}`;
    const res = await adminApi.post('/api/admin/promocodes', {
      data: {
        code,
        kind: 1, // Influencer
        discountType: 1, // PercentOff
        discountValue: 20.0,
        durationMonths: 3,
        maxTotalUses: null,
        maxUsesPerTenant: 1,
        validFrom: null,
        validUntil: null,
        appliesTo: 2, // Both
        metadata: null,
      },
    });
    expect(res.ok()).toBeTruthy();
  });

  test('TC-A038 Tworzenie duplikatu kodu rzuca błąd', async ({ adminApi }) => {
    const code = `E2E-DUP-${UNIQUE()}`;
    const first = await adminApi.post('/api/admin/promocodes', {
      data: {
        code,
        kind: 0,
        discountType: 0,
        discountValue: 49.0,
        durationMonths: null,
        maxTotalUses: null,
        maxUsesPerTenant: 1,
        validFrom: null,
        validUntil: null,
        appliesTo: 0,
        metadata: null,
      },
    });
    expect(first.ok()).toBeTruthy();

    const dup = await adminApi.post('/api/admin/promocodes', {
      data: {
        code,
        kind: 0,
        discountType: 0,
        discountValue: 39.0,
        durationMonths: null,
        maxTotalUses: null,
        maxUsesPerTenant: 1,
        validFrom: null,
        validUntil: null,
        appliesTo: 0,
        metadata: null,
      },
      failOnStatusCode: false,
    });
    // Duplicate kod → handler rzuca InvalidOperationException → GlobalFallback
    // mapuje na 500 (TODO: dodać mapping na 409). Nie-200 wystarczy do E2E.
    expect(dup.status()).toBeGreaterThanOrEqual(400);
  });

  test('TC-A039 Admin deaktywuje kod — staje się invalid w public validate', async ({
    adminApi,
    request,
  }) => {
    const code = `E2E-DEACT-${UNIQUE()}`;
    // 1) create
    const created = await adminApi.post('/api/admin/promocodes', {
      data: {
        code,
        kind: 0,
        discountType: 0,
        discountValue: 49.0,
        durationMonths: null,
        maxTotalUses: null,
        maxUsesPerTenant: 1,
        validFrom: null,
        validUntil: null,
        appliesTo: 0,
        metadata: null,
      },
    });
    expect(created.ok()).toBeTruthy();
    const id = await created.json();

    // 2) public validate przed deactivate → isValid=true
    const beforeDeact = await request.post(
      `${process.env.E2E_API_URL ?? 'http://localhost:5199'}/api/promo/validate`,
      { data: { code }, failOnStatusCode: false },
    );
    expect(beforeDeact.ok()).toBeTruthy();
    const before = (await beforeDeact.json()) as { isValid: boolean };
    expect(before.isValid).toBe(true);

    // 3) deactivate
    const deact = await adminApi.post(`/api/admin/promocodes/${id}/deactivate`);
    expect([200, 204]).toContain(deact.status());

    // 4) public validate po deactivate → isValid=false
    const afterDeact = await request.post(
      `${process.env.E2E_API_URL ?? 'http://localhost:5199'}/api/promo/validate`,
      { data: { code }, failOnStatusCode: false },
    );
    expect(afterDeact.ok()).toBeTruthy();
    const after = (await afterDeact.json()) as { isValid: boolean };
    expect(after.isValid).toBe(false);
  });

  test('TC-A040 PATCH validity — wygasły kod jest invalid', async ({ adminApi, request }) => {
    const code = `E2E-VAL-${UNIQUE()}`;
    const created = await adminApi.post('/api/admin/promocodes', {
      data: {
        code,
        kind: 0,
        discountType: 0,
        discountValue: 49.0,
        durationMonths: null,
        maxTotalUses: null,
        maxUsesPerTenant: 1,
        validFrom: null,
        validUntil: null,
        appliesTo: 0,
        metadata: null,
      },
    });
    expect(created.ok()).toBeTruthy();
    const id = await created.json();

    // Ustaw validUntil w przeszłości
    const past = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
    const patch = await adminApi.patch(`/api/admin/promocodes/${id}/validity`, {
      data: { validUntil: past },
    });
    expect([200, 204]).toContain(patch.status());

    const valid = await request.post(
      `${process.env.E2E_API_URL ?? 'http://localhost:5199'}/api/promo/validate`,
      { data: { code }, failOnStatusCode: false },
    );
    const body = (await valid.json()) as { isValid: boolean };
    expect(body.isValid).toBe(false);
  });
});

test.describe('Public — promo validate @p0 @billing @promo', () => {
  test('TC-A036b Public validate happy path', async ({ request, adminApi }) => {
    const code = `E2E-PUB-${UNIQUE()}`;
    await adminApi.post('/api/admin/promocodes', {
      data: {
        code,
        kind: 0,
        discountType: 0,
        discountValue: 49.0,
        durationMonths: null,
        maxTotalUses: null,
        maxUsesPerTenant: 1,
        validFrom: null,
        validUntil: null,
        appliesTo: 0,
        metadata: null,
      },
    });

    const res = await request.post(
      `${process.env.E2E_API_URL ?? 'http://localhost:5199'}/api/promo/validate`,
      { data: { code } },
    );
    expect(res.ok()).toBeTruthy();
    const body = (await res.json()) as {
      isValid: boolean;
      discountPreview: string | null;
      message: string | null;
    };
    expect(body.isValid).toBe(true);
    expect(body.discountPreview).toBeTruthy();
    expect(body.discountPreview).toContain('49');
  });

  test('TC-A038b Public validate — nieistniejący kod', async ({ request }) => {
    const res = await request.post(
      `${process.env.E2E_API_URL ?? 'http://localhost:5199'}/api/promo/validate`,
      { data: { code: `NOPE-${UNIQUE()}` } },
    );
    expect(res.ok()).toBeTruthy();
    const body = (await res.json()) as { isValid: boolean; message: string | null };
    expect(body.isValid).toBe(false);
    expect(body.message).toMatch(/nieprawidłow/i);
  });

  test('TC-A038c Public validate — pusty kod', async ({ request }) => {
    const res = await request.post(
      `${process.env.E2E_API_URL ?? 'http://localhost:5199'}/api/promo/validate`,
      { data: { code: '' } },
    );
    expect(res.ok()).toBeTruthy();
    const body = (await res.json()) as { isValid: boolean };
    expect(body.isValid).toBe(false);
  });

  test('TC-A038d Public validate — normalizacja (case-insensitive, trim)', async ({
    adminApi,
    request,
  }) => {
    const code = `E2E-CASE-${UNIQUE()}`;
    await adminApi.post('/api/admin/promocodes', {
      data: {
        code,
        kind: 0,
        discountType: 0,
        discountValue: 49.0,
        durationMonths: null,
        maxTotalUses: null,
        maxUsesPerTenant: 1,
        validFrom: null,
        validUntil: null,
        appliesTo: 0,
        metadata: null,
      },
    });

    const lower = code.toLowerCase();
    const withSpaces = `  ${lower}  `;
    const res = await request.post(
      `${process.env.E2E_API_URL ?? 'http://localhost:5199'}/api/promo/validate`,
      { data: { code: withSpaces } },
    );
    expect(res.ok()).toBeTruthy();
    const body = (await res.json()) as { isValid: boolean };
    expect(body.isValid).toBe(true);
  });
});

test.describe('Admin — promo permissions @p1 @billing @promo @security', () => {
  test('TC-A040b Owner nie ma dostępu do /api/admin/promocodes', async ({ ownerApi }) => {
    const res = await ownerApi.get('/api/admin/promocodes', { failOnStatusCode: false });
    // SystemAdminOnly policy — Owner dostaje 403
    expect([401, 403]).toContain(res.status());
  });

  test('TC-A040c Anon nie ma dostępu do /api/admin/promocodes', async ({ request }) => {
    const res = await request.get(
      `${process.env.E2E_API_URL ?? 'http://localhost:5199'}/api/admin/promocodes`,
      { failOnStatusCode: false },
    );
    expect([401, 403]).toContain(res.status());
  });
});
