import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { of } from 'rxjs';
import {
  AppointmentConfirmationMode,
  OnboardingClient,
  SlotGenerationMode,
} from '@core/api/api-client';
import { OnboardingStateService } from '@core/auth/onboarding-state.service';
import { OnboardingScheduleStepComponent } from './schedule-step.component';
import { OnboardingWizardStore } from '../onboarding-wizard.store';

/**
 * Regresja: `startTimeModel`/`endTimeModel` były zwykłymi polami, a `sharedRangeValid` — `computed()`
 * nad nimi. Computed bez producentów-sygnałów liczy się RAZ i cache'uje wynik, więc walidacja
 * zamarzała na `true` z wartości startowych (09:00 < 17:00): komunikat o błędzie nigdy się nie
 * pokazywał, „Dalej" nigdy nie blokowało, a odwrócony zakres szedł do backendu.
 *
 * Te testy przechodzą przez publiczne zachowanie kroku (canSubmit + render + payload), więc
 * złapią zarówno powrót do zwykłych pól, jak i zgubione wywołanie sygnału w szablonie payloadu.
 */
describe('OnboardingScheduleStepComponent — walidacja zakresu godzin', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  const setup = () => {
    const client = {
      setSchedule: vi.fn().mockReturnValue(of(undefined)),
      // `complete()` domyka onboarding i od przeniesienia „Zapisów" przed grafik woła go WŁAŚNIE
      // ten krok — bez tego mocka `onNext` wpadał w catch, a asercje payloadu i tak przechodziły.
      complete: vi.fn().mockReturnValue(of({ tenantId: 't1', slug: 'salon' })),
    };
    const router = { navigate: vi.fn() };

    TestBed.configureTestingModule({
      imports: [OnboardingScheduleStepComponent],
      providers: [
        OnboardingWizardStore,
        { provide: OnboardingClient, useValue: client },
        {
          // `ensure()` woła konstruktor kroku, żeby odtworzyć wybór „na bieżąco" po F5.
          // Domyślnie: brak deklaracji → ścieżka grafiku powtarzalnego (te testy dotyczą właśnie jej).
          provide: OnboardingStateService,
          useValue: { markStale: vi.fn(), ensure: vi.fn().mockReturnValue(of(null)) },
        },
        { provide: Router, useValue: router },
      ],
    });

    const fixture = TestBed.createComponent(OnboardingScheduleStepComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, client, router };
  };

  it('domyślny zakres 09:00–17:00 jest poprawny i przepuszcza dalej', () => {
    const { component } = setup();
    expect(component['sharedRangeValid']()).toBe(true);
    expect(component['canSubmit']()).toBe(true);
  });

  it('odwrócony zakres unieważnia walidację i blokuje „Dalej”', () => {
    const { component } = setup();

    component['startTimeModel'].set('18:00');
    component['endTimeModel'].set('09:00');

    expect(component['sharedRangeValid']()).toBe(false);
    expect(component['canSubmit']()).toBe(false);
  });

  it('odwrócony zakres pokazuje komunikat o błędzie', () => {
    const { fixture, component } = setup();

    component['startTimeModel'].set('18:00');
    component['endTimeModel'].set('09:00');
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Godzina zakończenia musi być późniejsza niż rozpoczęcia.');
  });

  it('równe godziny są odrzucane (zerowy dzień pracy)', () => {
    const { component } = setup();

    component['startTimeModel'].set('09:00');
    component['endTimeModel'].set('09:00');

    expect(component['sharedRangeValid']()).toBe(false);
    expect(component['canSubmit']()).toBe(false);
  });

  it('onNext wysyła realnie wybrane godziny, nie wartości startowe', async () => {
    const { component, client } = setup();

    component['startTimeModel'].set('10:00');
    component['endTimeModel'].set('16:00');

    await component['onNext']();

    expect(client.setSchedule).toHaveBeenCalledTimes(1);
    const payload = client.setSchedule.mock.calls[0][0];
    // Godziny są teraz PER DZIEŃ — payload niesie listę dni, nie jeden wspólny zakres.
    expect(payload.days.every((d: { startTime: string }) => d.startTime === '10:00:00')).toBe(true);
    expect(payload.days.every((d: { endTime: string }) => d.endTime === '16:00:00')).toBe(true);
  });

  it('onNext nie strzela do API przy odwróconym zakresie', async () => {
    const { component, client } = setup();

    component['startTimeModel'].set('18:00');
    component['endTimeModel'].set('09:00');

    await component['onNext']();

    expect(client.setSchedule).not.toHaveBeenCalled();
    // Ani grafiku, ani domknięcia: niepoprawny formularz nie może zakończyć kreatora.
    expect(client.complete).not.toHaveBeenCalled();
  });

  /**
   * Grafik jest OSTATNIM krokiem treściowym, więc to on domyka onboarding i dowozi wybór z kroku
   * „Zapisy", który do tej pory czekał w buforze kreatora. Wcześniej `complete()` wołał krok zasad;
   * po przeniesieniu go przed grafik zrobiłby to w połowie kreatora i `setupGuard` wyrzuciłby
   * właścicielkę do panelu, zanim ustawiłaby terminy i godziny.
   */
  it('domyka onboarding wyborem zasad z bufora i prowadzi na „Gotowe"', async () => {
    const { component, client, router } = setup();
    TestBed.inject(OnboardingWizardStore).confirmationMode.set(
      AppointmentConfirmationMode.Manual,
    );

    await component['onNext']();

    expect(client.complete).toHaveBeenCalledWith({
      confirmationMode: AppointmentConfirmationMode.Manual,
    });
    expect(router.navigate).toHaveBeenCalledWith(['/setup/done']);
  });

  it('domknięcie idzie PO zapisie grafiku — nie odwrotnie', () => {
    // Kolejność ma znaczenie: `complete()` zapala `onboardingCompleted`, po którym guard przestaje
    // wpuszczać na `/setup/**`. Gdyby padło przed `setSchedule`, błąd zapisu grafiku zostawiłby
    // salon domknięty i BEZ godzin, czyli z publicznym linkiem bez ani jednego terminu.
    const { component, client } = setup();
    const order: string[] = [];
    client.setSchedule.mockImplementation(() => {
      order.push('setSchedule');
      return of(undefined);
    });
    client.complete.mockImplementation(() => {
      order.push('complete');
      return of({ tenantId: 't1', slug: 'salon' });
    });

    return component['onNext']().then(() => {
      expect(order).toEqual(['setSchedule', 'complete']);
    });
  });
});

/**
 * „Ustawiam dni na bieżąco" (papierowy kalendarz) — pytanie stoi NAD formularzem i go zwija.
 * Wybór trybu wyznaczania terminów z poprzedniego kroku musi lecieć TAKŻE tą ścieżką: jest
 * podpowiedzią startową dla każdego dnia specjalnego, który właścicielka doda później.
 */
describe('OnboardingScheduleStepComponent — grafik powtarzalny vs na bieżąco', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  const setup = (stateUsesAdHoc = false) => {
    const client = {
      setSchedule: vi.fn().mockReturnValue(of(undefined)),
      // `complete()` domyka onboarding i od przeniesienia „Zapisów" przed grafik woła go WŁAŚNIE
      // ten krok — bez tego mocka `onNext` wpadał w catch, a asercje payloadu i tak przechodziły.
      complete: vi.fn().mockReturnValue(of({ tenantId: 't1', slug: 'salon' })),
    };

    TestBed.configureTestingModule({
      imports: [OnboardingScheduleStepComponent],
      providers: [
        OnboardingWizardStore,
        { provide: OnboardingClient, useValue: client },
        {
          provide: OnboardingStateService,
          useValue: {
            markStale: vi.fn(),
            ensure: vi.fn().mockReturnValue(of({ usesAdHocSchedule: stateUsesAdHoc })),
          },
        },
        { provide: Router, useValue: { navigate: vi.fn() } },
      ],
    });

    const fixture = TestBed.createComponent(OnboardingScheduleStepComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, client };
  };

  it('domyślnie wybrany jest grafik powtarzalny', () => {
    const { component } = setup();
    expect(component['usesAdHoc']()).toBe(false);
  });

  it('„planuję każdy miesiąc osobno” zwija formularz dni i godzin', () => {
    const { fixture, component } = setup();

    component['setAdHoc'](true);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    // Formularz znika, ale musi go zastąpić instrukcja — inaczej kreator kończy się bez ani jednej
    // wskazówki, GDZIE wpisać dostępność, a kalendarz startuje pusty i wygląda na zepsuty.
    // Cytujemy dokładną etykietę przycisku z kalendarza, żeby dało się go znaleźć.
    expect(text).toContain('Ustaw godziny na ten dzień');
    expect(fixture.nativeElement.querySelector('[data-testid="setup-preset-weekdays"]')).toBeNull();
  });

  it('„planuję każdy miesiąc osobno” nie blokuje „Dalej” mimo braku dni', () => {
    const { component } = setup();

    component['selectedDays'].set([]);
    expect(component['canSubmit']()).toBe(false);

    component['setAdHoc'](true);
    expect(component['canSubmit']()).toBe(true);
  });

  it('„na bieżąco” wysyła useAdHoc=true i NIE gubi wybranego trybu terminów', async () => {
    const { component, client } = setup();
    const store = TestBed.inject(OnboardingWizardStore);
    store.slotMode.set(SlotGenerationMode.FixedStartTimes);

    component['setAdHoc'](true);
    await component['onNext']();

    expect(client.setSchedule).toHaveBeenCalledTimes(1);
    const payload = client.setSchedule.mock.calls[0][0];
    expect(payload.useAdHoc).toBe(true);
    // Tryb z poprzedniego kroku musi przetrwać — to podpowiedź dla każdego dnia specjalnego.
    expect(payload.slotMode).toBe(SlotGenerationMode.FixedStartTimes);
  });

  it('grafik powtarzalny wysyła useAdHoc=false wraz z dniami', async () => {
    const { component, client } = setup();

    await component['onNext']();

    const payload = client.setSchedule.mock.calls[0][0];
    expect(payload.useAdHoc).toBe(false);
    expect(payload.days.length).toBeGreaterThan(0);
  });

  it('deklaracja z backendu odtwarza wybór po F5', () => {
    const { component } = setup(true);
    expect(component['usesAdHoc']()).toBe(true);
  });
});

/**
 * Dwie naprawy zgłoszone z ręcznego testu:
 *
 * 1. Tryb stałych godzin pytał o Od/Do — front blokował „Dalej" do czasu wybrania poprawnego
 *    zakresu, a backend go WYRZUCAŁ (puste WorkRanges) i nawet nie walidował. Wymuszana wartość
 *    bez żadnego znaczenia.
 * 2. Kreator nie umiał różnych godzin w różne dni — jeden zakres szedł do WSZYSTKICH dni, więc
 *    właścicielka pracująca Pn 9–17 / Wt 10–18 kończyła konfigurację z BŁĘDNYM grafikiem
 *    i klientka rezerwowała wtorek na 9:00.
 */
describe('OnboardingScheduleStepComponent — tryb stały i godziny per dzień', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  const setup = (slotMode = SlotGenerationMode.Grid) => {
    const client = {
      setSchedule: vi.fn().mockReturnValue(of(undefined)),
      // `complete()` domyka onboarding i od przeniesienia „Zapisów" przed grafik woła go WŁAŚNIE
      // ten krok — bez tego mocka `onNext` wpadał w catch, a asercje payloadu i tak przechodziły.
      complete: vi.fn().mockReturnValue(of({ tenantId: 't1', slug: 'salon' })),
    };

    TestBed.configureTestingModule({
      imports: [OnboardingScheduleStepComponent],
      providers: [
        OnboardingWizardStore,
        { provide: OnboardingClient, useValue: client },
        {
          provide: OnboardingStateService,
          useValue: { markStale: vi.fn(), ensure: vi.fn().mockReturnValue(of(null)) },
        },
        { provide: Router, useValue: { navigate: vi.fn() } },
      ],
    });

    TestBed.inject(OnboardingWizardStore).slotMode.set(slotMode);
    const fixture = TestBed.createComponent(OnboardingScheduleStepComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, client };
  };

  it('tryb stały NIE pokazuje Od/Do — dzień nie ma okna pracy', () => {
    const { fixture } = setup(SlotGenerationMode.FixedStartTimes);
    expect(fixture.nativeElement.querySelector('[data-testid="setup-start-time"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="setup-end-time"]')).toBeNull();
  });

  it('tryb stały: odwrócony zakres Od/Do NIE blokuje „Dalej”', () => {
    const { component } = setup(SlotGenerationMode.FixedStartTimes);

    // Zakres jest w tym trybie bez znaczenia (backend go wyrzuca), więc nie może niczego blokować.
    component['startTimeModel'].set('18:00');
    component['endTimeModel'].set('09:00');
    component['fixedTimes'].set(['10:00']);

    expect(component['canSubmit']()).toBe(true);
  });

  it('tryb stały nadal wymaga co najmniej jednej godziny startu', () => {
    const { component } = setup(SlotGenerationMode.FixedStartTimes);
    expect(component['canSubmit']()).toBe(false);

    component['fixedTimes'].set(['10:00']);
    expect(component['canSubmit']()).toBe(true);
  });

  it('tryb stały wysyła dni BEZ godzin', async () => {
    const { component, client } = setup(SlotGenerationMode.FixedStartTimes);
    component['fixedTimes'].set(['10:00', '12:00']);

    await component['onNext']();

    const payload = client.setSchedule.mock.calls[0][0];
    expect(payload.days.every((d: { startTime?: string }) => d.startTime === undefined)).toBe(true);
    expect(payload.fixedStartTimes).toEqual(['10:00:00', '12:00:00']);
  });

  it('godziny per dzień: wtorek trzyma SWOJE godziny, nie poniedziałkowe', async () => {
    const { component, client } = setup();

    component['setPerDayHours'](true);
    component['setDayStart'](2, '10:00'); // Wt
    component['setDayEnd'](2, '18:00');

    await component['onNext']();

    const payload = client.setSchedule.mock.calls[0][0];
    const monday = payload.days.find((d: { day: number }) => d.day === 1);
    const tuesday = payload.days.find((d: { day: number }) => d.day === 2);

    expect(monday.startTime).toBe('09:00:00');
    expect(tuesday.startTime).toBe('10:00:00');
    expect(tuesday.endTime).toBe('18:00:00');
  });

  it('godziny per dzień: odwrócony zakres w JEDNYM dniu blokuje „Dalej”', () => {
    const { component } = setup();

    component['setPerDayHours'](true);
    expect(component['canSubmit']()).toBe(true);

    component['setDayStart'](3, '18:00'); // Śr
    component['setDayEnd'](3, '09:00');

    expect(component['canSubmit']()).toBe(false);
  });

  it('preset czyści nadpisania per dzień — inaczej cicho zostałyby stare godziny', () => {
    const { component } = setup();

    component['setPerDayHours'](true);
    component['setDayStart'](2, '10:00');
    component['applyPreset']('weekdays');

    expect(component['perDayHours']()).toBe(false);
    expect(component['hoursFor'](2).start).toBe('09:00');
  });
});
