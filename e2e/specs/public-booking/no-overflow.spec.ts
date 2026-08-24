import { test, expect } from '../../fixtures/seeded-tenant.fixture';

/**
 * Public booking + landing — brak poziomego overflow na mobile.
 *
 * Mobile-first: aplikacja NIGDY nie powinna mieć poziomego paska przewijania
 * na wąskim ekranie. Test wchodzi na każdą publiczną stronę przy viewport 320px
 * (iPhone SE / małe Androidy — najwęższy realny ekran) i failuje, jeśli
 * `documentElement.scrollWidth > clientWidth`. W komunikacie błędu wypisuje
 * KONKRETNE elementy, które wystają, żeby naprawa była natychmiastowa.
 *
 * Globalny guard żyje w web/src/styles/global.css (@layer base). Ten test
 * pilnuje, żeby guard + dyscyplina komponentów faktycznie trzymały.
 *
 * baseURL = chromium-web (WEB_URL) z playwright.config.
 */

const SLUG = 'rest-api-integration';
const WEB_URL = process.env.E2E_WEB_URL ?? 'http://localhost:4321';

// 320px = najwęższy realny ekran, który chcemy wspierać.
test.use({ viewport: { width: 320, height: 800 } });

/**
 * Zwraca listę elementów, których prawa krawędź wychodzi poza viewport.
 *
 * UWAGA — dlaczego NIE sprawdzamy `documentElement.scrollWidth`:
 * globalny guard (`body { overflow-x: clip }` w global.css) przycina overflow,
 * więc `scrollWidth` NIGDY nie przekroczy `clientWidth` — asercja na nim byłaby
 * zawsze zielona, nawet gdy element jest ucięty. Dlatego mierzymy per-element
 * `getBoundingClientRect().right`, które widzi wystający element MIMO clipa.
 *
 * Wykluczenia (zamierzony / nieszkodliwy „overflow"):
 *  - elementy wewnątrz kontenera `overflow-x: auto|scroll` (np. karuzela dat,
 *    owinięte tabele) — tam poziomy scroll jest celowy,
 *  - dekoracje `pointer-events: none` pozycjonowane absolute/fixed (poświaty,
 *    blur-bloby) — mają bleedować poza ekran i być przycięte; nie dotykalne,
 *    nie psują UX.
 */
async function findOverflowingElements(page: import('@playwright/test').Page) {
  return page.evaluate(() => {
    const docWidth = document.documentElement.clientWidth;
    const offenders: { tag: string; cls: string; right: number; text: string }[] = [];

    const isAllowedOverflow = (el: Element): boolean => {
      const cs = getComputedStyle(el);
      // dekoracja: nieklikalna + wyrwana z flow → ma bleedować i być przycięta
      if (cs.pointerEvents === 'none' && (cs.position === 'absolute' || cs.position === 'fixed')) {
        return true;
      }
      // wewnątrz celowego kontenera ze scrollem poziomym
      let node: Element | null = el.parentElement;
      while (node) {
        const ox = getComputedStyle(node).overflowX;
        if (ox === 'auto' || ox === 'scroll') return true;
        node = node.parentElement;
      }
      return false;
    };

    for (const el of Array.from(document.body.querySelectorAll('*'))) {
      const rect = el.getBoundingClientRect();
      // prawa krawędź wychodzi poza viewport (tolerancja 1px na zaokrąglenia)
      if (rect.right > docWidth + 1 && rect.width > 0 && !isAllowedOverflow(el)) {
        offenders.push({
          tag: el.tagName.toLowerCase(),
          cls: (el.getAttribute('class') ?? '').slice(0, 120),
          right: Math.round(rect.right),
          text: (el.textContent ?? '').trim().slice(0, 40),
        });
      }
    }
    return { docWidth, offenders: offenders.slice(0, 15) };
  });
}

async function expectNoOverflow(page: import('@playwright/test').Page, label: string) {
  const { docWidth, offenders } = await findOverflowingElements(page);
  const report = offenders
    .map((o) => `  <${o.tag} class="${o.cls}"> right=${o.right}px (viewport=${docWidth}) "${o.text}"`)
    .join('\n');
  expect(
    offenders,
    `${label}: ${offenders.length} element(ów) wychodzi poza viewport ${docWidth}px. ` +
      `Napraw źródło (sztywna szerokość / brak min-w-0 / nierozdzielny string) ` +
      `albo owiń w overflow-x-auto, jeśli scroll jest zamierzony:\n${report}`,
  ).toEqual([]);
}

test.describe('Mobile — brak poziomego overflow @p0 @public-booking', () => {
  const staticPages = [
    { path: '/', label: 'Landing marketingowy (index)' },
    { path: '/regulamin', label: 'Regulamin' },
    { path: '/polityka-prywatnosci', label: 'Polityka prywatności' },
  ];

  for (const { path, label } of staticPages) {
    test(`${label} — brak overflow @320px`, async ({ page }) => {
      await page.goto(`${WEB_URL}${path}`);
      await page.waitForLoadState('networkidle', { timeout: 10_000 }).catch(() => {});
      await expectNoOverflow(page, label);
    });
  }

  test('Strona rezerwacji /[slug] — brak overflow @320px', async ({ page, seededTenant }) => {
    await page.goto(`${WEB_URL}/${SLUG}`);
    await expect(page.getByTestId('booking-entry-mode-toggle')).toBeVisible({ timeout: 10_000 });
    await expectNoOverflow(page, 'Booking /[slug] — wybór trybu');
  });

  test('Zarządzanie wizytą (tab Manage) — brak overflow @320px', async ({ page, seededTenant }) => {
    await page.goto(`${WEB_URL}/${SLUG}`);
    await page.getByTestId('booking-entry-mode-manage').click();
    await expect(page.getByTestId('manage-contact-email')).toBeVisible({ timeout: 10_000 });
    await expectNoOverflow(page, 'Booking /[slug] — Manage');
  });
});
