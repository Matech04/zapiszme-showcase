import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { of, throwError } from 'rxjs';
import { AppointmentsClient, OnboardingStateDto } from '@core/api/api-client';
import { MessageService } from 'primeng/api';
import { AuthSessionService } from '@core/auth/auth-session.service';
import { OnboardingStateService } from '@core/auth/onboarding-state.service';
import { GuideProgressService } from '@core/guides/guide-progress.service';
import { GuideService } from '@core/guides/guide.service';
import { environment } from '@env/environment';
import { StartHereCardComponent } from './start-here-card.component';

/**
 * Karta zastąpiła checklistę pierwszych kroków. Testujemy to, co w tamtej było zepsute (warunek
 * znikania) oraz to, po co powstała: wybór z kreatora ma zmieniać podsuwane przewodniki.
 */
describe('StartHereCardComponent', () => {
  let hasAnyAppointment: ReturnType<typeof vi.fn>;
  let start: ReturnType<typeof vi.fn>;
  let stateSignal: ReturnType<typeof signal<OnboardingStateDto | null>>;
  let completedSignal: ReturnType<typeof signal<ReadonlySet<string>>>;
  let loadedSignal: ReturnType<typeof signal<boolean>>;
  let addMessage: ReturnType<typeof vi.fn>;

  /**
   * `await` jest konieczny: `rxResource` oddaje `defaultValue` (true = „są wizyty") i dopiero po
   * mikrozadaniu podmienia je na wynik strumienia. Bez czekania testy przechodziłyby na wartości
   * domyślnej — także te sprawdzające, że czegoś nie ma.
   */
  const setup = async (hasTeam = false) => {
    TestBed.configureTestingModule({
      imports: [StartHereCardComponent],
      providers: [
        // Stopka „Wszystkie przewodniki" to routerLink — bez routera fixture nie wstaje.
        provideRouter([]),
        { provide: AppointmentsClient, useValue: { hasAnyAppointment } },
        { provide: OnboardingStateService, useValue: { state: stateSignal } },
        { provide: AuthSessionService, useValue: { currentRole: () => 'owner' } },
        { provide: GuideService, useValue: { running: signal(false), start } },
        {
          provide: GuideProgressService,
          useValue: {
            load: vi.fn().mockResolvedValue(undefined),
            completed: completedSignal,
            isLoaded: loadedSignal,
          },
        },
        { provide: MessageService, useValue: { add: addMessage } },
      ],
    });
    const fixture = TestBed.createComponent(StartHereCardComponent);
    fixture.componentRef.setInput('hasTeam', hasTeam);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  };

  const guideIds = (el: HTMLElement) =>
    [...el.querySelectorAll('[data-testid^="start-here-guide-"]')].map((b) =>
      b.getAttribute('data-testid')!.replace('start-here-guide-', ''),
    );

  beforeEach(() => {
    hasAnyAppointment = vi.fn().mockReturnValue(of(false));
    start = vi.fn();
    stateSignal = signal<OnboardingStateDto | null>({
      slug: 'salon-ani',
      usesAdHocSchedule: false,
      // Salon po nowym kreatorze — bez tego karta w ogóle się nie pokazuje (patrz test niżej).
      hasIndustry: true,
    } as OnboardingStateDto);
    completedSignal = signal<ReadonlySet<string>>(new Set<string>());
    loadedSignal = signal(true);
    addMessage = vi.fn();
  });

  it('pokazuje link do rezerwacji, dopóki salon nie ma żadnej wizyty', async () => {
    const el = (await setup()).nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid="start-here-card"]')).not.toBeNull();

    // Adres budujemy z `environment`, a NIE z wpisanego na sztywno portu: worktree dostają własne
    // porty przez hook, więc literał („localhost:4378") przechodził lokalnie i wywracał się po
    // scaleniu do main. Sprawdzamy kształt linku, nie konkretny numer portu.
    const bazowy = environment.bookingBaseUrl.replace(/\/+$/, '');
    expect(el.querySelector('[data-testid="start-here-link"]')?.textContent?.trim()).toBe(
      `${bazowy.replace(/^https?:\/\//, '')}/salon-ani`,
    );
    // Otwarcie „jak klient" musi iść na PEŁNY adres, inaczej link nigdzie się nie klika.
    expect(el.querySelector('[data-testid="start-here-open"]')?.getAttribute('href')).toBe(
      `${bazowy}/salon-ani`,
    );
  });

  it('po pierwszej wizycie zwija się do paska — kalendarz odzyskuje ekran', async () => {
    hasAnyAppointment = vi.fn().mockReturnValue(of(true));

    const el = (await setup()).nativeElement as HTMLElement;

    // Pełna karta zabierała 56% ekranu telefonu, spychając wizyty poniżej zgięcia.
    expect(el.querySelector('[data-testid="start-here-card"]')).toBeNull();
    const pasek = el.querySelector('[data-testid="start-here-collapsed"]');
    expect(pasek).not.toBeNull();
    expect(pasek?.textContent).toContain('2 przewodniki');
  });

  it('klik w pasek rozwija pełną listę — nauka jest o jedno kliknięcie', async () => {
    hasAnyAppointment = vi.fn().mockReturnValue(of(true));
    const fixture = await setup();
    const el = fixture.nativeElement as HTMLElement;

    el.querySelector<HTMLButtonElement>('[data-testid="start-here-collapsed"]')!.click();
    fixture.detectChanges();

    expect(el.querySelector('[data-testid="start-here-card"]')).not.toBeNull();
    expect(guideIds(el).length).toBeGreaterThan(0);
  });

  it('bez żadnej wizyty karta jest od razu pełna — pusty kalendarz nie ma czego zasłaniać', async () => {
    const el = (await setup()).nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid="start-here-collapsed"]')).toBeNull();
    expect(el.querySelector('[data-testid="start-here-card"]')).not.toBeNull();
  });

  /**
   * Salony sprzed kreatora (a także zakładane przez admina i demo) mają pustą branżę, bo ustawia ją
   * wyłącznie krok kreatora, a migracja backfillowała im tylko `onboarding_completed_at`. Pracują
   * w aplikacji od miesięcy — podpowiedzi na start byłyby dla nich szumem nad kalendarzem.
   */
  it('konto sprzed kreatora nie dostaje karty w ogóle — nawet gdy nie ma wizyt', async () => {
    stateSignal.set({ slug: 'stary-salon', hasIndustry: false } as OnboardingStateDto);

    const el = (await setup()).nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid="start-here-card"]')).toBeNull();
    expect(el.querySelector('[data-testid="start-here-collapsed"]')).toBeNull();
  });

  it('nie miga kartą, zanim stan onboardingu dojedzie', async () => {
    stateSignal.set(null);

    const el = (await setup()).nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid="start-here-card"]')).toBeNull();
    expect(el.querySelector('[data-testid="start-here-collapsed"]')).toBeNull();
  });

  it('„planuję każdy miesiąc osobno" dostaje otwieranie dnia z kalendarza', async () => {
    stateSignal.set({
      slug: 'salon-ani',
      usesAdHocSchedule: true,
      hasIndustry: true,
    } as OnboardingStateDto);

    const el = (await setup()).nativeElement as HTMLElement;

    expect(guideIds(el)[0]).toBe('open-day-from-calendar');
    expect(guideIds(el)).not.toContain('set-weekly-schedule');
  });

  it('grafik powtarzalny dostaje zamiast tego wyjątek na jeden dzień', async () => {
    const el = (await setup()).nativeElement as HTMLElement;

    expect(guideIds(el)[0]).toBe('set-special-day');
  });

  // Osobne `it`, nie dwa `setup()` w jednym: TestBed odmawia rekonfiguracji po instancjonowaniu.
  it('salon jednoosobowy nie dostaje przewodnika o usługach pracownika', async () => {
    expect(guideIds((await setup(false)).nativeElement)).not.toContain('assign-employee-services');
  });

  it('salon z zespołem dostaje przewodnik o usługach pracownika', async () => {
    expect(guideIds((await setup(true)).nativeElement)).toContain('assign-employee-services');
  });

  it('klik w pozycję uruchamia przewodnik', async () => {
    const el = (await setup()).nativeElement as HTMLElement;

    el.querySelector<HTMLButtonElement>('[data-testid="start-here-guide-set-special-day"]')!.click();

    expect(start).toHaveBeenCalledWith(expect.objectContaining({ id: 'set-special-day' }));
  });

  it('znika dopiero, gdy nie ma ani linku, ani nieprzejdzionych przewodników', async () => {
    hasAnyAppointment = vi.fn().mockReturnValue(of(true));
    completedSignal.set(new Set(['set-special-day', 'add-appointment']));

    const el = (await setup()).nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid="start-here-card"]')).toBeNull();
  });

  it('po przejściu ostatniej pozycji mówi, że to koniec — zniknięcie bez słowa czyta się jak usterka', async () => {
    const fixture = await setup();

    completedSignal.set(new Set(['set-special-day']));
    fixture.detectChanges();
    expect(addMessage).not.toHaveBeenCalled();

    completedSignal.set(new Set(['set-special-day', 'add-appointment']));
    fixture.detectChanges();

    expect(addMessage).toHaveBeenCalledWith(
      expect.objectContaining({ summary: 'To wszystko' }),
    );
  });

  it('nie gratuluje przy wejściu komuś, kto przeszedł wszystko dawno temu', async () => {
    // Zanim postęp dojedzie z serwera, zbiór ukończonych jest pusty — lista ma pozycje i zaraz
    // spada do zera. Bez bramki `isLoaded` komunikat wyskakiwałby przy każdym wejściu.
    loadedSignal = signal(false);
    hasAnyAppointment = vi.fn().mockReturnValue(of(true));
    const fixture = await setup();

    completedSignal.set(new Set(['set-special-day', 'add-appointment']));
    loadedSignal.set(true);
    fixture.detectChanges();

    expect(addMessage).not.toHaveBeenCalled();
  });

  it('przy błędzie zapytania nie zaczepia linkiem działającego salonu', async () => {
    hasAnyAppointment = vi.fn().mockReturnValue(throwError(() => new Error('offline')));

    const el = (await setup()).nativeElement as HTMLElement;

    expect(el.querySelector('[data-testid="start-here-link"]')).toBeNull();
  });
});
