import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AcceptInviteRequest, AuthClient, AuthSessionDto, DemoClient, LoginRequest, RegisterOwnerRequest, RegisterOwnerResponse } from '@core/api/api-client';
import { NavigationService, UserRole } from '@core/services/NavigationService';
import { MessageService } from 'primeng/api';
import { CsrfTokenService } from './csrf-token.service';
import { SessionExpiryService } from './session-expiry.service';
import { catchError, finalize, fromEvent, map, Observable, of, race, retry, shareReplay, switchMap, take, tap, throwError, timeout, timer } from 'rxjs';
import { resetHttpCache } from '@core/interceptors/http-cache.interceptor';

/**
 * Wynik sondy sesji (`GET /api/auth/me`). Rozróżnienie `anonymous` vs `unavailable` jest istotne:
 * tylko `anonymous` (odpowiedź 401 od serwera) oznacza „naprawdę wylogowany". `unavailable` to
 * „nie wiemy" — sieć nie odpowiedziała, cookie sesji nadal może być ważne.
 */
export type SessionOutcome =
  | { readonly kind: 'authenticated'; readonly session: AuthSessionDto }
  | { readonly kind: 'anonymous' }
  | { readonly kind: 'unavailable' };

/** Limit na pojedynczą próbę — chroni przed „czarną dziurą" sieci (captive portal), gdzie żądanie wisi. */
const PROBE_TIMEOUT_MS = 10_000;
/**
 * Odstępy przed kolejnymi próbami. Długość tablicy = liczba ponowień (poza próbą pierwszą).
 *
 * Suma (~6 s) jest celowo większa niż wygląda na potrzebną: offline `fetch` pada NATYCHMIAST, więc
 * krótka seria wyczerpuje się w ułamku czasu, w jakim telefon wybudza radio (realnie 3–10 s). Ten
 * budżet pokrywa też przypadek, w którym `navigator.onLine` mówi już `true`, a trasy jeszcze nie ma —
 * bramka `waitForNetwork()` tego nie łapie, bo `true` niczego nie gwarantuje.
 */
const PROBE_RETRY_DELAYS_MS = [500, 1500, 4000];
/**
 * Ile czekamy na zdarzenie `online`, zanim mimo wszystko spróbujemy.
 *
 * Cap, nie obietnica: `navigator.onLine === false` bywa uparte na desktopie z VPN-em, a sonda w tle
 * jest tańsza niż zawieszony na zawsze bootstrap.
 */
const OFFLINE_WAIT_MS = 10_000;

/**
 * Tylko odpowiedź 401 dowodzi, że użytkownik nie ma sesji.
 *
 * Klient NSwag zamienia KAŻDY błąd na `ApiException` z polem `status` — również błąd transportu, który
 * dostaje `status: 0`. `TimeoutError` z kolei nie ma `status` w ogóle. Dlatego sprawdzamy `status`
 * strukturalnie: cokolwiek nie jest jawnym 401, traktujemy jako „nie wiemy”, nie jako wylogowanie.
 */
function isUnauthorized(error: unknown): boolean {
  if (error instanceof HttpErrorResponse) {
    return error.status === 401;
  }
  return typeof error === 'object' && error !== null && (error as { status?: unknown }).status === 401;
}

@Injectable({ providedIn: 'root' })
export class AuthSessionService {
  private readonly authClient = inject(AuthClient);
  private readonly demoClient = inject(DemoClient);
  private readonly nav = inject(NavigationService);
  private readonly router = inject(Router);
  private readonly csrf = inject(CsrfTokenService);
  private readonly messageService = inject(MessageService);

  constructor() {
    // Serwis jest `providedIn: 'root'` i żyje tyle, co aplikacja — subskrypcja nie wymaga sprzątania.
    inject(SessionExpiryService).expired$.subscribe(() => this.handleSessionExpired());
  }

  private readonly sessionState = signal<AuthSessionDto | null>(null);
  private readonly hydratedState = signal(false);
  private hydration$: Observable<SessionOutcome> | null = null;
  /** Rośnie z każdą sondą — pozwala odróżnić sondę bieżącą od porzuconej przez `retryHydrate()`. */
  private probeGeneration = 0;

  readonly session = this.sessionState.asReadonly();

  /**
   * Czy znamy już autorytatywny stan sesji (sesja z `/api/auth/me` albo 401 = wylogowany).
   * Nieudana sonda (sieć/5xx) NIE ustawia tej flagi — „nie wiemy" to nie to samo co „wylogowany”.
   */
  readonly isHydrated = this.hydratedState.asReadonly();
  readonly isAuthenticated = computed(() => !!this.sessionState()?.userId);

  /**
   * Rola zalogowanego użytkownika albo `null`, gdy jeszcze jej NIE ZNAMY (sesja nie wróciła
   * z `/api/auth/me`) lub użytkownik jest wylogowany.
   *
   * `null` to celowo osobny stan, nie „domyślnie właściciel". Wcześniej `mapRoles([])` zwracało
   * `'owner'`, więc przez pierwsze kilkaset ms każdy był dla frontu właścicielką — guardy
   * przepuszczały żądania po cudze dane i sypały serią 403 („Odmowa dostępu"). Każdy konsument
   * musi traktować `null` jako „brak uprawnień" (porównania do stringów robią to same z siebie).
   */
  readonly currentRole = computed<UserRole | null>(() => {
    const roles = this.sessionState()?.roles;
    return roles?.length ? this.mapRoles(roles) : null;
  });
  readonly currentEmployeeId = computed(() => this.sessionState()?.employeeId ?? null);
  /** Id konta Identity — klucz preferencji per-użytkownik (np. ostatni kalendarz w `/admin/schedule`). */
  readonly currentUserId = computed(() => this.sessionState()?.userId ?? null);
  /** Czy bieżąca sesja należy do efemerycznego demo-tenanta (steruje banerem + blokadami demo). */
  readonly isDemo = computed(() => !!this.sessionState()?.isDemo);
  /** Czy admin platformy działa w trybie wsparcia (impersonacja salonu) — steruje banerem. */
  readonly isImpersonating = computed(() => !!this.sessionState()?.isImpersonating);
  /** Nazwa salonu, w którym trwa sesja wsparcia (do banera). */
  readonly impersonatedTenantName = computed(() => this.sessionState()?.impersonatedTenantName ?? null);

  /**
   * Ustala stan sesji na starcie aplikacji (raz — potem z cache).
   *
   * KRYTYCZNE: sesję czyścimy WYŁĄCZNIE przy 401. Wcześniej `catchError` łapał wszystko i każdy
   * błąd transportu (status 0, 5xx, timeout) kasował sesję + zatrzaskiwał `hydratedState`, przez co
   * guardy odsyłały na `/login` mimo ważnego 30-dniowego cookie „zapamiętaj mnie". W PWA (standalone)
   * to był chleb powszedni: service worker serwuje shell z cache’u szybciej, niż telefon wybudzi radio,
   * więc `me()` leciało w martwą sieć → status 0 → „wylogowany". Zatrzask `hydratedState` sprawiał, że
   * powrót sieci już nie pomagał — dopiero pełny reload albo ponowne logowanie. Stąd zgłoszenia
   * „wylogowuje mnie cały czas mimo zapamiętaj mnie".
   *
   * Dlatego: ponawiamy błędy przejściowe, a gdy się nie uda — zwracamy `unavailable` BEZ czyszczenia
   * sesji i BEZ zatrzasku, żeby kolejna nawigacja (albo powrót sieci) spróbowała ponownie.
   */
  hydrate(): Observable<SessionOutcome> {
    if (this.hydratedState()) {
      const session = this.sessionState();
      return of(session ? { kind: 'authenticated' as const, session } : { kind: 'anonymous' as const });
    }

    // Deduplikacja: na jednej nawigacji hydrate() woła kilka guardów naraz (np. authGuard + roleGuard).
    // Bez tego każdy odpalałby własną sondę wraz z ponowieniami.
    if (!this.hydration$) {
      // Znacznik generacji: `finalize` sondy porzuconej przez `retryHydrate()` nie może wyzerować
      // rejestracji sondy, która zajęła jej miejsce (inaczej dedup znika i leci zbędne żądanie).
      const generation = ++this.probeGeneration;
      this.hydration$ = this.probeSession().pipe(
        finalize(() => {
          if (this.probeGeneration === generation) {
            this.hydration$ = null;
          }
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    }
    return this.hydration$;
  }

  /**
   * Czeka na sieć, gdy przeglądarka jawnie mówi, że jej nie ma.
   *
   * `navigator.onLine === false` jest wiarygodne („na pewno brak interfejsu"), `true` nie gwarantuje
   * niczego — dlatego bramkujemy TYLKO na `false`, a resztę zostawiamy ponowieniom. Bez tego cała seria
   * prób spalała się w ~2 s na natychmiastowych błędach transportu i użytkowniczka widziała ekran
   * `/offline` w scenariuszu, w którym sieć wracała sekundę później.
   */
  private waitForNetwork(): Observable<unknown> {
    if (typeof navigator === 'undefined' || navigator.onLine !== false) {
      return of(null);
    }
    return race(fromEvent(window, 'online'), timer(OFFLINE_WAIT_MS)).pipe(take(1));
  }

  private probeSession(): Observable<SessionOutcome> {
    return this.waitForNetwork().pipe(
      // Ponowienia opakowują SAMO żądanie, nie bramkę — inaczej każda próba czekałaby własne
      // `OFFLINE_WAIT_MS` i bootstrap przy trwałym offline ciągnąłby się grubo ponad minutę.
      switchMap(() =>
        this.authClient.me().pipe(
          // `timeout` PRZED `retry`, żeby limit obowiązywał każdą próbę z osobna, a nie całą serię.
          timeout(PROBE_TIMEOUT_MS),
          retry({
            count: PROBE_RETRY_DELAYS_MS.length,
            delay: (error, attempt) =>
              // 401 to autorytatywna odpowiedź serwera — ponawianie nic nie zmieni.
              isUnauthorized(error) ? throwError(() => error) : timer(PROBE_RETRY_DELAYS_MS[attempt - 1]),
          }),
        ),
      ),
      map((session) => {
        this.setSession(session);
        return { kind: 'authenticated' as const, session };
      }),
      catchError((error: unknown) => {
        if (isUnauthorized(error)) {
          this.clearSession();
          this.hydratedState.set(true);
          return of({ kind: 'anonymous' as const });
        }
        // Świadomie NIE wołamy clearSession() ani nie ustawiamy hydratedState — nie wiemy, czy sesja
        // wygasła. `isHydrated` zostaje `false`, więc konsumenci (np. centrum powiadomień) wstrzymują
        // odpytywanie zamiast zakładać wylogowanie.
        return of({ kind: 'unavailable' as const });
      }),
    );
  }

  /** Wymuszony re-fetch sesji (np. po zmianie własnej nazwy/e-maila) — odświeża header i menu. */
  refreshSession(): Observable<AuthSessionDto | null> {
    return this.authClient.me().pipe(
      tap((session) => this.setSession(session)),
      map((session) => session as AuthSessionDto | null),
      catchError(() => of(null)),
    );
  }

  login(request: LoginRequest): Observable<AuthSessionDto> {
    return this.authClient.login(request).pipe(
      switchMap((session) => this.refreshCsrf().pipe(map(() => session))),
      tap((session) => this.setSession(session)),
    );
  }

  /**
   * Rejestracja właściciela NIE loguje od razu — backend wymaga potwierdzenia maila.
   * Zwracamy minimalne info; aktywna sesja powstanie dopiero po confirm-email + login.
   */
  registerOwner(request: RegisterOwnerRequest): Observable<RegisterOwnerResponse> {
    return this.authClient.registerOwner(request);
  }

  /**
   * Uruchamia tryb demo: backend tworzy izolowanego demo-tenanta z danymi, wystawia cookie
   * sesyjne i zwraca sesję. Po stronie klienta zachowujemy się jak po loginie (CSRF + setSession).
   */
  startDemo(): Observable<AuthSessionDto> {
    return this.demoClient.start().pipe(
      switchMap((session) => this.refreshCsrf().pipe(map(() => session))),
      tap((session) => this.setSession(session)),
    );
  }

  /** Czy publiczny tryb demo jest włączony na backendzie (sterowanie widocznością przycisku). */
  demoEnabled(): Observable<boolean> {
    return this.demoClient.enabled().pipe(
      map((dto) => !!dto.enabled),
      catchError(() => of(false)),
    );
  }

  acceptInvite(request: AcceptInviteRequest): Observable<AuthSessionDto> {
    return this.authClient.acceptInvite(request).pipe(
      switchMap((session) => this.refreshCsrf().pipe(map(() => session))),
      tap((session) => this.setSession(session)),
    );
  }

  logout(): void {
    this.refreshCsrf()
      .pipe(
        switchMap(() => this.authClient.logout()),
        catchError(() => of(null)),
        tap(() => {
          this.clearSession();
          // Po wylogowaniu tożsamość znika — cache'owany token (dla zalogowanego usera) nie przejdzie
          // już walidacji, więc unieważniamy go, by kolejne logowanie pobrało świeży, anonimowy token.
          this.csrf.invalidate();
          // To samo dla cache HTTP: bez tego przelogowanie w ciągu TTL (5 s) pokazałoby
          // pracowników/usługi POPRZEDNIEGO salonu. Wąskie okno, ale przeciek między tenantami.
          resetHttpCache();
          void this.router.navigate(['/login'], { replaceUrl: true });
        }),
      )
      .subscribe();
  }

  private refreshCsrf(): Observable<unknown> {
    return this.csrf.refresh().pipe(catchError(() => of(null)));
  }

  private setSession(session: AuthSessionDto): void {
    this.hydratedState.set(true);
    this.sessionState.set(session);
    this.nav.syncRoleFromSession(session.roles ?? []);
  }

  private clearSession(): void {
    this.sessionState.set(null);
    // Wylogowany = brak roli. Ustawienie 'owner' pokazywało menu właścicielki po wylogowaniu.
    this.nav.setUserRole(null);
  }

  /**
   * Ponawia sondę po nieudanej próbie (przycisk „Spróbuj ponownie” / powrót sieci na ekranie offline).
   * Bez tego `hydrate()` oddałoby wynik z `shareReplay` poprzedniej, nieudanej próby.
   *
   * Sondy w locie nie anulujemy — `hydrate()` podbije generację, więc porzucona sonda dokończy się
   * cicho i nie ruszy rejestracji swojej następczyni.
   */
  retryHydrate(): Observable<SessionOutcome> {
    this.hydration$ = null;
    return this.hydrate();
  }

  /**
   * Serwer odrzucił ciasteczko w trakcie pracy — sprzątamy i odsyłamy na logowanie.
   *
   * Strażnik `!sessionState()` pełni dwie role: pomija sygnał, gdy nie ma czego wygaszać (401 z żądania
   * anonimowego), i działa jako zatrzask na serię — `clearSession()` zeruje stan, więc kolejne 401 z tej
   * samej salwy równoległych żądań nie wywołają drugiego toasta ani drugiej nawigacji.
   */
  private handleSessionExpired(): void {
    if (!this.sessionState()) {
      return;
    }

    this.clearSession();
    // Sesja jest autorytatywnie martwa — inaczej guardy poszłyby na `/offline` zamiast `/login`.
    this.hydratedState.set(true);
    // Token CSRF był wystawiony na tożsamość, której już nie ma.
    this.csrf.invalidate();
    this.messageService.add({
      severity: 'warn',
      summary: 'Sesja wygasła',
      detail: 'Zaloguj się ponownie.',
      life: 6000,
    });
    void this.router.navigate(['/login'], { replaceUrl: true });
  }

  /** Najwyższa rola z sesji. `null` dla nieznanej listy — NIGDY fallback na `'owner'`. */
  private mapRoles(roles: string[]): UserRole | null {
    const normalized = new Set(roles.map((role) => role.toLowerCase()));
    if (normalized.has('admin')) {
      return 'systemAdmin';
    }
    if (normalized.has('owner')) {
      return 'owner';
    }
    if (normalized.has('manager')) {
      return 'manager';
    }
    if (normalized.has('employee')) {
      return 'employee';
    }
    if (normalized.has('kiosk')) {
      return 'kiosk';
    }
    return null;
  }
}
