import { HttpContextToken, HttpEvent, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { Observable, of, tap } from 'rxjs';
import { finalize, shareReplay } from 'rxjs/operators';

/** Ustaw na `true`, żeby żądanie ominęło współdzielenie i cache (np. świadomy hard-refresh). */
export const SKIP_HTTP_CACHE = new HttpContextToken<boolean>(() => false);

/**
 * Endpointy danych referencyjnych — zmieniają się rzadko i są czytane przez wiele komponentów
 * naraz. Tylko one dostają krótkie TTL. Wizyty, powiadomienia i cokolwiek „na żywo" celowo NIE
 * są tu wymienione: dla nich działa wyłącznie współdzielenie żądań w locie, które z definicji
 * nie może zwrócić nieświeżych danych.
 */
const CACHEABLE = [
  '/api/Employees',
  '/api/Services',
  '/api/ServiceCategories',
  '/api/SalonSettings',
];

/**
 * TTL celowo krótkie: ma sklejać LAWINĘ zapytań przy montowaniu ekranu, a nie trzymać stanu
 * aplikacji. Dłuższe oszczędzałoby niewiele więcej, a zaczęłoby pokazywać cudze zmiany z opóźnieniem.
 */
const TTL_MS = 5000;

interface CacheEntry {
  response: HttpResponse<unknown>;
  storedAt: number;
}

const inFlight = new Map<string, Observable<HttpEvent<unknown>>>();
const cache = new Map<string, CacheEntry>();

function isCacheable(url: string): boolean {
  return CACHEABLE.some((prefix) => url.includes(prefix));
}

/**
 * Czy to mutacja danych aplikacji (a nie infrastruktura transportu, jak handshake SignalR
 * pod `/hubs/`). Tylko taka może unieważnić dane referencyjne.
 */
function isApiMutation(url: string): boolean {
  return url.includes('/api/');
}

/**
 * Deduplikacja i krótki cache zapytań GET.
 *
 * Powód: pomiar wejścia w kalendarz pokazał, że ten sam URL leci wielokrotnie w jednym cyklu
 * montowania — `/api/Employees` cztery razy, `/api/ServiceCategories` trzy, a trzy zapytania
 * o wizyty po dwa razy z identycznymi parametrami. Każdy komponent fetchuje niezależnie, bo
 * aplikacja nie ma żadnej warstwy współdzielenia.
 *
 * Dwa mechanizmy, świadomie o różnym poziomie ryzyka:
 *
 * 1. WSPÓŁDZIELENIE W LOCIE (wszystkie GET-y) — jeśli identyczne żądanie właśnie leci, drugi
 *    wołający dostaje tę samą odpowiedź zamiast własnego round-tripu. Nie da się tym podać
 *    nieświeżych danych: to dosłownie ta sama odpowiedź, na którą i tak by czekał.
 *
 * 2. CACHE Z TTL (tylko `CACHEABLE`) — pracownicy, usługi, kategorie, ustawienia salonu przez
 *    5 s. KAŻDA mutacja (cokolwiek poza GET) czyści cache w całości, więc dane zmienione
 *    z panelu są widoczne natychmiast. Zgrubne unieważnianie jest tu celowe: taniej i
 *    bezpieczniej niż mapowanie endpointów mutujących na czytane.
 */
export const httpCacheInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    // Mutacja = dane referencyjne mogły się zmienić. Czyścimy zgrubnie, zanim żądanie poleci,
    // żeby GET wystrzelony równolegle z odpowiedzią nie trafił w nieświeży wpis.
    //
    // ...ale TYLKO dla wywołań API. `POST /hubs/notifications/negotiate` (handshake SignalR)
    // niczego nie mutuje, a leci przy każdym (re)połączeniu — dokładnie w trakcie montowania
    // ekranu. Traktowany jak mutacja wywalał świeżo zapisanych pracowników/ustawienia z cache'u
    // i kolejne komponenty szły po nie do sieci jeszcze raz.
    if (isApiMutation(req.url)) cache.clear();
    return next(req);
  }

  if (req.context.get(SKIP_HTTP_CACHE)) return next(req);

  const key = req.urlWithParams;
  const cacheable = isCacheable(key);

  if (cacheable) {
    const hit = cache.get(key);
    if (hit && Date.now() - hit.storedAt < TTL_MS) {
      return of(hit.response.clone());
    }
    if (hit) cache.delete(key);
  }

  const pending = inFlight.get(key);
  if (pending) return pending;

  const shared = next(req).pipe(
    tap((event) => {
      if (cacheable && event instanceof HttpResponse && event.status >= 200 && event.status < 300) {
        cache.set(key, { response: event.clone(), storedAt: Date.now() });
      }
    }),
    // `refCount: false` — pierwszy subskrybent nie może anulować żądania w locie dla pozostałych.
    shareReplay({ bufferSize: 1, refCount: false }),
    finalize(() => inFlight.delete(key)),
  );

  inFlight.set(key, shared);
  return shared;
};

/** Czyści cache i mapę żądań w locie — dla testów oraz wylogowania (zmiana tożsamości/tenanta). */
export function resetHttpCache(): void {
  cache.clear();
  inFlight.clear();
}
