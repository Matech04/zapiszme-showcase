import { computed, inject, Injectable } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { toObservable } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';
import { SalonSettingsClient } from '@core/api/api-client';
import { AuthSessionService } from '@core/auth/auth-session.service';

@Injectable({ providedIn: 'root' })
export class SalonTitleService {
  private readonly titleService = inject(Title);
  private readonly authSession = inject(AuthSessionService);
  private readonly salonSettingsClient = inject(SalonSettingsClient);

  constructor() {
    // Źródłem jest sam tenant, nie `isAuthenticated`: właściciel wchodzi do panelu BEZ salonu
    // (kreator), a tenant pojawia się dopiero po jego ukończeniu — bez ponownego logowania.
    // Obserwowanie tenanta łapie ten moment i odświeża tytuł.
    toObservable(computed(() => this.authSession.session()?.tenantId ?? null))
      .pipe(
        switchMap((tenantId) => {
          // Konto bez kontekstu salonu — SystemAdmin (nigdy go nie ma) albo właściciel w trakcie
          // onboardingu (salon jeszcze nie istnieje). `GET /api/SalonSettings` zwróciłby wtedy
          // 400 `tenant.missing`, a errorInterceptor pokazałby toast „Nie udało się ustalić
          // kontekstu salonu" — na każdej stronie panelu admina i na starcie kreatora. Lokalny
          // `catchError` tego nie tłumi, bo interceptor jest wyżej w łańcuchu. Pomijamy fetch.
          if (!tenantId) {
            return of(null);
          }
          return this.salonSettingsClient.get().pipe(catchError(() => of(null)));
        }),
      )
      .subscribe((tenant) => {
        const salonName = tenant?.name;
        this.titleService.setTitle(salonName ? `zapisz.me – ${salonName}` : 'zapisz.me');
      });
  }
}
