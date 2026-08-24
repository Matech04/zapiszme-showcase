import type { Locator, Page } from '@playwright/test';
import { LoginPage } from '../pages/dashboard/LoginPage';
import type { E2eApi } from './api-client';

/**
 * Loguje ownera w UI dashboardu. Hasło seedowanego ownera nie jest znane testowi,
 * więc najpierw przechodzimy flow reset hasła (forgot-password → link z mailbox → ustaw
 * znane hasło), a potem logujemy się tym hasłem. Idempotentne — kolejne wywołanie
 * ponownie zresetuje hasło na to samo i zaloguje.
 *
 * Wzorzec wyciągnięty z shift-templates.spec.ts żeby nie duplikować w kolejnych specach.
 */
export async function loginAsOwner(page: Page, api: E2eApi, ownerEmail: string): Promise<void> {
  await page.goto('/forgot-password');
  await page.getByTestId('forgot-email').fill(ownerEmail);
  await page.getByTestId('forgot-submit').click();
  await page.waitForTimeout(800);
  const mail = await api.getLastAuthEmail(ownerEmail);
  if (mail.lastPasswordResetUrl) {
    await page.goto(mail.lastPasswordResetUrl);
    await page.getByTestId('reset-password-input').fill('Password123!');
    await page.getByTestId('reset-password-submit').click();
    await page.waitForTimeout(800);
  }
  await new LoginPage(page).login(ownerEmail, 'Password123!');
  await page.waitForURL(/\/admin/, { timeout: 15_000 }).catch(() => {});
}

/**
 * Wyzwala natywny HTML5 Drag & Drop między dwoma elementami (przekazanymi jako Locatory).
 *
 * Playwright `locator.dragTo()` używa mouse-emulacji, która NIE wypełnia
 * `DataTransfer` — a `drag-reorder.directive.ts` czyta `group:index` właśnie z
 * `dataTransfer.getData('text/plain')`. Dlatego ręcznie dispatchujemy
 * dragstart → dragover → drop → dragend ze WSPÓLNYM obiektem DataTransfer, tak by
 * payload zapisany w `dragstart` był odczytywalny w `drop`.
 *
 * Elementy rozwiązujemy po stronie Playwrighta (Locator → elementHandle), żeby działały
 * dowolne selektory (`:has-text`, getByTestId itp.) — `document.querySelector` w evaluate
 * obsługuje tylko czysty CSS.
 */
/**
 * Przeciąga element CDK Drag&Drop (`@angular/cdk/drag-drop`) realnym kursorem.
 *
 * CDK NIE używa natywnego HTML5 DnD — nasłuchuje zdarzeń pointer/mouse (mousedown →
 * mousemove → mouseup). Dlatego `nativeDragAndDrop` (syntetyczne DragEvent) tu nie zadziała.
 * Realne przeciągnięcie: hover na UCHWYCIE źródła (`cdkDragHandle`) → `mouse.down()` →
 * kilka kroków `mouse.move()` w stronę celu (CDK wymaga progu ruchu + kilku update'ów, by
 * rozpoznać drag i przeliczyć pozycję docelową) → `mouse.up()`.
 *
 * @param sourceHandle uchwyt elementu źródłowego (np. `[data-testid="...-drag-handle"]`)
 * @param target element docelowy — jego środek wyznacza miejsce upuszczenia
 */
export async function cdkDragAndDrop(
  page: Page,
  sourceHandle: Locator,
  target: Locator,
): Promise<void> {
  // Przy długich listach element może być daleko poza viewportem (boundingBox z ujemnym Y).
  // `page.mouse` operuje we współrzędnych viewportu, więc najpierw przewijamy źródło do widoku.
  await sourceHandle.scrollIntoViewIfNeeded();
  const srcBox = await sourceHandle.boundingBox();
  const dstBox = await target.boundingBox();
  if (!srcBox || !dstBox) {
    throw new Error('cdkDragAndDrop: nie udało się rozwiązać boundingBox source/target');
  }

  const startX = srcBox.x + srcBox.width / 2;
  const startY = srcBox.y + srcBox.height / 2;
  const draggingDown = dstBox.y > srcBox.y;
  const endX = dstBox.x + dstBox.width / 2;
  // CDK zamienia elementy, gdy kursor PRZEKRACZA środek sąsiada. Aby A trafiło ZA B
  // (drag w dół), celujemy poniżej środka B; dla draga w górę — powyżej środka B.
  const endY = draggingDown
    ? dstBox.y + dstBox.height * 0.85
    : dstBox.y + dstBox.height * 0.15;

  await sourceHandle.hover();
  await page.mouse.move(startX, startY);
  await page.mouse.down();

  // Pierwszy ruch przekracza próg startu draga w CDK i wyzwala `cdkDragStarted`.
  await page.mouse.move(startX, startY + (draggingDown ? 12 : -12), { steps: 5 });
  // Następnie w wielu krokach do celu — CDK przelicza sort przy każdym ruchu.
  const steps = 20;
  for (let i = 1; i <= steps; i++) {
    const x = startX + ((endX - startX) * i) / steps;
    const y = startY + ((endY - startY) * i) / steps;
    await page.mouse.move(x, y, { steps: 2 });
  }
  // Krótki postój nad celem, by CDK ustabilizował preview/placeholder przed upuszczeniem.
  await page.mouse.move(endX, endY, { steps: 5 });
  await page.waitForTimeout(150);
  await page.mouse.up();
}

export async function nativeDragAndDrop(page: Page, source: Locator, target: Locator): Promise<void> {
  const sourceHandle = await source.elementHandle();
  const targetHandle = await target.elementHandle();
  if (!sourceHandle || !targetHandle) {
    throw new Error('nativeDragAndDrop: nie udało się rozwiązać source/target elementHandle');
  }
  await page.evaluate(
    ({ source, target }) => {
      const dataTransfer = new DataTransfer();
      // WAŻNE: `bubbles: false`. Drag-itemy w katalogu usług są zagnieżdżone
      // (`category-drag-item` group=`categories` opakowuje `category-service-drag-item`
      // group=`category-services-{id}`). Każdy poziom ma własny host-listener
      // `(dragstart)`. Przy `bubbles: true` syntetyczny `dragstart` z elementu usługi
      // BĄBELKUJE do rodzica-kategorii, którego `onDragStart` NADPISUJE
      // `dataTransfer.setData(...)` payloadem grupy `categories` → drop trafia w złą grupę
      // i reorder usług się nie wykonuje. Realny browser nie ma tego problemu (jedno źródło).
      // Listenery są bindowane bezpośrednio na hoście, więc event NIE-bąbelkujący
      // dispatchowany WPROST na element nadal wyzwala jego handler.
      const fire = (el: Element, type: string) => {
        const rect = el.getBoundingClientRect();
        const ev = new DragEvent(type, {
          bubbles: false,
          cancelable: true,
          composed: true,
          clientX: rect.left + rect.width / 2,
          clientY: rect.top + rect.height / 2,
        });
        // Chromium nie ustawia dataTransfer w konstruktorze DragEvent — wstrzykujemy ręcznie.
        Object.defineProperty(ev, 'dataTransfer', { value: dataTransfer });
        el.dispatchEvent(ev);
      };
      fire(source, 'dragstart');
      fire(target, 'dragover');
      fire(target, 'drop');
      fire(source, 'dragend');
    },
    { source: sourceHandle, target: targetHandle },
  );
  await sourceHandle.dispose();
  await targetHandle.dispose();
}
