import { test as base } from '@playwright/test';
import { E2eApi, type E2eSeedResult } from '../helpers/api-client';

/**
 * Fixture seedująca świeży (lub reuse'owany) tenant w backendzie przez backdoor.
 * Test dostaje obiekt `seededTenant` z ID-kami + e-mailem ownera.
 *
 * Aktualnie seed jest idempotentny (zwraca istniejący tenant jeśli już jest) —
 * dlatego NIE robimy cleanup po teście; testy współdzielą jedno tenant per uruchomienie.
 * W przyszłości można dodać per-test slug + cleanup endpoint dla pełnej izolacji.
 */
export const test = base.extend<{
  api: E2eApi;
  seededTenant: E2eSeedResult;
}>({
  api: async ({ request }, use) => {
    await use(new E2eApi(request));
  },
  seededTenant: async ({ api }, use) => {
    const seed = await api.seed();
    await use(seed);
  },
});

export { expect } from '@playwright/test';
