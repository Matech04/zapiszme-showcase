import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppointmentConfirmationMode } from '@core/api/api-client';
import { OnboardingRulesStepComponent } from './rules-step.component';
import { OnboardingWizardStore } from '../onboarding-wizard.store';
import { ONBOARDING_STEPS } from '../onboarding-steps';

/**
 * Krok „Jak przyjmujesz zapisy?" stoi przed parą „Terminy + Godziny", bo grafik ma być ostatnią
 * rzeczą w kreatorze — z niego wychodzi się prosto do aplikacji. Ta zmiana kolejności ma jeden
 * nieoczywisty skutek: krok NIE MOŻE już wołać `complete()`, bo ta komenda domyka onboarding,
 * a domknięty onboarding każe `setupGuard` odesłać właścicielkę z `/setup/**` do panelu.
 */
describe('OnboardingRulesStepComponent — zasady przed grafikiem', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
    globalThis.localStorage?.clear();
  });

  const setup = () => {
    const router = { navigate: vi.fn() };
    TestBed.configureTestingModule({
      imports: [OnboardingRulesStepComponent],
      providers: [OnboardingWizardStore, { provide: Router, useValue: router }],
    });

    const fixture = TestBed.createComponent(OnboardingRulesStepComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, router };
  };

  it('kolejność kroków: zapisy przed terminami i godzinami', () => {
    const order = ONBOARDING_STEPS.map((s) => s.path);
    expect(order.indexOf('rules')).toBeLessThan(order.indexOf('slot-mode'));
    expect(order.indexOf('slot-mode')).toBeLessThan(order.indexOf('schedule'));
    // Grafik jest ostatnim krokiem treściowym — po nim tylko ekran „Gotowe".
    expect(order.at(-1)).toBe('done');
    expect(order.at(-2)).toBe('schedule');
  });

  it('„Dalej" prowadzi do terminów, nie do „Gotowe"', () => {
    const { component, router } = setup();

    component['onNext']();

    expect(router.navigate).toHaveBeenCalledWith(['/setup/slot-mode']);
  });

  it('nie domyka onboardingu — krok nie ma czym wołać backendu', () => {
    // Gdyby ktoś wrócił tu z `complete()`, kreator kończyłby się na piątym z ośmiu kroków, a
    // właścicielka nigdy nie zobaczyłaby ani „Terminów", ani „Godzin". Brak `OnboardingClient`
    // wśród zależności komponentu jest tu zabezpieczeniem: TestBed nie dostaje takiego providera,
    // więc ponowne wstrzyknięcie klienta wywali ten test na etapie tworzenia komponentu.
    expect(() => setup()).not.toThrow();
  });

  it('wybór trafia do bufora kreatora i przeżywa odświeżenie', () => {
    const { component } = setup();
    const store = TestBed.inject(OnboardingWizardStore);

    component['select'](AppointmentConfirmationMode.Manual);
    component['onNext']();

    expect(store.confirmationMode()).toBe(AppointmentConfirmationMode.Manual);

    // Nowa instancja store czyta draft z localStorage — tak samo jak po F5 w trakcie kreatora.
    // Bez tego wybór z kroku 5 przepadłby, zanim krok 7 zdąży go wysłać.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [OnboardingWizardStore] });
    expect(TestBed.inject(OnboardingWizardStore).confirmationMode()).toBe(
      AppointmentConfirmationMode.Manual,
    );
  });
});
