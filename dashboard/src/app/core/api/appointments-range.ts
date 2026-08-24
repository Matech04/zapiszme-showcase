import { HttpClient, HttpContext, HttpContextToken } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AppointmentPreviewDto } from './api-client';

/**
 * Token kontekstu HTTP do wyciszania toastów błędów dla zapytań tła (np. polling).
 * Interceptor sprawdza ten token i pomija wyświetlenie toasta dla 4xx/5xx.
 */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);

function pad2(n: number): string {
  return String(n).padStart(2, '0');
}

/**
 * Formatuje datę do `YYYY-MM-DD`, zgodnego z modelem `DateOnly?` w backendzie.
 * NSwag-owy `getAppointmentsByRange` wysyła `toISOString()` (datetime), co
 * powoduje 400 (ModelBinding) — używamy raw HttpClient.
 */
export function formatDateOnly(d: Date): string {
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
}

export interface FetchAppointmentsRangeOptions {
  /** Domyślnie `false`. Gdy `true`, interceptor nie pokaże toastu na błąd. */
  silent?: boolean;
}

/**
 * Pobiera listę wizyt z zakresu daty. Akceptuje `Date` lub gotowy string `YYYY-MM-DD`.
 * Pomija parametr `employeeId` gdy nie jest podany.
 */
export function fetchAppointmentsRange(
  http: HttpClient,
  apiBaseUrl: string,
  start: Date | string,
  end: Date | string,
  employeeId?: string | null,
  options?: FetchAppointmentsRangeOptions,
): Observable<AppointmentPreviewDto[]> {
  const startStr = typeof start === 'string' ? start : formatDateOnly(start);
  const endStr = typeof end === 'string' ? end : formatDateOnly(end);
  const params: Record<string, string> = { startDate: startStr, endDate: endStr };
  if (employeeId) params['employeeId'] = employeeId;
  return http.get<AppointmentPreviewDto[]>(`${apiBaseUrl}/api/Appointments`, {
    params,
    context: new HttpContext().set(SILENT_ERRORS, options?.silent === true),
  });
}
