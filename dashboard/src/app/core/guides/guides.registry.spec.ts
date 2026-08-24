import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { GUIDES, guidesForRole, guidesForRoute } from './guides.registry';
import { GuideStep } from './guide.types';

/**
 * Strażnik integralności rejestru przewodników.
 *
 * Powód istnienia jest konkretny: poprzednie przewodniki wskazywały na `[data-tour="services-hero"]`
 * i `[data-tour="customers-hero"]` jeszcze długo po tym, jak nagłówki hero zniknęły z panelu.
 * Silnik po cichu pomijał te kroki, więc dwa przewodniki chodziły okrojone przez wiele wydań
 * i nikt tego nie zauważył. Ten test zamienia taką cichą awarię w czerwony build.
 */

const SOURCE_ROOT = join(process.cwd(), 'src', 'app');

function collectSourceFiles(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      collectSourceFiles(full, acc);
    } else if (full.endsWith('.ts') || full.endsWith('.html')) {
      acc.push(full);
    }
  }
  return acc;
}

/** Cała treść źródeł panelu z pominięciem samych definicji przewodników — inaczej rejestr
 *  potwierdzałby sam siebie. */
const sourceText = collectSourceFiles(SOURCE_ROOT)
  .filter((file) => !file.includes(join('core', 'guides')))
  .map((file) => readFileSync(file, 'utf8'))
  .join('\n');

/** Wszystkie selektory, na których opierają się kroki (kotwica + wyzwalacz akcji). */
function selectorsOf(step: GuideStep): string[] {
  const selectors: string[] = [];
  if (step.element) selectors.push(step.element);

  const trigger = step.advanceOn;
  if (trigger?.on === 'click' && trigger.selector) selectors.push(trigger.selector);
  if (trigger?.on === 'appear' || trigger?.on === 'disappear') selectors.push(trigger.selector);

  return selectors;
}

describe('rejestr przewodników — integralność', () => {
  it('każda kotwica istnieje w źródłach panelu', () => {
    const missing: string[] = [];

    for (const guide of GUIDES) {
      for (const [index, step] of guide.steps.entries()) {
        for (const selector of selectorsOf(step)) {
          const attribute = selector.match(/^\[data-tour="([^"]+)"\]$/);
          expect(
            attribute,
            `${guide.id} krok ${index}: kotwice przewodników muszą używać [data-tour="…"], było "${selector}"`,
          ).not.toBeNull();

          if (attribute && !sourceText.includes(`data-tour="${attribute[1]}"`)) {
            missing.push(`${guide.id} krok ${index} → ${selector}`);
          }
        }
      }
    }

    expect(missing, `Kotwice bez odpowiednika w kodzie:\n${missing.join('\n')}`).toEqual([]);
  });

  it('identyfikatory są unikalne i w formacie akceptowanym przez backend', () => {
    const ids = GUIDES.map((g) => g.id);
    expect(new Set(ids).size, 'zduplikowane id przewodnika').toBe(ids.length);

    // Musi przejść GuideIdRules.Pattern po stronie API, inaczej zapis „ukończono" wróci z 400.
    for (const id of ids) {
      expect(id, `id "${id}" nie jest kebab-case`).toMatch(/^[a-z0-9]+(-[a-z0-9]+)*$/);
      expect(id.length).toBeLessThanOrEqual(64);
    }
  });

  it('każdy krok zadaniowy ma zdefiniowany wyzwalacz', () => {
    for (const guide of GUIDES) {
      for (const [index, step] of guide.steps.entries()) {
        if (step.kind === 'action') {
          expect(step.advanceOn, `${guide.id} krok ${index}: krok action bez advanceOn nigdy się nie zakończy`)
            .toBeDefined();
        }
      }
    }
  });

  it('każdy przewodnik ma role i trasę wejściową', () => {
    for (const guide of GUIDES) {
      expect(guide.roles.length, `${guide.id}: pusta lista ról = przewodnik niewidoczny dla nikogo`)
        .toBeGreaterThan(0);
      expect(guide.entryRoute.startsWith('/admin/'), `${guide.id}: entryRoute musi być pod /admin/`)
        .toBe(true);
    }
  });

  /**
   * Literówka w trasie nie wywala niczego głośno — przewodnik po prostu nigdy nie pokaże się
   * pod przyciskiem „?", a jego pierwszy krok zawiedzie dopiero u użytkownika. Sprawdzamy więc
   * każdy segment tras (wejściowej i krokowych) wobec `app.routes.ts`.
   */
  it('trasy przewodników wskazują na istniejące ścieżki panelu', () => {
    const routesText = readFileSync(join(SOURCE_ROOT, 'app.routes.ts'), 'utf8');

    // Trasy potomne bywają zapisane jednym literałem („my-availability/:id/schedules"), więc
    // zbieramy pojedyncze segmenty ze WSZYSTKICH literałów `path`, zamiast szukać całych ścieżek.
    const knownSegments = new Set(
      [...routesText.matchAll(/path:\s*'([^']*)'/g)]
        .flatMap((match) => match[1].split('/'))
        .filter(Boolean),
    );

    const unknown: string[] = [];

    const check = (route: string, where: string) => {
      for (const segment of route.replace('/admin/', '').split('/')) {
        // Tokeny (`:me`, `:id`) i puste segmenty pomijamy — to parametry, nie ścieżki.
        if (!segment || segment.startsWith(':')) continue;
        if (!knownSegments.has(segment)) unknown.push(`${where} → ${segment}`);
      }
    };

    for (const guide of GUIDES) {
      check(guide.entryRoute, `${guide.id} entryRoute`);
      for (const [index, step] of guide.steps.entries()) {
        if (step.route) check(step.route, `${guide.id} krok ${index}`);
        if (step.advanceOn?.on === 'route') check(step.advanceOn.route, `${guide.id} krok ${index} advanceOn`);
      }
    }

    expect(unknown, `Segmenty tras bez odpowiednika w app.routes.ts:\n${unknown.join('\n')}`).toEqual([]);
  });

  it('pracownik nie dostaje przewodników po ekranach, do których nie ma dostępu', () => {
    // Katalog usług chroni staffManagementGuard — przewodnik „Dodajmy usługę" odbiłby
    // pracownika od guarda w drugim kroku.
    const employeeGuides = guidesForRole('employee').map((g) => g.id);
    expect(employeeGuides).not.toContain('add-service');
    expect(employeeGuides).toContain('set-weekly-schedule');
  });

  it('dopasowanie po trasie uwzględnia parametry i rolę', () => {
    const onHub = guidesForRoute('/admin/my-availability/abc-123', 'owner').map((g) => g.id);
    expect(onHub).toContain('set-weekly-schedule');
    expect(onHub).toContain('set-special-day');

    // Trasa o innej długości nie może przypadkiem pasować do szablonu z tokenem.
    expect(guidesForRoute('/admin/my-availability/abc-123/schedules', 'owner')).toEqual([]);

    // Brak roli (sesja niezhydratyzowana) = brak przewodników, zamiast wycieku listy.
    expect(guidesForRoute('/admin/my-availability/abc-123', null)).toEqual([]);
  });
});
