# Preflight — backlog napraw

Stan po bramce **2026-07-31** (NO-GO: 3 CRITICAL, 12 HIGH, 31 MEDIUM, 28 LOW).
Pełny raport: `test-results/preflight-2026-07-31.md`. Poprzedni przebieg: `preflight-2026-05-28.md`.

> **Obserwacja z tego przebiegu:** kilka pozycji odłożonych w maju wróciło z WYŻSZĄ severity —
> oracle InviteOnly (LOW → HIGH), liczniki w `IMemoryCache` (notatka o skalowaniu → HIGH),
> bomba dekompresyjna (nowa → HIGH). Odkładanie ich nie było neutralne: rosły razem z ruchem.
> Pozycje oznaczone 🔁 to powroty.

---

## FALA 0 — blokery deployu ✅ ZROBIONE (commit 5b5ca641, 2026-07-31)

Wszystkie sześć zamknięte. C2 wykonane poza kodem (rotacja w Azure), pozostałe pięć w kodzie
wraz z 14 nowymi testami. Backend 1824/1824; kluczowe obszary zweryfikowane też na Postgresie.

- [x] **C1 · Cleanup skasuje konto Admina** — `App.Infrastructure/BackgroundJobs/UnconfirmedAccountCleanupHostedService.cs:126`
  Kryterium #2 łapie `kontakt@zapisz.me` (rola Admin, `PhoneNumberConfirmed=true` przy `PhoneNumber=NULL`).
  Zweryfikowane na produkcji: **1 trafienie**. Serwis rusza ≤60 min po starcie API, `Users.Remove()` = twarde kasowanie.
  → Wykluczyć role Admin/SystemAdmin z kryterium #2 **albo** wymagać `PhoneNumber != null`.
  → Test: „konto z rolą Admin bez `Employee` NIE jest kasowane" (realny Postgres, nie InMemory — kaskad nie egzekwuje).

- [x] **C2 · Rotacja klucza ACS** — commit `e41d092c`, wpis `.gitleaksignore:5-7`  *(poza kodem)*
  Klucz osiągalny z `main`; `.gitleaksignore` sam deklaruje „WYMAGA rotacji".
  → Rotacja w Azure Portal + podmiana `ACS_EMAIL_CONNECTION_STRING` w GH Secrets. Purge historii to krok drugi i bez rotacji bezużyteczny.
  → Po rotacji: zamienić w `.gitleaksignore` „WYMAGA rotacji" na datę wykonania, żeby wpis przestał być TODO udającym allowlistę.

- [x] **C3 · Dzwonek: kartezjan Employee co 8 s** — `App.Application/Notifications/Queries/GetOutsideScheduleAppointments/GetOutsideScheduleAppointmentsQuery.cs:82` (oraz `:63`)
  `ToListAsync()` bez projekcji przy 10 kolekcjach `OwnsMany`; `POLL_MS = 8000`. Regresja z `fa5c2fd2` (dzwonek całego salonu).
  → Projekcja na skalary potrzebne do `IsAvailable`; to samo dla `Appointments` w `:63`.
  → Test regresyjny: asercja liczby JOIN-ów w `ToQueryString()` = 0.

- [x] **H1 · `OnboardingController` bez polityki roli** — `App.Api/Controllers/OnboardingController.cs:23`  ⚡ *jedna linia*
  Cztery mutacje salonu dostępne dla Employee/Kiosk, w tym zmiana nazwy i **publicznego sluga rezerwacji**.
  Konto „Recepcja" przechodzi bramkę potwierdzonego telefonu, bo powstaje z `PhoneNumberConfirmed = true`.
  → `[Authorize(Policy = "BusinessManagement")]`; `GET state`/`industry-templates` mogą zostać otwarte.
  → Dodatkowo: gałąź `existing != null` tylko gdy `OnboardingCompletedAt == null`.

- [x] **H2 · `SetOnboardingSchedule` omija strażnika** — `App.Application/Onboarding/Commands/SetOnboardingSchedule/SetOnboardingScheduleCommand.cs:111-145`
  Gałąź `UseAdHoc` dezaktywuje WSZYSTKIE grafiki i robi `return` przed delegacją do `SetEmployeeScheduleCommand:204`.
  Cichy DoS na sprzedaż salonu, wykonalny przez zwykłego pracownika.
  → `EnsureSelfOrStaffManager` na wejściu handlera, PRZED pierwszym `Set*`.
  → Test musi asertować nie tylko 403, ale i brak częściowego zapisu.

- [x] **H4 · CSPRNG w OTP telefonu** — `App.Application/Auth/Commands/SendPhoneOtp/SendPhoneOtpCommand.cs:94`  ⚡ *jedna linia*
  Jedyne pozostałe `Random.Shared` — dwie inne ścieżki OTP przeniesiono z komentarzem „przewidywalny".
  Zakres gubi `999999` i zera wiodące (~900 tys. zamiast miliona).
  → `RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6")`.
  → Strażnik: grep `Random.Shared` w `App.Application` ma zwracać zero trafień.

---

## FALA 1 — pozostałe HIGH

- [ ] **H3 · Inwalidacja cache slug→tenant** — `Onboarding/.../CompleteProfileCommand.cs:108`, `Tenants/.../UpdateTenant.cs:32`
  Tylko `UpdateCurrentSalonSettings` unieważnia cache (TTL 5 min). Zwolniony slug można przejąć → cross-tenant odczyt **i zapis** na publicznym bookingu.
  → Najlepiej raz na zawsze: przechwycić zmianę `Tenant.Slug` w `SaveChangesAsync` przez `ChangeTracker` i unieważnić `OriginalValue` + `CurrentValue`.

- [ ] **H5 · Demo mintuje budżet SMS** — `RequestOtpCommand.cs:155`, `DemoController.cs:65`
  Wyciszenie demo siedzi w `NotificationDispatcher`, a OTP go omija. Anonim zakłada demo-tenanty i dostaje świeże pule.
  → Sprawdzać `Tenant.IsDemo` w obu ścieżkach OTP; albo wykluczyć demo-slugi z publicznego bookingu; albo `MonthlySmsHardCap = 0` przy `MarkAsDemo`.

- [ ] **H6 · Brak dziennego capa SMS per tenant** — `App.Domain/Aggregates/TenantAggregate/Subscription.cs:179`
  Wyczerpanie miesięcznego capa ubija też OTP → publiczne zapisy salonu martwe do 1. dnia miesiąca.
  → Cap dobowy (np. `max(20, miesięczny/10)`) + alert do właściciela przy 50/80/100%.

- [ ] **H7 · Kill-switch SMS w `IMemoryCache`** 🔁 — `App.Infrastructure/Notifications/Sms/SmsApiClient.cs:51-69`
  ~10 IP wyłącza SMS (w tym OTP) całej platformie; licznik zeruje się przy każdym deployu.
  → Liczniki do Postgresa/Redisa (INCR+TTL). Rozważyć osobne pule „OTP" i „powiadomienia", żeby drenaż powiadomień nie ubijał logowania.

- [ ] **H8 · Hold odnawialny bez końca** — `UpdatePublicAppointmentCommand.cs:78`, `BookingOtpProtectionService.cs:545-571`
  PATCH przedłuża dzierżawę w nieskończoność, a klucz `hold:active-ip` odświeża TTL przy każdym `Set` → licznik wraca do zera mimo żywych holdów.
  → Cap całkowitego życia holdu (np. 15 min od utworzenia) + klucz okna czasu zamiast odświeżanego TTL.

- [ ] **H9 · Oracle InviteOnly** 🔁 *(w maju LOW)* — `RequestOtpCommand.cs:216-252`
  Nieudana próba nie podbija żadnego licznika → darmowe sondowanie „czy ten numer jest klientem salonu".
  → Neutralne 200 bez wysyłki (weryfikacja whitelisty dopiero w `verify-otp`) + podbijanie licznika IP także przy porażce.

- [ ] **H10 · Kartezjan w `GetEmployeeById` / `GetEmployeeServices`** — `Employees/Queries/GetEmployeeById/GetEmployeeByIdQuery.cs:26`, `GetEmployeeServices/GetEmployeeServicesQuery.cs:35`
  Rodzeństwo (`leaves`, `schedules`, `overrides`, `month-publications`) naprawiono projekcjami — te dwa zostały.
  → Projekcja przed `FirstOrDefaultAsync`.

- [ ] **H11 · Bomba dekompresyjna** — `App.Infrastructure/Storage/ImageProcessingService.cs:63`
  `MaxInputBytes = 5 MB` ogranicza plik, nic nie ogranicza wymiarów. 5 MB PNG 30000×30000 ≈ 3,6 GB RAM na maszynie o 2 vCPU.
  → `Image.Identify` przed `LoadAsync`, odrzucać > ~40 Mpx. Miniaturę robić z przeskalowanego obrazu, nie z klona oryginału.

- [ ] **H12 · Brak deadline'u i circuit-breakera na kanałach zewnętrznych** — `AzureCommunicationEmailTransport.cs:26`, `AuthController.cs:1550`
  Bezpośredni wołający `IEmailTransport` omijają 15-sekundowy cap dispatchera; z retry ACS realny deadline to ~45 s.
  → Objąć wołających tym samym linked-CTS, `MaxRetries = 1`, `Microsoft.Extensions.Http.Resilience` na typed clientach, `AddRequestTimeouts()` z wyłączeniem SignalR.

---

## FALA 2 — MEDIUM (wybór o największym wpływie)

**Autoryzacja**
- [ ] `available-slots` i `month-availability` ignorują `StaffCalendarVisibilityPolicy` — `AppointmentController.cs:167,174`
- [ ] Zmiana roli nie rotuje `SecurityStamp` — demotowany Manager działa do 30 min 🔁 — `AuthController.cs:805`
- [ ] Siatka autoryzacyjna bez 5 mutacji wizyty (`/services`, `/duration`, `/final-price`, `swap`) — `AppointmentAuthorizationMatrixIntegrationTests.cs`

**Izolacja**
- [ ] `StaffAccessPolicy` nie sprawdza przynależności pracownika do tenanta przy `TeamFull/TeamReadOnly` — `StaffAccessPolicy.cs:57,82`
- [ ] Write-guard fail-open przy `TenantId == null` — `ApplicationDbContext.cs:145`
- [ ] Niedeterministyczny tenant przy wielu `Employee` na jednym `User` — `TenantIdentifierMiddleware.cs:126`

**Booking**
- [ ] Rezerwacja u pracownika `IsBookable=false` — `PlaceAppointmentCommand.cs:86`
- [ ] PATCH holdu i self-service reschedule omijają tryb stałych slotów — `ApplyRescheduleCommand.cs:111`
- [ ] Double-booking pod wyścigiem dla nakładających się zakresów 🔁 — `AppointmentConfiguration.cs:259` (indeks łapie tylko identyczny `StartTime`; potrzebny `btree_gist EXCLUDE`)

**RODO**
- [ ] Erasure nie czyści: zdjęć inspiracji (baza + R2), `OtpVerification`, `Notifications` — `CustomerErasure.cs:55`
- [ ] Hard-delete salonu zostawia zdjęcia w R2 bez namiaru — `TenantPurgeService.cs:25`
- [ ] Umami ładowany na stronie rezerwacji salonu wbrew polityce — `web/src/pages/[slug].astro:24`
- [ ] Kiosk czyta pełną bazę klientek z `GeneralNotes` — `CustomersController.cs:17`

**Odporność**
- [ ] Brak `EnableRetryOnFailure` — `Program.cs:621`
- [ ] Brak `mem_limit` na kontenerach 🔁 — `docker-compose.prod.yml:33,103`
- [ ] `IMemoryCache` bez `SizeLimit` — `Program.cs:724`
- [ ] `MarkAllNotificationsRead` robi N UPDATE-ów zamiast `ExecuteUpdate` — `MarkAllNotificationsReadCommand.cs:41`
- [ ] Brak indeksu `(TenantId, RecipientUserId, CreatedAtUtc)` — `NotificationConfiguration.cs:30`

**Konfiguracja**
- [ ] Brak `Cache-Control: no-store` na `/api/*` — `Program.cs:1104`
- [ ] Nagłówki bezpieczeństwa gubione na odpowiedziach błędu (kolejność `UseExceptionHandler`) — `Program.cs:1090`
- [ ] Ściszenie 2xx kasuje ślad udanych mutacji → brak rejestru dostępu do PII — `Program.cs:1178`
- [ ] Fronty bez realnego CSP 🔁 — `caddyfile:47,71,132`

**Zależności**
- [ ] `postcss` → `^8.5.25` w obu frontach
- [ ] Martwe `overrides`: `fast-uri ^3.1.2` → `^3.1.5`, `immutable ^5.1.5` → `^5.1.8` (celują w wersje nadal podatne)
- [ ] `web/package.json` overrides: `js-yaml ^4.3.0`, `svgo ^4.0.2`
- [ ] Lockfile backendu 🔁 — `RestorePackagesWithLockFile` + `--locked-mode` w CI
- [ ] Bump `Microsoft.* 10.0.2 → 10.0.10`, `Npgsql → 10.0.3`

---

## Znalezione poza bramką (praca bieżąca)

Pozycje spoza raportu `/preflight-security` — wychodzą przy zwykłej pracy z produktem. Trzymane
tutaj, żeby nie zaciemniać prowenancji fal 0–3, ale w tym samym pliku, bo priorytetyzuje się je razem.

- [x] **Były pracownik lądował w kreatorze zakładania salonu** — `GetOnboardingStateQuery.cs`,
  `setup.guard.ts`, nowy `inactive-account.component.ts` *(naprawione 2026-08-06)*
  Dezaktywacja pracownika zostawia konto `User` z rolą Employee, ale zabiera AKTYWNY rekord
  `Employee`. Handler stanu filtrował po `e.IsActive`, więc widział to identycznie jak świeżego
  właściciela przed krokiem „profil" → `NextStep: "Profile"` → `onboardingGuard` odbijał zwolnioną
  osobę na kreator ZAKŁADANIA WŁASNEGO SALONU. Podwójnie źle: komunikat absurdalny, a ścieżka i tak
  ślepa (mutacje kreatora wymagają `BusinessManagement`, `CompleteProfile` dodatkowo potwierdzonego
  telefonu) — konto zostawało bez wyjścia i bez wyjaśnienia.
  → Handler odróżnia „nigdy nie miał rekordu Employee" od „miał, nieaktywny"; drugi zwraca
  `NextStep: "InactiveAccount"`, który front mapuje na ekran „Twoje konto nie ma już dostępu".
  → Testy: integracyjny `Deactivated_employee_is_not_sent_to_the_salon_wizard` + jednostkowy
  w `setup.guard.spec.ts`.
  → Skala na dziś: 1 konto na produkcji (`chodackimateusz04@gmail.com`, nieaktywny pracownik
  w Salonie Magdaleny Borowskiej). Rosłaby z każdą rotacją kadry w salonach.

- [x] **Salony demo lądowały w kreatorze po świeżym seedzie** — `DbSeeder.cs` *(naprawione 2026-08-06)*
  Backfill z migracji `AddOnboardingFields` jest jednorazowym `UPDATE` istniejących wierszy, więc
  na świeżej bazie nie ma czego stemplować — seed wstawia tenanty dopiero po migracjach i nie
  ustawiał `OnboardingCompletedAt`. Dev-only: produkcyjne salony istnieją przed migracją, więc
  backfill je obejmuje.
  → `MarkDemoTenantsOnboarded` przy tworzeniu + idempotentny `EnsureDemoTenantsOnboardedAsync`
  (rusza wyłącznie trzy znane Id salonów demo). Testy: `DbSeederOnboardingTests` (3 przypadki).

---

## FALA 3 — LOW (higiena)

- [ ] `ShortLink` niesie `TenantId`, ale nie jest `ITenantEntity` — `ShortLink.cs:12`
- [ ] Martwy `EmployeeTenantResolver` z surowym ADO.NET omijającym `DbContext` — usunąć — `EmployeeTenantResolver.cs:19`
- [ ] Anonimowy `GET /api/Payments/status/{id}` bez limitera — `PaymentsController.cs:42`
- [ ] `Server: Caddy` na `api.zapisz.me` (pozostałe bloki mają `-Server`) — `caddyfile:79`
- [ ] Temat maila bez normalizacji CR/LF — `BookingConfirmedEventHandler.cs:59`
- [ ] Cookie `XSRF-TOKEN` omija `CrossOriginCookiePolicy` i jest martwe w prod — `AuthController.cs:88`
- [ ] Wyłączenie rate-limitu obejmuje całe `/internal`, ściszenie logu tylko `tls-allowed` — `Program.cs:423`
- [ ] Popover przewodnika = latentny sink `innerHTML` — dopisać test zakazujący interpolacji w `defs/` — `guide.service.ts:157`
- [ ] Backendowy test allowlisty `X-Client-Mode` — `Program.cs:1281`
- [ ] `IncludeQueryInRequestPath = false` jawnie (dziś domyślne, ale przełączenie „na chwilę" wsypuje PII do Loki)
- [ ] Maskowanie logów nie sięga `StructureValue` ani `LogEvent.Exception` — `SensitiveDataMaskingEnricher.cs:17`
- [ ] Brak retencji nieaktywnych klientek 🔁 — patrz decyzja D1
- [ ] Stopka z linkiem do polityki na stronie salonu (ścieżka skip-OTP omija jedyne wystąpienie) — `[slug].astro`
- [ ] `resend-phone-otp` → zawsze 204 (anti-enumeracja) 🔁 — `AuthController.cs:431`
- [ ] Self-service OTP: SHA-256 bez salta → HMAC z pepperem 🔁 — `OtpCodeHasher.cs:13`

---

## Decyzje do podjęcia (⚖️ wymagają Twojego wejścia)

- **D1 · Retencja nieaktywnych klientek** 🔁 — (a) job anonimizujący po N latach od ostatniej wizyty (rdzeń `CustomerErasure` gotowy, brakuje wołającego), czy (b) formalnie przyjąć obecny model. Jeśli (a) — jaki próg N? Decyzja należy do salonu jako administratora danych.
- **D2 · Umowa powierzenia z SMSAPI** (ComVision sp. z o.o.) — akcja prawna, nie kod.
- **D3 · Podprocesorzy w polityce** — dopisać Umami i Grafana Labs; potwierdzić region stacka Grafany.
- **D4 · Skalowanie ponad 1 instancję API** — dopóki jedna instancja, liczniki w pamięci są udokumentowane. Przy drugiej: capy SMS, rate-limit, OTP-protection i cache slug→tenant MUSZĄ iść do wspólnego store'u. To wpływa na priorytet H7.
- **D5 · Eksport i usuwanie kont własnych użytkowników** (właściciele/pracownicy — jesteśmy ADO) — dziś tylko ręcznie przez admina, bez rejestru i SLA.
- **D6 · Majory** (pause-and-ask): `astro 6 → 7` (linia 6.x nie dostanie już poprawek), `@primeng/themes` → `@primeuix/themes` (deprecated, ląduje w przeglądarce), `ImageSharp 3 → 4`.

---

## Testy do dopisania (z tego audytu)

- [ ] Konto z rolą Admin bez `Employee` NIE jest kasowane przez cleanup *(realny Postgres)*
- [ ] Usunięcie użytkownika kasuje `UserGuideCompletions`, nie tyka `Employees`/`Appointments` *(kaskadę pilnuje dziś tylko baza, a testy cleanupu chodzą na InMemory)*
- [ ] Employee/Kiosk dostaje 403 na wszystkich czterech `POST /api/onboarding/*`
- [ ] Siatka authz: 5 brakujących mutacji wizyty + 2 endpointy zadatków
- [ ] `TeamFull` w tenancie A + `employeeId` z tenanta B → `ForbiddenAccessException`
- [ ] Liczba JOIN-ów w `ToQueryString()` handlerów Employee = 0 *(strażnik przed trzecim nawrotem kartezjanu)*
- [ ] Upload obrazu 25000×25000 → 400, nie OOM
- [ ] Cross-tenant self-service: sesja tenanta A + `appointmentId` tenanta B → 404 dla `cancel` i `reschedule`
- [ ] Poziomy logowania: udany `POST` → Information, udany `GET` → Debug *(dziś nic nie pilnuje `GetLevel`)*
- [ ] `X-Client-Mode: <8 kB śmieci>` → `ClientMode=unknown` w logu
