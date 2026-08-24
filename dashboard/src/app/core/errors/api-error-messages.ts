import { HttpErrorResponse } from '@angular/common/http';
import { ApiException } from '@core/api/api-client';
import { parseAuthProblemJsonString } from '@core/errors/auth-problem-json';
import { DEFAULT_AUTH_LOCALE, translateAuthValidationEntry, type AuthLocaleId } from '@core/i18n/auth-validation.catalog';

export interface ApiProblemDetails {
  title?: string;
  detail?: string;
  errorCode?: string;
  messageKey?: string;
  errors?: Record<string, string[]>;
}

const errorMessages: Record<string, string> = {
  'validation.failed': 'Sprawdź poprawność danych formularza.',
  'validation.invalid_argument': 'Podane dane są nieprawidłowe.',
  'resource.not_found': 'Nie znaleziono zasobu.',
  'auth.unauthorized': 'Zaloguj się ponownie.',
  'auth.forbidden': 'Nie masz uprawnień do tej akcji.',
  // Sesja jest ważna — wygasł tylko token antiforgery. Celowo NIE brzmi jak „zaloguj się ponownie",
  // bo odświeżenie strony wystarczy i sugerowanie logowania wysyłałoby użytkownika w ślepy zaułek.
  'auth.antiforgery_invalid': 'Strona jest otwarta zbyt długo. Odśwież ją i spróbuj ponownie.',
  'tenant.violation': 'Nie możesz wykonać tej operacji w tym salonie.',
  'tenant.missing': 'Nie udało się ustalić kontekstu salonu.',
  'tenant.custom_domain.already_assigned': 'Ta domena jest już przypisana do innego salonu.',
  'auth.identity_employee_missing': 'Konto nie jest powiązane z pracownikiem w systemie.',
  'rate_limit.exceeded': 'Wykonujesz zbyt wiele żądań. Spróbuj ponownie później.',
  'persistence.failed': 'Nie udało się zapisać zmian. Spróbuj ponownie.',
  'internal.error': 'Wystąpił nieoczekiwany błąd serwera.',
  'image.invalid': 'Nie udało się przetworzyć pliku — upewnij się, że to poprawne zdjęcie (JPG lub PNG).',
  'image.too_large': 'Zdjęcie jest za duże. Maksymalny rozmiar to 5 MB.',
  'image.unsupported_format': 'Nieobsługiwany format pliku. Dozwolone są JPG i PNG.',
  'appointment.invalid_time_range': 'Godzina zakończenia musi być późniejsza niż godzina rozpoczęcia.',
  'appointment.slot_unavailable': 'Wybrany termin jest niedostępny albo został już zajęty.',
  'appointment.invalid_status': 'Wybrany status wizyty jest nieprawidłowy.',
  'appointment.completed_cannot_be_canceled': 'Nie można anulować zakończonej wizyty.',
  'appointment.otp.invalid_lease': 'Sesja rezerwacji wygasła. Wybierz termin ponownie.',
  'appointment.otp.missing_contact': 'Podaj numer telefonu albo adres e-mail.',
  'appointment.otp.verification_required': 'Najpierw poproś o kod weryfikacyjny.',
  'appointment.otp.too_many_failures': 'Przekroczono liczbę prób. Poproś o nowy kod.',
  'appointment.otp.invalid_code': 'Kod OTP jest nieprawidłowy albo wygasł.',
  'appointment.swap.terminal_status': 'Nie można zamienić wizyty, która została zakończona lub anulowana.',
  'appointment.swap.same_appointment': 'Nie można zamienić wizyty z samą sobą.',
  'appointment.swap.harmonization_unavailable': 'Nie można skrócić wizyty: pracownik nie ma przypisanej krótszej usługi.',
  'appointment.zero_duration': 'Wizyta musi zawierać usługę z czasem trwania (sam dodatek nie wystarczy).',
  'appointment.addon_requires_main': 'Usługa dodatkowa wymaga wybrania usługi głównej.',
  'appointment.addon_not_allowed': 'Wybrany dodatek nie pasuje do wybranej usługi głównej.',
  'service.addon_invalid': 'Wybrany dodatek jest nieprawidłowy (nie istnieje lub nie jest dodatkiem).',
  'employee.service_already_assigned': 'Ta usługa jest już przypisana do pracownika.',
  'employee.service_missing': 'Pracownik nie ma przypisanej tej usługi.',
  'employee.no_linked_account': 'Konto nie jest powiązane z pracownikiem w tym salonie.',
  'employee.cannot_mutate_other_profile': 'Możesz edytować tylko własny profil.',
  'leave.invalid_dates': 'Daty urlopu są nieprawidłowe.',
  'leave.overlap': 'Urlopy pracownika nie mogą się nakładać.',
  'leave.only_upcoming_may_be_removed': 'Można usunąć tylko nadchodzący urlop.',
  'schedule.overlapping_shifts': 'Przedziały godzin nakładają się na siebie.',
  'schedule.break_not_within_work_range': 'Przerwa musi mieścić się w godzinach pracy.',
  'schedule.invalid_days_count': 'Liczba dni grafiku przekracza limit cyklu.',
  'schedule.invalid_cycle_index': 'Nieprawidłowy indeks dnia w cyklu.',
  'schedule.days_collision': 'Dwa dni grafiku mają ten sam indeks cyklu.',
  'schedule.schedules_collision': 'Zakresy obowiązywania grafików nakładają się.',
  'date_range.invalid': 'Zakres dat jest nieprawidłowy.',
  'time_range.invalid': 'Zakres godzin jest nieprawidłowy.',
  'customer_verification.phone_disabled': 'Weryfikacja telefoniczna jest tymczasowo niedostępna.',
  'currency.invalid_length': 'Kod waluty musi mieć dokładnie 3 znaki.',
  'validation.value_too_long': 'Wartość jest za długa.',
  'auth.phone_not_confirmed': 'Numer telefonu nie został potwierdzony. Wpisz kod z SMS-a.',
  'auth.phone_otp_invalid': 'Nieprawidłowy kod SMS. Spróbuj ponownie.',
  'auth.phone_otp_expired': 'Kod SMS wygasł. Poproś o nowy.',
  'auth.phone_otp_locked': 'Zbyt wiele nieudanych prób. Poproś o nowy kod.',
  'auth.phone_otp_cooldown': 'Odczekaj chwilę przed ponowną wysyłką kodu.',
  'auth.phone_already_confirmed': 'Telefon jest już potwierdzony.',
  'auth.phone_email_not_confirmed': 'Najpierw potwierdź adres email — kod SMS będzie dostępny po potwierdzeniu maila.',
  'auth.sms_service_unavailable': 'Usługa SMS jest tymczasowo niedostępna. Spróbuj za chwilę.',
  'auth.registration_conflict': 'Nie udało się utworzyć konta. Sprawdź wprowadzone dane.',
  'onboarding.not_verified':
    'Najpierw potwierdź adres e-mail i numer telefonu, zanim utworzysz salon.',
  'tenant.slug_taken': 'Ten link jest już zajęty — wybierz inny publiczny adres.',
  'impersonation.tenant_not_found': 'Nie znaleziono salonu do wsparcia.',
  'impersonation.tenant_demo': 'Nie można wejść w tryb wsparcia na salonie demo.',
  'impersonation.read_only': 'Sesja wsparcia działa w trybie tylko do odczytu — zapis jest zablokowany.',
  'impersonation.session_inactive': 'Sesja wsparcia wygasła lub została zakończona.',
  'deposit.already_paid': 'Zadatek za tę wizytę został już opłacony.',
  'deposit.not_paid': 'Nie można zwrócić zadatku, który nie został opłacony.',
  'deposit.terminal_appointment': 'Nie można wygenerować zadatku dla zakończonej lub anulowanej wizyty.',
  'deposit.not_enabled': 'Zadatki są wyłączone. Włącz je w Ustawieniach → Zadatki.',
  'merchant_account.not_connected': 'Konto płatności nie jest połączone. Połącz Stripe w Ustawieniach → Zadatki.',
  'merchant_account.not_ready': 'Konto płatności nie jest jeszcze gotowe. Dokończ konfigurację Stripe.',
  'deposit.link_not_generated': 'Najpierw wygeneruj link do zadatku.',
  'deposit.customer_contact_missing': 'Klient nie ma zapisanego numeru telefonu / adresu e-mail do wysyłki.',
  'deposit.sms_cap_reached': 'Miesięczny limit SMS osiągnięty — wyślij e-mailem lub skopiuj link ręcznie.',
  'deposit.amount_exceeds_total': 'Kwota zadatku nie może przekraczać ceny wizyty.',
  'deposit.send_failed':
    'Nie udało się wysłać linku do zadatku — operator odrzucił wiadomość. Skopiuj link i wyślij go ręcznie.',
  'deposit.send_cooldown': 'Link został właśnie wysłany. Odczekaj kilka minut przed ponowną wysyłką.',
  'sms_template.type_not_customizable': 'Tego typu powiadomienia nie można personalizować.',
  'sms_template.body_empty': 'Treść szablonu SMS nie może być pusta.',
  'sms_template.too_long': 'Treść przekracza limit znaków dla wybranego kodowania (140 GSM / 70 z polskimi znakami).',
  'sms_template.invalid_placeholder': 'Użyto niedozwolonego znacznika dla tego typu powiadomienia.',
  'sms_template.not_pending': 'Ten szablon nie oczekuje już na akceptację.',
  'booking.paused': 'Rezerwacje w tym salonie są chwilowo wstrzymane.',
  'platform.maintenance': 'Trwają prace serwisowe platformy — zapisywanie zmian jest chwilowo wstrzymane. Spróbuj ponownie za chwilę.',
};

/**
 * Kody błędów ASP.NET Identity (login/hasło) → polski. Backend zwraca je jako KLUCZE w `errors`
 * (np. `{ "PasswordRequiresUpper": [...] }`), tłumaczenie jest po stronie frontendu.
 */
const identityErrorMessages: Record<string, string> = {
  DuplicateUserName: 'Ten adres e-mail jest już zajęty.',
  DuplicateEmail: 'Ten adres e-mail jest już zajęty.',
  InvalidUserName: 'Podaj poprawny adres e-mail.',
  InvalidEmail: 'Podaj poprawny adres e-mail.',
  PasswordTooShort: 'Hasło musi mieć co najmniej 8 znaków.',
  PasswordRequiresDigit: 'Hasło musi zawierać cyfrę.',
  PasswordRequiresLower: 'Hasło musi zawierać małą literę.',
  PasswordRequiresUpper: 'Hasło musi zawierać wielką literę.',
  PasswordRequiresNonAlphanumeric: 'Hasło musi zawierać znak specjalny (np. !, @, #).',
  PasswordRequiresUniqueChars: 'Hasło musi zawierać więcej różnych znaków.',
  PasswordMismatch: 'Nieprawidłowe obecne hasło.',
  InvalidToken: 'Link wygasł lub jest nieprawidłowy. Poproś o nowe zaproszenie / link do resetu hasła.',
  ExpiredToken: 'Link wygasł. Poproś o nowe zaproszenie / link do resetu hasła.',
  UserNotFound: 'Nie znaleziono konta dla tego linku.',
};

/** Składa polski komunikat z kodów Identity obecnych w `errors`. null = brak dopasowania. */
function identityErrorsToMessage(errors: Record<string, string[]> | undefined): string | null {
  if (!errors) {
    return null;
  }
  const messages = Object.keys(errors)
    .map((code) => identityErrorMessages[code])
    .filter((m): m is string => !!m);
  const unique = [...new Set(messages)];
  return unique.length ? unique.join(' ') : null;
}

export function apiErrorMessage(body: ApiProblemDetails | null | undefined, fallback: string): string {
  const code = body?.errorCode ?? body?.messageKey;
  if (code && errorMessages[code]) {
    return errorMessages[code];
  }

  // Błędy Identity (klucze w `errors`) — muszą wyprzedzić `title`, bo backend daje generyczny tytuł.
  const identity = identityErrorsToMessage(body?.errors);
  if (identity) {
    return identity;
  }

  return body?.detail || body?.title || fallback;
}

function readProblemField(body: Record<string, unknown>, camel: string, pascal: string): unknown {
  const c = body[camel];
  if (c !== undefined && c !== null) {
    return c;
  }
  return body[pascal];
}

function normalizeStringArray(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return value
    .filter((m): m is string => typeof m === 'string' && m.trim().length > 0)
    .map((m) => m.trim());
}

/** Z RFC 7807 / ASP.NET ProblemDetails + ValidationProblemDetails (także PascalCase). */
export function formatProblemDetailsBody(
  body: Record<string, unknown> | null | undefined,
  options?: { translateAuthErrorsForLocale?: AuthLocaleId },
): string | null {
  if (!body || typeof body !== 'object') {
    return null;
  }

  const errorsRaw = readProblemField(body, 'errors', 'Errors');
  const lines: string[] = [];
  if (errorsRaw && typeof errorsRaw === 'object' && !Array.isArray(errorsRaw)) {
    if (options?.translateAuthErrorsForLocale) {
      const loc = options.translateAuthErrorsForLocale;
      for (const [key, msgs] of Object.entries(errorsRaw as Record<string, unknown>)) {
        const translated = translateAuthValidationEntry(key, normalizeStringArray(msgs), loc);
        lines.push(...translated);
      }
    } else {
      for (const msgs of Object.values(errorsRaw as Record<string, unknown>)) {
        if (Array.isArray(msgs)) {
          for (const m of msgs) {
            if (typeof m === 'string' && m.trim()) {
              lines.push(m.trim());
            }
          }
        }
      }
    }
  }

  const title = readProblemField(body, 'title', 'Title');
  const detail = readProblemField(body, 'detail', 'Detail');
  const titleStr = typeof title === 'string' ? title.trim() : '';
  const detailStr = typeof detail === 'string' ? detail.trim() : '';

  if (lines.length > 0) {
    const header = detailStr || titleStr;
    const bullets = lines.map((l) => `• ${l}`).join('\n');
    return header ? `${header}\n\n${bullets}` : bullets;
  }

  if (titleStr && detailStr && titleStr !== detailStr) {
    return `${titleStr}\n${detailStr}`;
  }

  return titleStr || detailStr || null;
}

function fallbackForHttpStatus(status: number): string {
  switch (status) {
    case 0:
      return 'Brak połączenia z serwerem. Sprawdź internet i spróbuj ponownie.';
    case 400:
      return 'Serwer odrzucił żądanie. Sprawdź dane i spróbuj ponownie.';
    case 401:
      return 'Nieprawidłowy email lub hasło.';
    case 403:
      return 'Brak uprawnień do wykonania tej operacji.';
    case 404:
      return 'Nie znaleziono zasobu.';
    case 409:
      return 'Żądanie jest w konflikcie z aktualnym stanem (np. zajęty adres salonu).';
    case 429:
      return 'Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.';
    case 503:
      return 'Serwis jest chwilowo niedostępny.';
    default:
      return 'Wystąpił błąd serwera. Spróbuj ponownie za chwilę.';
  }
}

function formatFromApiException(ex: ApiException): string {
  const parsed = parseAuthProblemJsonString(ex.response ?? '');
  if (parsed) {
    const fromBody = formatProblemDetailsBody(parsed, { translateAuthErrorsForLocale: DEFAULT_AUTH_LOCALE });
    if (fromBody) {
      return fromBody;
    }
    const fromCodes = apiErrorMessage(parsed as ApiProblemDetails, '');
    if (fromCodes) {
      return fromCodes;
    }
  }

  return fallbackForHttpStatus(ex.status);
}

/**
 * Komunikat dla użytkownika z odpowiedzi API (logowanie, rejestracja, NSwag ApiException).
 */
export function formatAuthApiError(error: unknown): string {
  if (ApiException.isApiException(error) || error instanceof ApiException) {
    try {
      return formatFromApiException(error as ApiException);
    } catch {
      return fallbackForHttpStatus((error as ApiException).status);
    }
  }

  if (error instanceof HttpErrorResponse) {
    const body = error.error;
    if (typeof body === 'string') {
      const parsed = parseAuthProblemJsonString(body);
      if (parsed) {
        const msg = formatProblemDetailsBody(parsed, {
          translateAuthErrorsForLocale: DEFAULT_AUTH_LOCALE,
        });
        if (msg) {
          return msg;
        }
      }
    }
    if (body && typeof body === 'object' && !Array.isArray(body) && !(typeof Blob !== 'undefined' && body instanceof Blob)) {
      const msg = formatProblemDetailsBody(body as Record<string, unknown>, {
        translateAuthErrorsForLocale: DEFAULT_AUTH_LOCALE,
      });
      if (msg) {
        return msg;
      }
    }
    return fallbackForHttpStatus(error.status);
  }

  return 'Wystąpił nieoczekiwany błąd. Spróbuj ponownie.';
}
