import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MessageService } from 'primeng/api';
import { catchError, from, of, switchMap, throwError } from 'rxjs';
import { ApiProblemDetails, apiErrorMessage } from '@core/errors/api-error-messages';
import { SILENT_ERRORS } from '@core/api/appointments-range';
import { SessionExpiryService } from '@core/auth/session-expiry.service';
import { CsrfTokenService } from '@core/auth/csrf-token.service';

/**
 * Znacznik „to jest już ponowienie po odświeżeniu tokenu" — bez niego druga porażka antiforgery
 * zapętliłaby odświeżanie. Nagłówek leci do backendu, ale jest dla niego obojętny.
 */
const XSRF_RETRIED = 'X-XSRF-Retried';

/** NSwag / api-client często ustawia responseType: blob — przy błędzie error.error to Blob, bez parsowania brak detail z ProblemDetails. */
function normalizeHttpError(error: HttpErrorResponse) {
  const blob = error.error;
  if (!(blob instanceof Blob)) {
    return of(error);
  }
  return from(blob.text()).pipe(
    switchMap((text) => {
      let parsed: unknown = {};
      try {
        parsed = text ? JSON.parse(text) : {};
      } catch {
        parsed = { detail: text || undefined };
      }
      return of(
        new HttpErrorResponse({
          error: parsed,
          headers: error.headers,
          status: error.status,
          statusText: error.statusText,
          url: error.url ?? undefined
        })
      );
    })
  );
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const messageService = inject(MessageService);
  const sessionExpiry = inject(SessionExpiryService);
  // Bezpieczne tutaj: serwis chodzi po `HttpBackend`, więc odświeżenie tokenu nie wraca
  // przez łańcuch interceptorów i nie zapętla się na własnym błędzie.
  const csrf = inject(CsrfTokenService);

  const suppressToastForAuthForms =
    req.method === 'POST' &&
    (req.url.includes('/api/auth/login') || req.url.includes('/api/auth/register-owner'));

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      // Formularze auth (login/register) obsługują błąd same przez wygenerowanego klienta
      // NSwag. `normalizeHttpError` zamienia blob na obiekt, przez co `blobToText` w kliencie
      // dostaje `undefined` i gubi ProblemDetails (errorCode/userId) — wtedy login pokazuje
      // mylące "Nieprawidłowy email lub hasło" zamiast wykryć PhoneNotConfirmed. Toast i tak tu
      // pomijamy, więc oddajemy oryginalny błąd (blob nietknięty), by klient odczytał ciało.
      if (suppressToastForAuthForms) {
        return throwError(() => error);
      }

      return normalizeHttpError(error).pipe(
        switchMap((err) => {
          let errorMessage = 'Wystąpił nieoczekiwany błąd';
          let summary = 'Błąd';
          const isSessionProbe = /\/api\/auth\/me$/i.test(req.url) && req.method === 'GET';

          // Sonda trybu demo na ekranie logowania — gdy endpoint nie istnieje (stary backend)
          // lub demo wyłączone, po prostu chowamy przycisk. Bez toasta „zasób nie istnieje".
          const isDemoProbe = /\/api\/demo\/enabled$/i.test(req.url) && req.method === 'GET';

          const body = err.error as ApiProblemDetails | null | undefined;

          switch (err.status) {
            case 400:
              summary = 'Błąd walidacji';
              errorMessage = apiErrorMessage(body, 'Nieprawidłowe dane');
              break;

            case 401:
              summary = 'Brak dostępu';
              errorMessage = 'Zaloguj się ponownie.';
              break;

            case 403:
              summary = 'Odmowa dostępu';
              errorMessage = 'Nie masz uprawnień do tej akcji.';
              break;

            case 404:
              summary = 'Nie znaleziono';
              errorMessage = 'Zasób, którego szukasz, nie istnieje.';
              break;

            case 500:
              summary = 'Błąd serwera';
              errorMessage = apiErrorMessage(body, 'Serwer napotkał problem. Spróbuj później.');
              break;

            case 0:
              summary = 'Błąd połączenia';
              errorMessage = 'Nie można połączyć się z serwerem.';
              break;

            default:
              errorMessage = apiErrorMessage(body, errorMessage);
          }

          // Sonda sesji (`GET /api/auth/me`) to zapytanie tła z bootstrapu — jej błędy obsługuje
          // `AuthSessionService.hydrate()` (ponowienia + ekran `/offline`), więc toast byłby zarówno
          // zbędny, jak i zwielokrotniony: każda ponowiona próba wyemitowałaby własny.
          if (isSessionProbe) {
            return throwError(() => err);
          }

          // 401 z żądania domenowego = serwer odrzucił ciasteczko (wygasło, rotacja kluczy
          // DataProtection). Dotąd leciał sam toast „Zaloguj się ponownie", a panel zostawał otwarty
          // ze stanem martwej sesji i sypał nim przy KAŻDYM kolejnym żądaniu.
          //
          // Toast zdejmujemy tutaj, bo pokazuje go `AuthSessionService` — dokładnie jeden raz, nawet
          // gdy 401 przyjdzie serią z kilku równoległych żądań. Żądania anonimowe (login, rejestracja)
          // wyszły z tej ścieżki wcześniej i obsługują błąd same.
          if (err.status === 401) {
            sessionExpiry.notifyExpired();
            return throwError(() => err);
          }

          // Wygasła sesja NIE przychodzi jako 401. Antiforgery jest sprawdzane PRZED autoryzacją,
          // a token jest związany z tożsamością — gdy ta znika (wygaśnięcie ciasteczka, wylogowanie
          // w drugiej karcie, rotacja kluczy DataProtection), serwer odpowiada 400
          // `auth.antiforgery_invalid`. Cała przemyślana obsługa wygaśnięcia wisiała więc na kodzie,
          // który w tym scenariuszu nie pada: użytkownik dostawał „Sprawdź dane i spróbuj ponownie"
          // i utykał — najdotkliwiej w kreatorze salonu, w środku wieloetapowego zapisu.
          //
          // Najpierw próbujemy ODZYSKAĆ: świeży token i jedno ponowienie. To pokrywa przypadek,
          // w którym sesja żyje, a nieważny jest sam token (długo otwarta karta, restart backendu
          // w dev). Dopiero gdy ponowienie też padnie, uznajemy sesję za martwą — `notifyExpired()`
          // ma własny strażnik, więc nic nie zrobi, jeśli front już wie, że nie ma sesji.
          const errorCode = (err.error as ApiProblemDetails | null)?.errorCode;
          if (err.status === 400 && errorCode === 'auth.antiforgery_invalid' && !req.headers.has(XSRF_RETRIED)) {
            return csrf.refresh().pipe(
              switchMap((token) =>
                next(
                  req.clone({
                    headers: req.headers.set('X-XSRF-TOKEN', token).set(XSRF_RETRIED, '1'),
                  }),
                ),
              ),
              catchError((retryErr: HttpErrorResponse) => {
                sessionExpiry.notifyExpired();
                return throwError(() => retryErr);
              }),
            );
          }

          const isSalonSettingsGet =
            /\/api\/SalonSettings$/i.test(req.url) && req.method === 'GET';
          if (isSalonSettingsGet && err.status === 403) {
            return throwError(() => err);
          }

          // Konfiguracja grafiku cudzego pracownika (`CanManageOwnEmployeeScope` → 403). Kalendarz
          // pobiera ją best-effort i połyka błąd przez `catchError`, więc toast byłby szumem —
          // pracownik nie wykonał żadnej akcji, tylko otworzył widok zespołu.
          const isScheduleConfigRead =
            /\/api\/Employees\/[^/]+\/(employee-schedules|leaves|schedule-overrides)$/i.test(req.url) &&
            req.method === 'GET';
          if (isScheduleConfigRead && err.status === 403) {
            return throwError(() => err);
          }

          const savingWeeklySchedule =
            /\/api\/Employees\/[^/]+\/weekly-schedule/i.test(req.url) && req.method === 'POST';

          const scheduleOverrideMutation =
            /\/api\/Employees\/[^/]+\/schedule-overrides/i.test(req.url) &&
            (req.method === 'POST' || req.method === 'DELETE');

          if (savingWeeklySchedule) {
            summary = 'Błąd zapisu grafiku';
            errorMessage = apiErrorMessage(body, errorMessage);
          }

          if (scheduleOverrideMutation) {
            summary = 'Dzień specjalny';
            errorMessage = apiErrorMessage(body, errorMessage);
          }

          // Wysyłka linku do zadatku padła po stronie operatora (smsapi/SMTP) — to nie błąd danych
          // wpisanych przez personel, więc „Błąd walidacji" wprowadzałby w błąd.
          if (body?.errorCode === 'deposit.send_failed') {
            summary = 'Nie wysłano';
          }

          /**
           * Pollingi tła (centrum powiadomień, dashboard heatmap) ustawiają `SILENT_ERRORS=true`,
           * by chwilowy 4xx/5xx nie spamował toastami przy każdej iteracji co 8 s.
           */
          if (!req.context.get(SILENT_ERRORS) && !suppressToastForAuthForms && !isDemoProbe) {
            messageService.add({
              severity: 'error',
              summary,
              detail: errorMessage,
              life: 6000
            });
          }

          return throwError(() => err);
        })
      );
    })
  );
};
