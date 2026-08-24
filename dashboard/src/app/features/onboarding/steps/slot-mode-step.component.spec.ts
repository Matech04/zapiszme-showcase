import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SlotGenerationMode } from '@core/api/api-client';
import { OnboardingSlotModeStepComponent } from './slot-mode-step.component';
import { OnboardingWizardStore } from '../onboarding-wizard.store';

/**
 * Krok „Jak wyznaczasz terminy?" — jedyny w kreatorze, który pyta o rzecz ABSTRAKCYJNĄ: osoba
 * bez ani jednej przyjętej klientki ma rozstrzygnąć kształt swojego dnia pracy. Cały ciężar
 * decyzji niosą więc podglądy i przykład, i to je pilnują poniższe testy.
 */
describe('OnboardingSlotModeStepComponent — wyjaśnienie różnicy między trybami', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
    globalThis.localStorage?.clear();
  });

  const setup = () => {
    TestBed.configureTestingModule({
      imports: [OnboardingSlotModeStepComponent],
      providers: [OnboardingWizardStore, { provide: Router, useValue: { navigate: vi.fn() } }],
    });

    const fixture = TestBed.createComponent(OnboardingSlotModeStepComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance };
  };

  const cells = (fixture: ReturnType<typeof setup>['fixture'], card: 'grid' | 'fixed') =>
    Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
        `[data-testid="setup-slot-mode-${card}"] .grid-cols-4 > span`,
      ),
    );

  const exampleText = (fixture: ReturnType<typeof setup>['fixture']) =>
    (fixture.nativeElement as HTMLElement)
      .querySelector('[data-testid="setup-slot-mode-example"]')!
      .textContent!.replace(/\s+/g, ' ')
      .trim();

  it('oba podglądy rysują tę samą oś czasu', () => {
    // Sedno porównania: karty różnią się WYŁĄCZNIE liczbą zapalonych godzin. Gdy podglądy przestaną
    // pokazywać tę samą dobę (osobne tablice, inna liczba komórek), oko nie ma czego zestawić —
    // dokładnie tak było, gdy lewa karta miała siatkę, a prawa trzy pełne belki.
    const { fixture } = setup();

    const gridLabels = cells(fixture, 'grid').map((c) => c.textContent!.trim());
    const fixedLabels = cells(fixture, 'fixed').map((c) => c.textContent!.trim());

    expect(gridLabels).toEqual(fixedLabels);
    expect(gridLabels[0]).toBe('09:00');
    expect(gridLabels).toHaveLength(16);
  });

  it('tryb elastyczny zapala wszystkie godziny, stały tylko wyznaczone', () => {
    const { fixture } = setup();
    const lit = (card: 'grid' | 'fixed') =>
      cells(fixture, card)
        .filter((c) => c.classList.contains('bg-primary/25'))
        .map((c) => c.textContent!.trim());

    expect(lit('grid')).toHaveLength(16);
    expect(lit('fixed')).toEqual(['09:00', '10:30', '12:00']);
  });

  it('godziny z podglądu stałego leżą na wspólnej osi', () => {
    // Gdyby wypadły poza oś (jak dawne 14:00 przy siatce kończącej się o 12:45), prawa karta
    // pokazałaby siatkę bez ani jednej zapalonej komórki — czyli tryb, w którym nie da się zapisać.
    const { fixture } = setup();
    const lit = cells(fixture, 'fixed').filter((c) => c.classList.contains('bg-primary/25'));

    expect(lit.length).toBeGreaterThan(0);
  });

  it('przykład pod kartami zmienia się razem z wyborem', () => {
    const { fixture, component } = setup();

    expect(exampleText(fixture)).toContain('9:00, 9:15, 9:30');
    expect(exampleText(fixture)).toContain('Polecane');

    component['select'](SlotGenerationMode.FixedStartTimes);
    fixture.detectChanges();

    const fixedExample = exampleText(fixture);
    expect(fixedExample).toContain('9:00, 10:30 i 12:00');
    expect(fixedExample).toContain('następnym kroku');
  });

  it('przykład trybu stałego wymienia dokładnie te godziny, które zapala podgląd', () => {
    // Przykład i podgląd to dwa opisy tego samego — rozjazd („podgląd mówi 10:30, tekst 12:00")
    // jest gorszy niż brak przykładu.
    const { fixture, component } = setup();
    component['select'](SlotGenerationMode.FixedStartTimes);
    fixture.detectChanges();

    const example = exampleText(fixture);
    for (const hour of cells(fixture, 'fixed')
      .filter((c) => c.classList.contains('bg-primary/25'))
      .map((c) => c.textContent!.trim().replace(/^0/, ''))) {
      expect(example).toContain(hour);
    }
  });

  it('wybór trafia do store dopiero po „Dalej"', () => {
    const { fixture, component } = setup();
    const store = TestBed.inject(OnboardingWizardStore);

    component['select'](SlotGenerationMode.FixedStartTimes);
    expect(store.slotMode()).toBe(SlotGenerationMode.Grid);

    component['onNext']();
    expect(store.slotMode()).toBe(SlotGenerationMode.FixedStartTimes);
  });
});
