import { describe, expect, it } from 'vitest';
import { SalonLearningContext, startHereItems } from './start-here';

/**
 * Sedno: wybór z kroku „Jak układasz grafik?" ma realnie zmieniać to, co widzi właścicielka.
 * Do tej pory katalog podawał wszystkim tę samą listę, więc osoba planująca miesiąc samodzielnie
 * dostawała na czele „Ustawmy grafik powtarzalny" — instrukcję do czynności, której nie wykonuje.
 */
describe('startHereItems', () => {
  const base: SalonLearningContext = {
    usesAdHocSchedule: false,
    hasTeam: false,
    role: 'owner',
    completedGuideIds: new Set<string>(),
  };

  const ids = (ctx: Partial<SalonLearningContext>) =>
    startHereItems({ ...base, ...ctx }).map((i) => i.guide.id);

  it('„planuję każdy miesiąc osobno" dostaje otwieranie dnia z kalendarza, i to na pierwszym miejscu', () => {
    const wynik = ids({ usesAdHocSchedule: true });

    expect(wynik[0]).toBe('open-day-from-calendar');
    // Grafik powtarzalny to dokładnie ta czynność, której świadomie nie prowadzi.
    expect(wynik).not.toContain('set-weekly-schedule');
  });

  it('grafik powtarzalny dostaje zamiast tego wyjątek na jeden dzień', () => {
    const wynik = ids({ usesAdHocSchedule: false });

    expect(wynik[0]).toBe('set-special-day');
    expect(wynik).not.toContain('open-day-from-calendar');
  });

  it('wizyta z telefonu jest dla obu ścieżek — nikt nie ucieknie przed dzwoniącą klientką', () => {
    expect(ids({ usesAdHocSchedule: true })).toContain('add-appointment');
    expect(ids({ usesAdHocSchedule: false })).toContain('add-appointment');
  });

  it('salon jednoosobowy nie dostaje przewodnika o przypisywaniu usług pracownikom', () => {
    expect(ids({ hasTeam: false })).not.toContain('assign-employee-services');
    expect(ids({ hasTeam: true })).toContain('assign-employee-services');
  });

  it('przejdziony przewodnik znika z listy', () => {
    const wynik = ids({
      usesAdHocSchedule: true,
      completedGuideIds: new Set(['open-day-from-calendar']),
    });

    expect(wynik).not.toContain('open-day-from-calendar');
    expect(wynik).toContain('add-appointment');
  });

  it('gdy wszystko przejdzione, lista jest pusta — karta ma się nie pokazać', () => {
    const wynik = ids({
      usesAdHocSchedule: true,
      hasTeam: true,
      completedGuideIds: new Set([
        'open-day-from-calendar',
        'add-appointment',
        'assign-employee-services',
      ]),
    });

    expect(wynik).toEqual([]);
  });

  it('nigdy więcej niż trzy pozycje — karta nad kalendarzem to lista, nie ściana', () => {
    expect(ids({ usesAdHocSchedule: true, hasTeam: true }).length).toBeLessThanOrEqual(3);
  });

  it('rola bez dostępu do przewodnika go nie dostaje', () => {
    // Recepcja (kiosk) obsługuje wizyty, ale nie ma własnego kalendarza ani usług pracowników.
    const wynik = ids({ role: 'kiosk', usesAdHocSchedule: true, hasTeam: true });

    expect(wynik).toEqual(['add-appointment']);
  });

  it('bez znanej roli nie zgadujemy — pusto', () => {
    expect(ids({ role: null })).toEqual([]);
  });
});
