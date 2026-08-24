import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-client';

/**
 * Wywołania API dla nadpisań grafiku, które wymagają ręcznego URL (np. DateOnly w ścieżce jako yyyy-MM-dd).
 * Nie edytujemy wygenerowanego EmployeesClient (NSwag wstawia toISOString() i API zwraca 400).
 */
@Injectable({ providedIn: 'root' })
export class ScheduleOverridesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  removeOverride(employeeId: string, dateYyyyMmDd: string): Observable<unknown> {
    const url =
      `${this.baseUrl}/api/Employees/${encodeURIComponent(employeeId)}/schedule-overrides/` +
      `${encodeURIComponent(dateYyyyMmDd)}`;
    return this.http.delete(url, {
      observe: 'response',
      responseType: 'blob',
      headers: new HttpHeaders({ Accept: 'application/octet-stream' })
    });
  }
}
