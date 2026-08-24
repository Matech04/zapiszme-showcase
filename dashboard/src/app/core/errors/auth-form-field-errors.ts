import { HttpErrorResponse } from '@angular/common/http';
import { ApiException } from '@core/api/api-client';
import { formatAuthApiError, formatProblemDetailsBody } from '@core/errors/api-error-messages';
import { parseAuthProblemJsonString } from '@core/errors/auth-problem-json';
import {
  DEFAULT_AUTH_LOCALE,
  translateAuthValidationEntry,
  type AuthLocaleId,
} from '@core/i18n/auth-validation.catalog';

const REGISTER_OWNER_FIELDS = new Set([
  'salonName',
  'salonSlug',
  'firstName',
  'lastName',
  'displayName',
  'timeZoneId',
  'currency',
  'email',
  'password',
]);

/** Klucze ValidationProblemDetails z Identity — kod błędu, nie nazwa pola. */
const IDENTITY_CODE_TO_REGISTER_FIELD: Record<string, string> = {
  PasswordTooShort: 'password',
  PasswordRequiresDigit: 'password',
  PasswordRequiresLower: 'password',
  PasswordRequiresUpper: 'password',
  PasswordRequiresNonAlphanumeric: 'password',
  PasswordRequiresUniqueChars: 'password',
  PasswordMismatch: 'password',
  DuplicateEmail: 'email',
  DuplicateUserName: 'email',
  InvalidEmail: 'email',
  InvalidUserName: 'email',
};

function readErrorsMap(body: Record<string, unknown>): Record<string, unknown> | null {
  const raw = body['errors'] ?? body['Errors'];
  if (raw && typeof raw === 'object' && !Array.isArray(raw)) {
    return raw as Record<string, unknown>;
  }
  return null;
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

function normalizeMessageList(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }
  return value
    .filter((m): m is string => typeof m === 'string' && m.trim().length > 0)
    .map((m) => m.trim());
}

function pushUnique(target: string[], items: string[]): void {
  for (const item of items) {
    if (!target.includes(item)) {
      target.push(item);
    }
  }
}

const LOGIN_FORM_FIELDS = new Set(['email', 'password']);

/** Kody Identity / ASP.NET mapowane na pola logowania (gdy API zwraca ValidationProblemDetails). */
const IDENTITY_CODE_TO_LOGIN_FIELD: Record<string, string> = {
  PasswordTooShort: 'password',
  PasswordRequiresDigit: 'password',
  PasswordRequiresLower: 'password',
  PasswordRequiresUpper: 'password',
  PasswordRequiresNonAlphanumeric: 'password',
  PasswordRequiresUniqueChars: 'password',
  PasswordMismatch: 'password',
  DuplicateEmail: 'email',
  DuplicateUserName: 'email',
  InvalidEmail: 'email',
  InvalidUserName: 'email',
};

function resolveLoginField(apiKey: string): string | null {
  const mapped = IDENTITY_CODE_TO_LOGIN_FIELD[apiKey];
  if (mapped) {
    return mapped;
  }
  const camel = toCamelCase(apiKey);
  if (LOGIN_FORM_FIELDS.has(camel)) {
    return camel;
  }
  const lower = apiKey.toLowerCase();
  for (const f of LOGIN_FORM_FIELDS) {
    if (f.toLowerCase() === lower) {
      return f;
    }
  }
  return null;
}

function resolveRegisterOwnerField(apiKey: string): string | null {
  const mapped = IDENTITY_CODE_TO_REGISTER_FIELD[apiKey];
  if (mapped) {
    return mapped;
  }
  const camel = toCamelCase(apiKey);
  if (REGISTER_OWNER_FIELDS.has(camel)) {
    return camel;
  }
  const lower = apiKey.toLowerCase();
  for (const f of REGISTER_OWNER_FIELDS) {
    if (f.toLowerCase() === lower) {
      return f;
    }
  }
  return null;
}

export function getAuthProblemJson(error: unknown): Record<string, unknown> | null {
  if (ApiException.isApiException(error) || error instanceof ApiException) {
    const ex = error as ApiException;
    return parseAuthProblemJsonString(ex.response ?? '');
  }
  if (error instanceof HttpErrorResponse) {
    const body = error.error;
    if (typeof body === 'string') {
      return parseAuthProblemJsonString(body);
    }
    if (body instanceof Blob) {
      return null;
    }
    if (body && typeof body === 'object' && !Array.isArray(body)) {
      return body as Record<string, unknown>;
    }
  }
  return null;
}

/**
 * Dzieli ValidationProblemDetails na pola formularza rejestracji właściciela
 * oraz opcjonalny komunikat globalny (np. Turnstile, błędy bez mapowania).
 */
export function partitionRegisterOwnerAuthErrors(
  error: unknown,
  locale: AuthLocaleId = DEFAULT_AUTH_LOCALE,
): {
  fieldErrors: Record<string, string[]>;
  globalMessage: string | null;
} {
  const parsed = getAuthProblemJson(error);
  if (!parsed) {
    return { fieldErrors: {}, globalMessage: 'Nie udało się odczytać odpowiedzi serwera.' };
  }

  const errObj = readErrorsMap(parsed);
  const fieldErrors: Record<string, string[]> = {};
  const globalLines: string[] = [];

  if (errObj) {
    for (const [key, raw] of Object.entries(errObj)) {
      const msgs = translateAuthValidationEntry(key, normalizeMessageList(raw), locale);
      if (msgs.length === 0) {
        continue;
      }
      const field = resolveRegisterOwnerField(key);
      if (field) {
        const bucket = fieldErrors[field] ?? [];
        pushUnique(bucket, msgs);
        fieldErrors[field] = bucket;
      } else {
        pushUnique(globalLines, msgs);
      }
    }
  }

  const hasFieldErrors = Object.keys(fieldErrors).length > 0;
  let globalMessage: string | null = null;

  if (globalLines.length > 0) {
    globalMessage = globalLines.map((l) => `• ${l}`).join('\n');
  } else if (!hasFieldErrors) {
    globalMessage = formatProblemDetailsBody(parsed, { translateAuthErrorsForLocale: locale });
  }

  return { fieldErrors, globalMessage };
}

/**
 * Dzieli odpowiedź błędu logowania na pola email/hasło oraz komunikat globalny
 * (typowe 401 z samym tytułem, Turnstile, błędy bez mapowania).
 */
export function partitionLoginAuthErrors(
  error: unknown,
  locale: AuthLocaleId = DEFAULT_AUTH_LOCALE,
): {
  fieldErrors: Record<string, string[]>;
  globalMessage: string | null;
} {
  const parsed = getAuthProblemJson(error);
  if (!parsed) {
    return { fieldErrors: {}, globalMessage: formatAuthApiError(error) };
  }

  const errObj = readErrorsMap(parsed);
  const fieldErrors: Record<string, string[]> = {};
  const globalLines: string[] = [];

  if (errObj) {
    for (const [key, raw] of Object.entries(errObj)) {
      const msgs = translateAuthValidationEntry(key, normalizeMessageList(raw), locale);
      if (msgs.length === 0) {
        continue;
      }
      const field = resolveLoginField(key);
      if (field) {
        const bucket = fieldErrors[field] ?? [];
        pushUnique(bucket, msgs);
        fieldErrors[field] = bucket;
      } else {
        pushUnique(globalLines, msgs);
      }
    }
  }

  const hasFieldErrors = Object.keys(fieldErrors).length > 0;
  let globalMessage: string | null = null;

  if (globalLines.length > 0) {
    globalMessage = globalLines.map((l) => `• ${l}`).join('\n');
  } else if (!hasFieldErrors) {
    globalMessage = formatProblemDetailsBody(parsed, { translateAuthErrorsForLocale: locale });
  }

  if (!globalMessage?.trim() && !hasFieldErrors) {
    globalMessage = formatAuthApiError(error);
  }

  return { fieldErrors, globalMessage };
}
