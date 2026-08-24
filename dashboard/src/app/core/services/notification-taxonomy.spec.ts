import { describe, it, expect } from 'vitest';
import {
  NotificationType,
  categoryFromNotificationType,
  notificationVisual,
  NOTIFICATION_VISUALS,
  type NotificationCategory,
} from './notification-taxonomy';

/**
 * Rdzeń overhaulu dzwonka: backendowy `NotificationType` (int) MUSI mapować się na właściwą
 * kategorię prezentacyjną, żeby „anulowano" ≠ „czeka na potwierdzenie" ≠ „nowa rezerwacja".
 * Wcześniej front ignorował `type` i wszystkie trwałe powiadomienia wyglądały tak samo.
 */
describe('categoryFromNotificationType', () => {
  it.each<[keyof typeof NotificationType, NotificationCategory]>([
    ['NewBookingToSalon', 'new-booking'],
    ['AwaitingConfirmationToSalon', 'awaiting-confirmation'],
    ['CancellationToSalon', 'cancelled'],
    ['CancelledBySalonToCustomer', 'cancelled'],
    ['RescheduleToSalon', 'rescheduled'],
    ['RescheduledBySalonToCustomer', 'rescheduled'],
    ['AppointmentReminderToCustomer', 'reminder'],
    ['AppointmentReminder2hToCustomer', 'reminder'],
  ])('typ %s → kategoria %s', (typeName, expected) => {
    expect(categoryFromNotificationType(NotificationType[typeName])).toBe(expected);
  });

  it.each([null, undefined, 0, 999, 12 /* CustomerVerificationOtp — nie trafia do dzwonka */])(
    'nieznany/niesalonowy typ (%s) → fallback „system"',
    (type) => {
      expect(categoryFromNotificationType(type as number)).toBe('system');
    },
  );
});

describe('notificationVisual', () => {
  const categories: NotificationCategory[] = [
    'new-booking',
    'awaiting-confirmation',
    'cancelled',
    'rescheduled',
    'reminder',
    'outside-schedule',
    'system',
  ];

  it('każda kategoria ma komplet wyglądu (etykieta, ikona, klasy)', () => {
    for (const c of categories) {
      const v = notificationVisual(c);
      expect(v.label.length).toBeGreaterThan(0);
      expect(v.icon).toMatch(/^pi pi-/);
      expect(v.chipClass).toContain('bg-');
      expect(v.tagClass).toContain('text-');
      expect(v.borderClass).toMatch(/^border-/);
    }
  });

  it('kategorie mają rozróżnialne ikony i etykiety (żaden overhaul-cel się nie duplikuje)', () => {
    const labels = categories.map((c) => NOTIFICATION_VISUALS[c].label);
    const icons = categories.map((c) => NOTIFICATION_VISUALS[c].icon);
    expect(new Set(labels).size).toBe(labels.length);
    expect(new Set(icons).size).toBe(icons.length);
  });

  it('kluczowe typy mają oczekiwane etykiety po polsku', () => {
    expect(notificationVisual('cancelled').label).toBe('Anulowano');
    expect(notificationVisual('awaiting-confirmation').label).toBe('Czeka na potwierdzenie');
    expect(notificationVisual('new-booking').label).toBe('Nowa rezerwacja');
  });
});
