import { UserRole } from '@core/models/navigation.model';
import { ADD_APPOINTMENT_GUIDE } from './defs/clients.guides';
import { OPEN_DAY_FROM_CALENDAR_GUIDE } from './defs/open-day-from-calendar.guide';
import { SET_SPECIAL_DAY_GUIDE } from './defs/set-special-day.guide';
import { ASSIGN_EMPLOYEE_SERVICES_GUIDE } from './defs/team.guides';
import { GuideDef } from './guide.types';

/**
 * Dobór przewodników „Zacznij tutaj" na podstawie tego, co właścicielka wybrała w kreatorze.
 *
 * Katalog `/admin/guides` pokazuje wszystko każdemu — słusznie, bo to katalog. Ta lista jest
 * czymś innym: trzema rzeczami, których TEN salon będzie potrzebował najpierw, przy TYM sposobie
 * pracy. Różnica jest widoczna na jednym przykładzie: właścicielka, która wybrała „planuję każdy
 * miesiąc osobno", dostaje w katalogu „Ustawmy grafik powtarzalny" jako pierwszy kafelek
 * dostępności — czyli instrukcję do czynności, której świadomie nie wykonuje.
 *
 * Czego ta lista NIE robi: nie odhacza niczego za kreator. To, że kreator dodał usługi, nie znaczy,
 * że właścicielka umie dodać usługę sama — przewodnik uczy czynności w panelu, a nie potwierdza
 * stan konfiguracji. Dlatego pozycje znikają wyłącznie po PRZEJŚCIU przewodnika
 * (`GuideProgressService`, stan w bazie), nigdy przez wykrycie danych.
 */

/** Wejście doboru — same fakty o salonie, bez zależności od Angulara i DOM-u. */
export interface SalonLearningContext {
  /** Wybór z kroku „Jak układasz grafik?" — „planuję każdy miesiąc osobno". */
  usesAdHocSchedule: boolean;
  /** Czy w salonie jest ktoś poza właścicielką (kreator o zespół nie pyta). */
  hasTeam: boolean;
  role: UserRole | null;
  /** Przewodniki już przejdzione — pozycja znika dopiero tutaj. */
  completedGuideIds: ReadonlySet<string>;
}

export interface StartHereItem {
  guide: GuideDef;
  /** Jedno zdanie: dlaczego akurat to, akurat w tym salonie. Widoczne pod tytułem. */
  reason: string;
}

/** Ile pozycji maksymalnie — karta nad kalendarzem ma być listą, nie ścianą. */
const MAX_ITEMS = 3;

export function startHereItems(ctx: SalonLearningContext): StartHereItem[] {
  const candidates: StartHereItem[] = [];

  // 1. Dostępność — rozstrzyga wybór z kreatora, bo to dwa różne sposoby pracy.
  if (ctx.usesAdHocSchedule) {
    // Bez grafiku powtarzalnego kalendarz startuje pusty: dopóki nie otworzy pierwszego dnia,
    // klientka nie zobaczy ŻADNEGO terminu. Najpilniejsza rzecz w całym panelu.
    candidates.push({
      guide: OPEN_DAY_FROM_CALENDAR_GUIDE,
      reason: 'Twój kalendarz zaczyna pusty — dopóki nie otworzysz dnia, klientki nie zobaczą terminów.',
    });
  } else {
    // Grafik powtarzalny kreator już zapisał. Luka jest gdzie indziej: co zrobić, gdy jeden dzień
    // ma wyglądać inaczej niż reszta tygodnia.
    candidates.push({
      guide: SET_SPECIAL_DAY_GUIDE,
      reason: 'Grafik masz ustawiony — to pokazuje, jak zmienić godziny w jednym dniu, bez ruszania reszty.',
    });
  }

  // 2. Wizyta z telefonu — niezależne od wyborów: telefon dzwoni od pierwszego dnia, a rezerwacja
  //    online nie obsługuje klientki, która woli zadzwonić.
  candidates.push({
    guide: ADD_APPOINTMENT_GUIDE,
    reason: 'Klientka zadzwoni zamiast rezerwować online — tak zapisujesz ją ręcznie.',
  });

  // 3. Zespół — tylko gdy jest kogo przypisać. Salonowi jednoosobowemu „przypiszmy usługi
  //    pracownikowi" nie mówi nic; kreator o zespół nie pyta, więc czytamy stan salonu.
  if (ctx.hasTeam) {
    candidates.push({
      guide: ASSIGN_EMPLOYEE_SERVICES_GUIDE,
      reason: 'Bez przypisanych usług klientka nie zarezerwuje wizyty u tej osoby.',
    });
  }

  return candidates
    .filter((item) => (ctx.role ? item.guide.roles.includes(ctx.role) : false))
    .filter((item) => !ctx.completedGuideIds.has(item.guide.id))
    .slice(0, MAX_ITEMS);
}
