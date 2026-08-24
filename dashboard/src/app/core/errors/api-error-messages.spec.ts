import { describe, expect, it } from 'vitest';
import { apiErrorMessage, formatProblemDetailsBody } from './api-error-messages';

describe('apiErrorMessage — błędy Identity (klucze w `errors`)', () => {
  it('tłumaczy pojedynczy kod Identity na polski (zamiast generycznego title)', () => {
    const body = {
      title: 'Nie udało się utworzyć konta użytkownika.',
      errors: { PasswordRequiresUpper: ['Passwords must have at least one uppercase.'] },
    };
    expect(apiErrorMessage(body, 'fallback')).toBe('Hasło musi zawierać wielką literę.');
  });

  it('łączy wiele kodów Identity w jeden komunikat', () => {
    const body = {
      title: 'Nie udało się utworzyć konta użytkownika.',
      errors: {
        PasswordRequiresDigit: ['x'],
        PasswordRequiresNonAlphanumeric: ['y'],
      },
    };
    const msg = apiErrorMessage(body, 'fallback');
    expect(msg).toContain('Hasło musi zawierać cyfrę.');
    expect(msg).toContain('Hasło musi zawierać znak specjalny (np. !, @, #).');
  });

  it('mapuje zajęty e-mail (DuplicateUserName)', () => {
    const body = { title: 'x', errors: { DuplicateUserName: ['taken'] } };
    expect(apiErrorMessage(body, 'fallback')).toBe('Ten adres e-mail jest już zajęty.');
  });

  it('kod błędu (errorCode) ma priorytet nad mapowaniem Identity', () => {
    const body = { errorCode: 'rate_limit.exceeded', errors: { PasswordTooShort: ['x'] } };
    expect(apiErrorMessage(body, 'fallback')).toBe(
      'Wykonujesz zbyt wiele żądań. Spróbuj ponownie później.',
    );
  });

  it('nieznane klucze `errors` (np. FluentValidation po polu) nie są traktowane jak Identity → fallback do title', () => {
    const body = { title: 'Coś poszło nie tak', errors: { Email: ['Pole wymagane'] } };
    expect(apiErrorMessage(body, 'fallback')).toBe('Coś poszło nie tak');
  });

  // Wygasły link zaproszenia/resetu: Identity zwraca kod `InvalidToken`. TOAST idzie przez apiErrorMessage.
  it('mapuje InvalidToken (wygasły link) na polski komunikat — ścieżka toasta', () => {
    const body = {
      title: 'Nie udało się utworzyć konta użytkownika.',
      errors: { InvalidToken: ['Invalid token.'] },
    };
    const msg = apiErrorMessage(body, 'fallback');
    expect(msg).toContain('Link wygasł');
    expect(msg).not.toContain('Invalid token');
  });
});

// KARTA na ekranie reset/accept-invite renderuje błąd przez formatAuthApiError → formatProblemDetailsBody
// (locale 'pl' → translateAuthValidationEntry). To OSOBNY katalog niż mapa toasta — regresja: pokazywał
// surowe angielskie „Invalid token." pod polskim tytułem.
describe('formatProblemDetailsBody — tłumaczenie błędów Identity w karcie (locale pl)', () => {
  it('InvalidToken → polski komunikat, bez angielskiego fallbacku', () => {
    const body = {
      title: 'Nie udało się utworzyć konta użytkownika.',
      errors: { InvalidToken: ['Invalid token.'] },
    };
    const msg = formatProblemDetailsBody(body, { translateAuthErrorsForLocale: 'pl' });
    expect(msg).toContain('Link wygasł');
    expect(msg).not.toContain('Invalid token');
  });
});
