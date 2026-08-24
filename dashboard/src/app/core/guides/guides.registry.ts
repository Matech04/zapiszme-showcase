import { UserRole } from '@core/models/navigation.model';
import { ADD_LEAVE_GUIDE } from './defs/add-leave.guide';
import { ADD_SERVICE_GUIDE } from './defs/add-service.guide';
import {
  BOOKING_ACCESS_GUIDE,
  BOOKING_CONFIRMATION_GUIDE,
  PUBLIC_FORM_GUIDE,
  SHARE_BOOKING_LINK_GUIDE,
} from './defs/booking.guides';
import { ADD_APPOINTMENT_GUIDE, ADD_CUSTOMER_GUIDE } from './defs/clients.guides';
import { GROUP_SERVICES_GUIDE } from './defs/group-services.guide';
import { OPEN_DAY_FROM_CALENDAR_GUIDE } from './defs/open-day-from-calendar.guide';
import {
  CHECK_USAGE_GUIDE,
  CONNECT_STRIPE_GUIDE,
  SET_DEPOSIT_AMOUNT_GUIDE,
  SET_REMINDERS_GUIDE,
} from './defs/money.guides';
import { SET_SPECIAL_DAY_GUIDE } from './defs/set-special-day.guide';
import { SET_WEEKLY_SCHEDULE_GUIDE } from './defs/set-weekly-schedule.guide';
import {
  ADD_EMPLOYEE_GUIDE,
  ASSIGN_EMPLOYEE_SERVICES_GUIDE,
  SETUP_RECEPTION_GUIDE,
} from './defs/team.guides';
import { GuideDef } from './guide.types';

/**
 * Jedyne źródło prawdy o przewodnikach. Karmi trzy rzeczy naraz: katalog `/admin/guides`,
 * kontekstowy przycisk „?" na stronach oraz test integralności kotwic
 * (`guides.registry.spec.ts`), który pilnuje, żeby selektory nie wskazywały w próżnię.
 *
 * Ten ostatni jest tu najważniejszy. Poprzednie przewodniki zgniły właśnie dlatego, że
 * silnik po cichu pomijał kroki ze zgubioną kotwicą — dwa kroki były martwe przez wiele
 * wydań i nikt tego nie zauważył.
 *
 * Kolejność w tablicy jest kolejnością w obrębie sekcji katalogu; sekcje układa
 * `GUIDE_CATEGORY_LABELS`.
 */
export const GUIDES: readonly GuideDef[] = [
  // Rezerwacje online — od tego zależy, czy do salonu w ogóle trafiają zapisy.
  SHARE_BOOKING_LINK_GUIDE,
  BOOKING_ACCESS_GUIDE,
  BOOKING_CONFIRMATION_GUIDE,
  PUBLIC_FORM_GUIDE,

  // Dostępność — kolejność odpowiada trzem kafelkom huba, a przed nimi stoi ścieżka
  // kalendarzowa: dla właścicielki bez grafiku powtarzalnego to JEDYNY sposób otwarcia dnia,
  // więc nie może być zakopana pod przewodnikiem o grafiku, którego świadomie nie prowadzi.
  OPEN_DAY_FROM_CALENDAR_GUIDE,
  SET_WEEKLY_SCHEDULE_GUIDE,
  SET_SPECIAL_DAY_GUIDE,
  ADD_LEAVE_GUIDE,

  // Oferta.
  ADD_SERVICE_GUIDE,
  GROUP_SERVICES_GUIDE,

  // Codzienna obsługa.
  ADD_APPOINTMENT_GUIDE,
  ADD_CUSTOMER_GUIDE,

  // Pieniądze: najpierw wpływ (zadatki), potem koszt (powiadomienia).
  CONNECT_STRIPE_GUIDE,
  SET_DEPOSIT_AMOUNT_GUIDE,
  SET_REMINDERS_GUIDE,
  CHECK_USAGE_GUIDE,

  // Zespół — w kolejności, w jakiej realnie się to robi.
  ADD_EMPLOYEE_GUIDE,
  ASSIGN_EMPLOYEE_SERVICES_GUIDE,
  SETUP_RECEPTION_GUIDE,
];

/** Przewodniki dostępne dla danej roli. */
export function guidesForRole(role: UserRole | null): GuideDef[] {
  if (!role) return [];
  return GUIDES.filter((guide) => guide.roles.includes(role));
}

/**
 * Przewodniki, które zaczynają się na wskazanej trasie — do kontekstowego przycisku „?".
 * Porównujemy po szablonie trasy (z tokenem `:me`), bo w tym miejscu nie znamy jeszcze
 * konkretnego id pracownika.
 */
export function guidesForRoute(url: string, role: UserRole | null): GuideDef[] {
  return guidesForRole(role).filter((guide) => routeMatches(guide.entryRoute, url));
}

/** Dopasowanie trasy z tokenami (`:me`, `:id`) do konkretnego URL-a. */
function routeMatches(template: string, url: string): boolean {
  const path = url.split('?')[0].split('#')[0];
  const templateParts = template.split('/');
  const pathParts = path.split('/');

  if (templateParts.length !== pathParts.length) return false;

  return templateParts.every(
    (part, i) => part.startsWith(':') || part === pathParts[i],
  );
}
