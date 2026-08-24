import { test as base } from '@playwright/test';
import { E2eApi } from '../helpers/api-client';

/**
 * Fixture do kontroli czasu via FakeTimeProvider w backendzie (env E2E).
 *
 * Użycie:
 * ```ts
 * test('hold lease expires after TTL', async ({ clock, ... }) => {
 *   // ... ustaw hold ...
 *   await clock.advance(70);  // przeskakujemy 70s = poza HoldTtl (60s default, 5s w E2E)
 *   // ... assert że slot jest wolny
 * });
 * ```
 */
export const test = base.extend<{
  clock: {
    advance: (seconds: number) => Promise<void>;
  };
}>({
  clock: async ({ request }, use) => {
    const api = new E2eApi(request);
    await use({
      advance: (seconds: number) => api.advanceTime(seconds),
    });
  },
});

export { expect } from '@playwright/test';
