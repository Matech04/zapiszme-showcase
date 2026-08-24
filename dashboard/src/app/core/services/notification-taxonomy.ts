/**
 * Taksonomia powiadomień dzwonka — JEDNO źródło prawdy dla rozróżnienia typów w UI.
 *
 * Panel dzwonka scala 4 źródła (patrz `NotificationCenterService`), które historycznie
 * lądowały jako jedna, nierozróżnialna lista („niebieski zegar dla wszystkiego"). Tutaj
 * każdemu zdarzeniu przypisujemy `NotificationCategory`, a kategorii — spójny wygląd
 * (etykieta + ikona + kolor). Dzięki temu „anulowano", „czeka na potwierdzenie",
 * „nowa rezerwacja" itd. są rozpoznawalne na pierwszy rzut oka.
 */

/** Kategoria prezentacyjna — niezależna od kanału (persisted/pending/poll). */
export type NotificationCategory =
  | 'new-booking'
  | 'awaiting-confirmation'
  | 'cancelled'
  | 'rescheduled'
  | 'reminder'
  | 'outside-schedule'
  | 'system';

/**
 * Backendowy `NotificationType` (int w DTO) — tylko wartości, które realnie docierają do
 * dzwonka salonu (odbiorcy z kontem panelu). Lustro
 * `App.Domain/Aggregates/TenantAggregate/NotificationType.cs`.
 */
export const NotificationType = {
  NewBookingToSalon: 1,
  CancellationToSalon: 3,
  RescheduleToSalon: 5,
  AppointmentReminderToCustomer: 7,
  AwaitingConfirmationToSalon: 8,
  CancelledBySalonToCustomer: 9,
  RescheduledBySalonToCustomer: 10,
  AppointmentReminder2hToCustomer: 11,
} as const;

/** Mapuje surowy `NotificationType` (int) trwałego powiadomienia na kategorię prezentacyjną. */
export function categoryFromNotificationType(type: number | null | undefined): NotificationCategory {
  switch (type) {
    case NotificationType.NewBookingToSalon:
      return 'new-booking';
    case NotificationType.AwaitingConfirmationToSalon:
      return 'awaiting-confirmation';
    case NotificationType.CancellationToSalon:
    case NotificationType.CancelledBySalonToCustomer:
      return 'cancelled';
    case NotificationType.RescheduleToSalon:
    case NotificationType.RescheduledBySalonToCustomer:
      return 'rescheduled';
    case NotificationType.AppointmentReminderToCustomer:
    case NotificationType.AppointmentReminder2hToCustomer:
      return 'reminder';
    default:
      return 'system';
  }
}

export interface NotificationVisual {
  /** Krótka etykieta kategorii (pigułka), np. „Anulowano". */
  readonly label: string;
  /** Klasa ikony PrimeNG (`pi pi-...`). */
  readonly icon: string;
  /**
   * Klasy tła + tekstu dla kwadratowej ikony (light + dark). Pełne literały —
   * Tailwind skanuje pliki .ts, więc muszą być zapisane dosłownie (bez interpolacji).
   */
  readonly chipClass: string;
  /** Klasy pigułki-etykiety (light + dark). */
  readonly tagClass: string;
  /** Kolor lewej krawędzi wpisu, gdy nieprzeczytany. */
  readonly borderClass: string;
}

/**
 * Wygląd per kategoria. Kolory semantyczne (emerald/amber/rose/indigo/orange/sky/slate),
 * NIE rampa surface — więc bezpieczne w dark mode bez odwracania (patrz notatka
 * o odwróconej rampie surface w dashboardzie).
 */
export const NOTIFICATION_VISUALS: Record<NotificationCategory, NotificationVisual> = {
  'new-booking': {
    label: 'Nowa rezerwacja',
    icon: 'pi pi-calendar-plus',
    chipClass: 'bg-emerald-100/80 text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-300',
    tagClass: 'bg-emerald-100/70 text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-300',
    borderClass: 'border-emerald-500',
  },
  'awaiting-confirmation': {
    label: 'Czeka na potwierdzenie',
    icon: 'pi pi-hourglass',
    chipClass: 'bg-amber-100/80 text-amber-700 dark:bg-amber-950/40 dark:text-amber-300',
    tagClass: 'bg-amber-100/70 text-amber-700 dark:bg-amber-950/40 dark:text-amber-300',
    borderClass: 'border-amber-500',
  },
  cancelled: {
    label: 'Anulowano',
    icon: 'pi pi-times-circle',
    chipClass: 'bg-rose-100/80 text-rose-700 dark:bg-rose-950/40 dark:text-rose-300',
    tagClass: 'bg-rose-100/70 text-rose-700 dark:bg-rose-950/40 dark:text-rose-300',
    borderClass: 'border-rose-500',
  },
  rescheduled: {
    label: 'Przełożono',
    icon: 'pi pi-sync',
    chipClass: 'bg-indigo-100/80 text-indigo-700 dark:bg-indigo-950/40 dark:text-indigo-300',
    tagClass: 'bg-indigo-100/70 text-indigo-700 dark:bg-indigo-950/40 dark:text-indigo-300',
    borderClass: 'border-indigo-500',
  },
  reminder: {
    label: 'Przypomnienie',
    icon: 'pi pi-bell',
    chipClass: 'bg-sky-100/80 text-sky-700 dark:bg-sky-950/40 dark:text-sky-300',
    tagClass: 'bg-sky-100/70 text-sky-700 dark:bg-sky-950/40 dark:text-sky-300',
    borderClass: 'border-sky-500',
  },
  'outside-schedule': {
    label: 'Poza grafikiem',
    icon: 'pi pi-exclamation-triangle',
    chipClass: 'bg-orange-100/80 text-orange-700 dark:bg-orange-950/40 dark:text-orange-300',
    tagClass: 'bg-orange-100/70 text-orange-700 dark:bg-orange-950/40 dark:text-orange-300',
    borderClass: 'border-orange-500',
  },
  system: {
    label: 'Informacja',
    icon: 'pi pi-info-circle',
    chipClass: 'bg-slate-100/80 text-slate-600 dark:bg-slate-800/50 dark:text-slate-300',
    tagClass: 'bg-slate-100/70 text-slate-600 dark:bg-slate-800/50 dark:text-slate-300',
    borderClass: 'border-slate-400',
  },
};

/** Skrót — wygląd dla danej kategorii (z bezpiecznym fallbackiem na `system`). */
export function notificationVisual(category: NotificationCategory): NotificationVisual {
  return NOTIFICATION_VISUALS[category] ?? NOTIFICATION_VISUALS.system;
}
