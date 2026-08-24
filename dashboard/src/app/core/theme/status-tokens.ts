export type AppointmentStatusVariant =
  | 'pending'
  | 'booked'
  | 'inProgress'
  | 'completed'
  | 'canceled'
  | 'awaitingOtp'
  | 'default';

export interface StatusTokens {
  /** Polski label do wyświetlenia (sentence case). */
  label: string;
  /** Klasy tła + obramowania kafelka na osi czasu (light + dark). */
  surfaceClasses: string;
  /** Klasy koloru tekstu chip'a/etykiety (light + dark). */
  textClasses: string;
  /** Lewa wskazówka kolorystyczna (klasy `bg-*`). */
  accentClasses: string;
  /** Ikona (`pi pi-*`) reprezentująca status. */
  icon: string;
}

export const APPOINTMENT_STATUS_TOKENS: Record<AppointmentStatusVariant, StatusTokens> = {
  pending: {
    label: 'Oczekuje',
    surfaceClasses:
      'bg-amber-50 dark:bg-amber-950/40 border-amber-200/70 dark:border-amber-800/50',
    textClasses: 'text-amber-700 dark:text-amber-300',
    accentClasses: 'bg-amber-500',
    icon: 'pi pi-clock',
  },
  booked: {
    label: 'Zatwierdzona',
    surfaceClasses: 'bg-sky-50 dark:bg-sky-950/35 border-sky-200/60 dark:border-sky-800/50',
    textClasses: 'text-sky-700 dark:text-sky-300',
    accentClasses: 'bg-sky-500',
    icon: 'pi pi-check-circle',
  },
  inProgress: {
    label: 'W trakcie',
    surfaceClasses:
      'bg-violet-50 dark:bg-violet-950/40 border-violet-200/60 dark:border-violet-800/50',
    textClasses: 'text-violet-700 dark:text-violet-300',
    accentClasses: 'bg-violet-500',
    icon: 'pi pi-spin pi-spinner',
  },
  completed: {
    label: 'Zakończona',
    surfaceClasses:
      'bg-emerald-50 dark:bg-emerald-950/35 border-emerald-200/60 dark:border-emerald-800/50',
    textClasses: 'text-emerald-700 dark:text-emerald-300',
    accentClasses: 'bg-emerald-500',
    icon: 'pi pi-flag-fill',
  },
  canceled: {
    label: 'Anulowana',
    surfaceClasses:
      'bg-surface-200/80 dark:bg-surface-200/50 border-surface-400/60 dark:border-surface-600/50',
    textClasses: 'text-surface-600 dark:text-surface-300',
    accentClasses: 'bg-surface-400',
    icon: 'pi pi-times-circle',
  },
  awaitingOtp: {
    label: 'Oczekuje na kod',
    surfaceClasses:
      'bg-surface-100/80 dark:bg-surface-100/50 border-surface-300/60 dark:border-surface-600/50',
    textClasses: 'text-surface-500 dark:text-surface-400',
    accentClasses: 'bg-surface-400',
    icon: 'pi pi-lock',
  },
  default: {
    label: 'Wizyta',
    surfaceClasses:
      'bg-slate-50 dark:bg-slate-900/50 border-slate-200/60 dark:border-slate-700/50',
    textClasses: 'text-slate-600 dark:text-slate-300',
    accentClasses: 'bg-slate-400',
    icon: 'pi pi-calendar',
  },
};

export const APPOINTMENT_STATUS_ID_MAP: Record<number, AppointmentStatusVariant> = {
  1: 'pending',
  2: 'booked',
  3: 'inProgress',
  4: 'completed',
  5: 'canceled',
  6: 'awaitingOtp',
};

export const APPOINTMENT_STATUS_NAME_MAP: Record<string, AppointmentStatusVariant> = {
  pending: 'pending',
  booked: 'booked',
  inprogress: 'inProgress',
  completed: 'completed',
  canceled: 'canceled',
  cancelled: 'canceled',
  awaitingotp: 'awaitingOtp',
};

export function statusVariantFromIdOrName(
  id: number | undefined | null,
  name: string | undefined | null,
): AppointmentStatusVariant {
  if (id != null && APPOINTMENT_STATUS_ID_MAP[id]) {
    return APPOINTMENT_STATUS_ID_MAP[id];
  }
  const normalized = (name ?? '').replace(/\s+/g, '').toLowerCase();
  return APPOINTMENT_STATUS_NAME_MAP[normalized] ?? 'default';
}

export function statusTokens(variant: AppointmentStatusVariant): StatusTokens {
  return APPOINTMENT_STATUS_TOKENS[variant] ?? APPOINTMENT_STATUS_TOKENS.default;
}
