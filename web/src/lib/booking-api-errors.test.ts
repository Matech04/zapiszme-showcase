import { describe, it, expect } from 'vitest';
import { BookingApiException } from './booking-openapi-client';
import {
  bookingApiErrorMessage,
  bookingApiRetryAfterSeconds,
  classifyBookingError,
  isAbortError,
  shouldReportKind,
} from './booking-api-errors';

describe('bookingApiRetryAfterSeconds', () => {
  it('zwraca null dla zwykłego Error', () => {
    expect(bookingApiRetryAfterSeconds(new Error('x'))).toBeNull();
  });

  it('czyta Retry-After z nagłówków BookingApiException (mała litera)', () => {
    const e = new BookingApiException('Too Many', 429, '{}', { 'retry-after': '120' }, null);
    expect(bookingApiRetryAfterSeconds(e)).toBe(120);
  });

  it('czyta Retry-After z nagłówków (wielka litera)', () => {
    const e = new BookingApiException('Too Many', 429, '{}', { 'Retry-After': '60' }, null);
    expect(bookingApiRetryAfterSeconds(e)).toBe(60);
  });

  it('zwraca null gdy Retry-After nie jest dodatnią liczbą', () => {
    const e = new BookingApiException('x', 429, '{}', { 'retry-after': '0' }, null);
    expect(bookingApiRetryAfterSeconds(e)).toBeNull();
  });
});

describe('bookingApiErrorMessage', () => {
  it('używa centralnego komunikatu z errorCode przed detail', () => {
    const body = JSON.stringify({
      errorCode: 'appointment.otp.invalid_lease',
      detail: 'Techniczny fallback',
    });
    const e = new BookingApiException('HTTP', 403, body, {}, null);
    expect(bookingApiErrorMessage(e)).toBe('Sesja rezerwacji wygasła. Wybierz termin ponownie.');
  });

  it('mapuje errorCode missing_name na komunikat o imieniu i nazwisku', () => {
    const body = JSON.stringify({
      errorCode: 'appointment.otp.missing_name',
      detail: 'Techniczny fallback',
    });
    const e = new BookingApiException('HTTP', 400, body, {}, null);
    expect(bookingApiErrorMessage(e)).toBe('Podaj imię i nazwisko, aby zarezerwować wizytę.');
  });

  it('mapuje errorCode unsupported_phone_region na komunikat o polskim numerze', () => {
    const body = JSON.stringify({
      errorCode: 'appointment.otp.unsupported_phone_region',
      detail: 'Obsługujemy wyłącznie polskie numery telefonu (+48).',
    });
    const e = new BookingApiException('HTTP', 400, body, {}, null);
    expect(bookingApiErrorMessage(e)).toContain('polskie numery telefonu');
  });

  it('parsuje detail z JSON w odpowiedzi API', () => {
    const body = JSON.stringify({ title: 'Bad', detail: 'Brak wolnego terminu.' });
    const e = new BookingApiException('HTTP', 400, body, {}, null);
    expect(bookingApiErrorMessage(e)).toBe('Brak wolnego terminu.');
  });

  it('używa title gdy brak detail', () => {
    const body = JSON.stringify({ title: 'Validation failed' });
    const e = new BookingApiException('HTTP', 422, body, {}, null);
    expect(bookingApiErrorMessage(e)).toBe('Validation failed');
  });

  // Klientka salonu nie ma pojęcia, co znaczy „Server error" ani „Load failed" — dla 5xx
  // i awarii transportu pokazujemy stały, polski komunikat, a surowa treść idzie do logu.
  it('nie pokazuje angielskiej treści serwera przy 5xx', () => {
    const e = new BookingApiException('Server error', 500, 'plain text', {}, null);
    const message = bookingApiErrorMessage(e);
    expect(message).not.toContain('Server error');
    expect(message).toContain('po naszej stronie');
  });

  it('zamienia „Load failed" (Safari) na komunikat o braku połączenia', () => {
    expect(bookingApiErrorMessage(new TypeError('Load failed'))).toContain('połączenie');
  });

  it('zamienia „Failed to fetch" (Chrome) na komunikat o braku połączenia', () => {
    expect(bookingApiErrorMessage(new TypeError('Failed to fetch'))).toContain('połączenie');
  });

  it('nie wypuszcza treści zwykłego Error do UI', () => {
    expect(bookingApiErrorMessage(new Error('Cannot read properties of undefined'))).toBe(
      'Wystąpił nieoczekiwany błąd. Spróbuj ponownie.',
    );
  });

  it('zwraca domyślny tekst dla nieznanego typu', () => {
    expect(bookingApiErrorMessage(null)).toBe('Wystąpił nieoczekiwany błąd. Spróbuj ponownie.');
  });

  it('odpowiedź HTML zamiast JSON: klientka dostaje komunikat po ludzku, bez nazwy zmiennej buildu', () => {
    const e = new SyntaxError(`Unexpected token '<', "<!DOCTYPE "... is not valid JSON`);
    const message = bookingApiErrorMessage(e);
    expect(message).not.toContain('PUBLIC_API_BASE_URL');
    expect(message).toContain('systemem rezerwacji');
  });
});

describe('classifyBookingError', () => {
  it('rozpoznaje warianty komunikatów sieciowych każdej przeglądarki', () => {
    expect(classifyBookingError(new TypeError('Load failed'))).toBe('network');
    expect(classifyBookingError(new TypeError('Failed to fetch'))).toBe('network');
    expect(
      classifyBookingError(new TypeError('NetworkError when attempting to fetch resource.')),
    ).toBe('network');
  });

  it('przerwane żądanie nie jest awarią', () => {
    const abort = new Error('aborted');
    abort.name = 'AbortError';
    expect(classifyBookingError(abort)).toBe('aborted');
    expect(isAbortError(abort)).toBe(true);
  });

  it('rozdziela 404 / 429 / 5xx / 4xx', () => {
    expect(classifyBookingError(new BookingApiException('x', 404, '{}', {}, null))).toBe('not-found');
    expect(classifyBookingError(new BookingApiException('x', 429, '{}', {}, null))).toBe('rate-limit');
    expect(classifyBookingError(new BookingApiException('x', 503, '{}', {}, null))).toBe('server');
    expect(classifyBookingError(new BookingApiException('x', 400, '{}', {}, null))).toBe('request');
  });

  it('null/undefined nie wysadza klasyfikacji', () => {
    // Wygenerowany `BookingApiException.isBookingApiException` czyta pole bez sprawdzenia null.
    expect(classifyBookingError(null)).toBe('unknown');
    expect(classifyBookingError(undefined)).toBe('unknown');
  });
});

describe('shouldReportKind', () => {
  it('raportuje awarie techniczne, milczy przy błędach biznesowych', () => {
    expect(shouldReportKind('network')).toBe(true);
    expect(shouldReportKind('server')).toBe(true);
    expect(shouldReportKind('unknown')).toBe(true);
    // 4xx to normalna praca aplikacji (zajęty slot, zły kod) — nie zasypujemy sobie Seq.
    expect(shouldReportKind('request')).toBe(false);
    expect(shouldReportKind('rate-limit')).toBe(false);
    expect(shouldReportKind('aborted')).toBe(false);
  });
});
