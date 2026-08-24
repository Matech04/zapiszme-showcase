/**
 * Teksty walidacji auth (Identity + typowe klucze ASP.NET) — źródło prawdy dla UI.
 * Docelowo: osobne pliki `auth-validation.en.ts` i wybór katalogu wg ustawień języka użytkownika.
 */

export type AuthLocaleId = 'pl';

/** Domyślny locale do czasu podpięcia serwisu i18n. */
export const DEFAULT_AUTH_LOCALE: AuthLocaleId = 'pl';

const validationMessagesPl: Record<string, string> = {
  DefaultError: 'Wystąpił nieznany błąd walidacji.',
  DuplicateEmail: 'Ten adres e-mail jest już zarejestrowany.',
  DuplicateUserName: 'Ta nazwa użytkownika jest już zajęta.',
  InvalidEmail: 'Podany adres e-mail jest nieprawidłowy.',
  InvalidUserName: 'Podana nazwa użytkownika jest nieprawidłowa.',
  PasswordMismatch: 'Hasła nie są takie same.',
  PasswordRequiresDigit: 'Hasło musi zawierać co najmniej jedną cyfrę (0–9).',
  PasswordRequiresLower: 'Hasło musi zawierać co najmniej jedną małą literę (a–z).',
  PasswordRequiresNonAlphanumeric: 'Hasło musi zawierać co najmniej jeden znak specjalny (np. !, @, #).',
  PasswordRequiresUpper: 'Hasło musi zawierać co najmniej jedną wielką literę (A–Z).',
  PasswordTooShort: 'Hasło jest zbyt krótkie.',
  PasswordRequiresUniqueChars: 'Hasło musi zawierać wymaganą liczbę różnych znaków.',
  // Identity zwraca `InvalidToken` zarówno dla wygasłego, jak i uszkodzonego linku — nie rozróżnia.
  InvalidToken: 'Link wygasł lub jest nieprawidłowy. Poproś o nowe zaproszenie lub link do resetu hasła.',
  ExpiredToken: 'Link wygasł. Poproś o nowe zaproszenie lub link do resetu hasła.',
  UserNotFound: 'Nie znaleziono konta dla tego linku.',
};

function firstInt(text: string | undefined): string | null {
  const m = text?.match(/\d+/);
  return m ? m[0] : null;
}

function toCamelCase(key: string): string {
  if (!key) {
    return key;
  }
  if (key.length === 1) {
    return key.toLowerCase();
  }
  return key[0].toLowerCase() + key.slice(1);
}

function getValidationCatalog(locale: AuthLocaleId): Record<string, string> {
  switch (locale) {
    case 'pl':
      return validationMessagesPl;
    default:
      return validationMessagesPl;
  }
}

/**
 * Zwraca komunikaty do pokazania użytkownikowi dla jednego wpisu `errors` z ValidationProblemDetails.
 * Preferuje tłumaczenie po kluczu (kod Identity / nazwa pola); treść z API zostaje tylko jako fallback.
 */
export function translateAuthValidationEntry(
  apiKey: string,
  serverMessages: string[],
  locale: AuthLocaleId = DEFAULT_AUTH_LOCALE,
): string[] {
  const catalog = getValidationCatalog(locale);
  const primary = serverMessages[0];

  if (apiKey === 'PasswordTooShort') {
    const n = firstInt(primary);
    if (n) {
      return [`Hasło musi mieć co najmniej ${n} znaków.`];
    }
  }
  if (apiKey === 'PasswordRequiresUniqueChars') {
    const n = firstInt(primary);
    if (n) {
      return [`Hasło musi zawierać co najmniej ${n} różnych znaków.`];
    }
  }

  const fromCatalog = catalog[apiKey] ?? catalog[toCamelCase(apiKey)];
  if (fromCatalog) {
    return [fromCatalog];
  }

  return serverMessages.length > 0 ? serverMessages : [];
}
