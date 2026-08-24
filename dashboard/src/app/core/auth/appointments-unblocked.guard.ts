import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { BookingPauseStore } from '@core/services/booking-pause.store';
import { AuthSessionService } from './auth-session.service';

/**
 * Blokuje formularze tworzenia/edycji wizyty (schedule/new, appointment/:id/edit), gdy salon
 * wstrzymał rezerwacje albo trwa globalny tryb serwisowy platformy. Przekierowuje wtedy owner/managera
 * z powrotem na kalendarz (strona domowa panelu). Sam kalendarz NIE jest guardowany — jest stroną
 * domową; próby zapisu z niego i tak są blokowane po stronie serwera (400 booking.paused / 503).
 *
 * Dotyczy tylko owner/manager — to oni „zarządzają" wizytami. Pracownik/kiosk oglądają swój dzień
 * (widok-only), a przy globalnym trybie serwisowym backend i tak blokuje ich zapisy (503).
 */
export const appointmentsUnblockedGuard: CanActivateFn = async () => {
  const store = inject(BookingPauseStore);
  const session = inject(AuthSessionService);
  const router = inject(Router);

  await store.ensureLoaded();
  if (!store.appointmentsBlocked()) return true;

  const roles = session.session()?.roles?.map((r) => r.toLowerCase()) ?? [];
  const managesAppointments = roles.includes('owner') || roles.includes('manager');
  if (!managesAppointments) return true;

  return router.createUrlTree(['/admin/schedule']);
};
