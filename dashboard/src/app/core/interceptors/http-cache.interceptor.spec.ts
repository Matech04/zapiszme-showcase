import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { httpCacheInterceptor, resetHttpCache, SKIP_HTTP_CACHE } from './http-cache.interceptor';

/**
 * Deduplikacja i krótki cache GET-ów. Powód powstania: wejście w kalendarz wysyłało ten sam URL
 * wielokrotnie w jednym cyklu montowania (/api/Employees ×4, /api/ServiceCategories ×3,
 * trzy zapytania o wizyty po dwa razy) — każdy komponent fetchował niezależnie.
 *
 * Testy pilnują granicy bezpieczeństwa: współdzielenie w locie wolno wszystkim, ale cache z TTL
 * TYLKO danym referencyjnym, i każda mutacja musi go unieważnić.
 */
describe('httpCacheInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    resetHttpCache();
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([httpCacheInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
    resetHttpCache();
  });

  it('scala równoległe identyczne GET-y w jeden round-trip', () => {
    const wyniki: unknown[] = [];
    http.get('/api/Employees').subscribe((r) => wyniki.push(r));
    http.get('/api/Employees').subscribe((r) => wyniki.push(r));
    http.get('/api/Employees').subscribe((r) => wyniki.push(r));

    // Jedno żądanie do sieci, ale wszyscy trzej wołający dostają odpowiedź.
    backend.expectOne('/api/Employees').flush([{ id: 'e1' }]);
    expect(wyniki).toHaveLength(3);
    backend.verify();
  });

  it('współdzieli w locie także endpointy spoza allowlisty (to nie może być nieświeże)', () => {
    // Wizyty NIE są cache'owane z TTL, ale scalanie żądań w locie jest bezpieczne z definicji:
    // to dosłownie ta sama odpowiedź, na którą drugi wołający i tak by czekał.
    http.get('/api/Appointments?startDate=2026-08-03').subscribe();
    http.get('/api/Appointments?startDate=2026-08-03').subscribe();
    backend.expectOne('/api/Appointments?startDate=2026-08-03').flush([]);
    backend.verify();
  });

  it('dane referencyjne serwuje z cache w obrębie TTL', () => {
    http.get('/api/Services').subscribe();
    backend.expectOne('/api/Services').flush([{ id: 's1' }]);

    vi.advanceTimersByTime(1000);
    let drugi: unknown = null;
    http.get('/api/Services').subscribe((r) => (drugi = r));

    backend.expectNone('/api/Services');
    expect(drugi).toEqual([{ id: 's1' }]);
  });

  it('po TTL pobiera na nowo', () => {
    http.get('/api/Services').subscribe();
    backend.expectOne('/api/Services').flush([{ id: 's1' }]);

    vi.advanceTimersByTime(5001);
    http.get('/api/Services').subscribe();
    backend.expectOne('/api/Services').flush([{ id: 's2' }]);
    backend.verify();
  });

  it('NIE cache’uje danych na żywo — wizyty lecą ponownie po zakończeniu pierwszego żądania', () => {
    http.get('/api/Appointments').subscribe();
    backend.expectOne('/api/Appointments').flush([]);

    vi.advanceTimersByTime(10);
    http.get('/api/Appointments').subscribe();
    // Brak TTL dla wizyt → drugie żądanie MUSI polecieć, inaczej kalendarz kłamałby po zmianie statusu.
    backend.expectOne('/api/Appointments').flush([]);
    backend.verify();
  });

  it('REGRESJA: mutacja unieważnia cache — inaczej panel pokazywałby stan sprzed zapisu', () => {
    http.get('/api/Employees').subscribe();
    backend.expectOne('/api/Employees').flush([{ id: 'e1' }]);

    http.post('/api/Employees', { firstName: 'Nowa' }).subscribe();
    backend.expectOne((r) => r.method === 'POST').flush({});

    // Wciąż w obrębie TTL, ale po mutacji — musi polecieć świeże zapytanie.
    vi.advanceTimersByTime(100);
    let po: unknown = null;
    http.get('/api/Employees').subscribe((r) => (po = r));
    backend.expectOne('/api/Employees').flush([{ id: 'e1' }, { id: 'e2' }]);
    expect(po).toEqual([{ id: 'e1' }, { id: 'e2' }]);
    backend.verify();
  });

  it('REGRESJA: handshake SignalR (POST /hubs/) NIE czyści cache — leci przy każdym montowaniu', () => {
    http.get('/api/Employees').subscribe();
    backend.expectOne('/api/Employees').flush([{ id: 'e1' }]);

    // `/hubs/notifications/negotiate` to POST, ale niczego nie mutuje. Traktowany jak mutacja
    // wywalał świeżo pobranych pracowników z cache'u dokładnie w trakcie montowania ekranu,
    // więc kolejne komponenty szły po nich do sieci ponownie.
    http.post('/hubs/notifications/negotiate?negotiateVersion=1', {}).subscribe();
    backend.expectOne((r) => r.url.includes('/hubs/')).flush({});

    vi.advanceTimersByTime(100);
    let po: unknown = null;
    http.get('/api/Employees').subscribe((r) => (po = r));
    // Brak expectOne dla GET-a: odpowiedź musi przyjść z cache'u, bez round-tripu.
    expect(po).toEqual([{ id: 'e1' }]);
    backend.verify();
  });

  it('każda metoda mutująca czyści cache, nie tylko POST', () => {
    for (const wykonaj of [
      () => http.put('/api/Appointments/1', {}),
      () => http.patch('/api/Appointments/1/status', {}),
      () => http.delete('/api/Appointments/1'),
    ]) {
      resetHttpCache();
      http.get('/api/SalonSettings').subscribe();
      backend.expectOne('/api/SalonSettings').flush({ v: 1 });

      wykonaj().subscribe();
      backend.expectOne((r) => r.method !== 'GET').flush({});

      http.get('/api/SalonSettings').subscribe();
      backend.expectOne('/api/SalonSettings').flush({ v: 2 });
    }
    backend.verify();
  });

  it('SKIP_HTTP_CACHE omija cache (świadomy hard-refresh)', () => {
    http.get('/api/Services').subscribe();
    backend.expectOne('/api/Services').flush([{ id: 's1' }]);

    http
      .get('/api/Services', { context: new HttpContext().set(SKIP_HTTP_CACHE, true) })
      .subscribe();
    backend.expectOne('/api/Services').flush([{ id: 's-fresh' }]);
    backend.verify();
  });

  it('różne parametry to różne wpisy — nie sklejamy zakresów dat', () => {
    http.get('/api/Employees?a=1').subscribe();
    http.get('/api/Employees?a=2').subscribe();
    backend.expectOne('/api/Employees?a=1').flush([]);
    backend.expectOne('/api/Employees?a=2').flush([]);
    backend.verify();
  });

  it('błąd nie trafia do cache — kolejne wywołanie próbuje ponownie', () => {
    http.get('/api/Services').subscribe({ error: () => undefined });
    backend.expectOne('/api/Services').flush('bum', { status: 500, statusText: 'Server Error' });

    http.get('/api/Services').subscribe({ error: () => undefined });
    backend.expectOne('/api/Services').flush([{ id: 's1' }]);
    backend.verify();
  });
});
