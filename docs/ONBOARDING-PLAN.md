# Plan: nowy onboarding właściciela salonu

Gałąź: `onboarding`. Dokument projektowy — stan na 2026-07-10.

## Problem

Rejestracja żąda 11 pól naraz (`RegisterOwnerRequest`), łącznie ze slugiem, strefą czasową i walutą.
Po jej ukończeniu właściciel dostaje **całkowicie pusty salon**: bez usług, bez kategorii, bez grafiku,
i ląduje wprost na kalendarzu, który nic mu nie mówi. Nie ma nigdzie wyjaśnienia, że *grafik pracownika
to jedyne źródło terminów widocznych dla klienta* — ani że istnieje wybór między slotami elastycznymi
a stałymi godzinami startu.

## Decyzje (podjęte przez właściciela produktu)

| Kwestia | Decyzja |
|---|---|
| Kreator blokuje panel? | **Tak, twardy guard** na `/admin/**` |
| Szablony branżowe | **Prefill z zaznaczaniem**; zapis dopiero po „Dalej" |
| Zakres | **Pełny**: slim `register-owner` + kreator |
| Płeć w kreatorze | **Nie zbieramy**; copy bezosobowe |
| Rejestracja | e-mail + hasło + **telefon** + promo (opcjonalnie) + Turnstile |
| Ważność linku e-mail | **24h** (default Identity — bez custom providera) |
| Cleanup porzuconego konta | **24h**, sweep co godzinę |
| Kod SMS (OTP) | 10 min — bez zmian (`Auth:PhoneOtpTtlMinutes`) |

## Architektura: kiedy powstaje Tenant

**Droga B — Tenant i Employee powstają dopiero w kroku „Jak się nazywasz?".**
Do tego momentu istnieje wyłącznie `IdentityUser` z rolą `Owner`.

Uzasadnienie: przy twardym guardzie nikt i tak nie wchodzi do `/admin` przed końcem kreatora,
więc brak tenanta nikomu nie przeszkadza. Alternatywa (Tenant od razu z atrapami) wymagałaby
wpisania fikcyjnego imienia do `Employee` (`Guard.NormalizeRequiredText` rzuca na pusty string)
i zostawiałaby w bazie salony-widma o nazwie „Mój salon".

Weryfikacja wykazała, że droga B jest bezpieczna:

- `TenantIdentifierMiddleware` **nie rzuca** dla usera bez `Employee` — loguje warning i przepuszcza.
- Cookie/claimy **nie zawierają `tenantId`** — jest rozwiązywany per-request. Utworzenie tenanta
  po zalogowaniu nie wymaga przelogowania; kolejne `GET /api/auth/me` zwróci już `TenantId`.
- `RemoveTenantDataAsync` jest odporne na usera bez tenanta (pętla po pustej liście `tenantIds`).
- `ConfirmPhoneCommand` nie zależy od tenanta — działa pre-tenant.

Konsekwencje, o których trzeba pamiętać:

- `CompleteProfileCommand` będzie **jedynym handlerem nie dziedziczącym po `TenantHandler<,>`**
  (tenant jeszcze nie istnieje). Musi czytać `userId` z claima, **nie** wołać `GetResolvedTenantId()`.
  To wymaga jawnego komentarza w kodzie, inaczej ktoś to „naprawi".
- Zalogowany Owner bez tenanta dostanie **403 z każdego endpointu panelu**
  (`GetResolvedTenantId() == null → Forbid()`). Front musi obsłużyć sesję z `TenantId == null`
  i kierować do `/setup`, nie do panelu.

## Ścieżka użytkownika

```
/register             e-mail + hasło + telefon + Turnstile  (+ promo, zwijany)
      ↓ mail z linkiem (24h)
/confirm-email        klik w link → automatycznie leci SMS OTP
/setup/phone/verify   6 cyfr (TTL 10 min)
      ↓ === tu powstaje Tenant + Employee + VAT rates + redempcja promo ===
/setup/profile        Jak się nazywasz?          imię, nazwisko
/setup/industry       Czym się zajmujesz?        kafle branż
/setup/services       Twoje usługi               prefill, odznacz/edytuj cenę i czas
/setup/salon          Nazwa salonu + link        slug z live-sprawdzaniem dostępności
/setup/slot-mode      Jaki grafik wolisz?        elastyczny vs stałe godziny
/setup/schedule       Kiedy pracujesz?           presety dni i godzin
/setup/booking-rules  Jak przyjmujesz zapisy?    potwierdzanie auto/ręczne
/setup/done           Twój link jest gotowy      kopiuj / QR / „Otwórz jak klient"
      ↓
/admin/schedule       kalendarz + karta „roześlij link" (do pierwszej wizyty)
```

Strefa czasowa i waluta znikają z rejestracji — wbite na `Europe/Warsaw` / `PLN`,
edytowalne w Ustawienia → Dane salonu, gdzie już są.

## Grafik elastyczny vs statyczny

Mapuje się **wprost** na istniejący `Employee.SlotGenerationMode` (`Grid` | `FixedStartTimes`).
Dziś pole jest zakopane w zaawansowanym ekranie grafiku i nowy właściciel nigdy go świadomie nie wybiera.

Ekran `/setup/slot-mode` pokazuje dwie karty z **wizualnym podglądem kalendarzyka** (gęsta siatka
vs trzy kafle godzin) — nie radio button:

- **Elastyczny** (`Grid`) — klient wybiera dowolną godzinę, co `Tenant.AppointmentSlotStepMinutes` (15).
  Kalendarz wypełnia się ciasno; kosztem są dziury między wizytami.
  → automatycznie włączamy `GapFillingSettings.Mode = PreferAdjacent`, bez pytania.
- **Stałe godziny** (`FixedStartTimes`) — klient wybiera tylko z godzin podanych przez właściciela.
  Dzień przewidywalny; ryzyko: jeśli godziny nie pasują, rezerwacji nie będzie wcale.

Uwagi implementacyjne:

- `Employee.SetWeeklySchedule` **nie potrafi** ustawić fixed start times — zawsze buduje `ScheduleDay`
  z konstruktora zakresowego, czyli `Grid`, i nie dotyka `SlotGenerationMode`. To skrót dla seedera.
- Do trybu statycznego kreator musi wołać `SetEmployeeSchedule` (`TenantHandler`), więc krok
  **musi być po** utworzeniu tenanta. Kolejność powyżej to spełnia.
- `GetSlotModeForDate` rozstrzyga tryb **per dzień** z `ScheduleDay.IsFixed`; globalne pole
  `SlotGenerationMode` jest tylko fallbackiem.

Ekran `/setup/schedule` daje presety (Pn–Pt 9–17, Pn–Sob 9–17, własny), a przy trybie stałym
dodatkowo godziny startu wspólne dla wszystkich dni roboczych. Cykle 1–4 tyg., przerwy i override'y
zostają w normalnym ekranie grafiku — w kreatorze ich nie ma.

### „Papierowy kalendarz" — brak grafiku powtarzalnego (dodane 2026-07-16)

Część stylistek nie prowadzi grafiku powtarzalnego — dostępność wpisuje dzień po dniu przez dni
specjalne. **Silnik już to obsługiwał**: `Employee.IsAvailable` rozstrzyga w kolejności
urlop → wyjątek → grafik → niedostępny, więc wyjątek działa BEZ grafiku tygodniowego (jest na to
nazwany test `OverrideOnlyFixedDayIntegrationTests` — „Papierowy kalendarz"). Kreator tego nie
oferował i co gorsza **wymuszał odwrotność**, z domyślnym Pn–Pt 9–17 — właścicielka pracująca
z kartki kończyła konfigurację z fikcyjnym grafikiem, który już generował terminy klientkom.

**To są DWIE OSTATNIE OSIE, nie jedna** (kluczowe — pierwsze podejście je skleiło i było błędne):

| Oś | Pytanie | Gdzie |
|---|---|---|
| Kształt **dnia** pracy | siatka co 15 min czy stałe godziny startu | krok 5 |
| Rytm **tygodnia** | powtarzalny czy dzień po dniu | krok 6 |

Dzień wpisany na bieżąco **też ma tryb** — `ScheduleOverride` niesie własny `IsFixed`,
`SetScheduleOverrideCommand` przyjmuje `SlotGenerationMode`, a `GetSlotModeForDate` mówi wprost
o „mieszaniu trybów per dzień". Dlatego „na bieżąco" NIE jest trzecią kartą obok
Elastyczny/Stałe — byłaby alternatywą dla czegoś ortogonalnego.

- **Krok 5 „Jak wyznaczasz terminy?"** — Elastyczny / Stałe godziny. Tytuł świadomie nie mówi
  „grafik": to własność pojedynczego dnia, a o grafik pytamy dopiero dalej. „Jaki grafik wolisz?"
  tuż przed „czy w ogóle ustawić grafik?" byłoby inwersją.
- **Krok 6 „Kiedy pracujesz?"** — najpierw karty „Grafik powtarzalny" / „Ustawiam dni na bieżąco",
  formularz dni i godzin pod spodem i tylko przy pierwszej. Pytanie stoi NAD swoim skutkiem, na
  jednym ekranie, więc licznik kreatora zostaje stały („z 8") i nie musi być warunkowy.

`SlotGenerationMode` zapisujemy **także** przy „na bieżąco" — to podpowiedź startowa dla każdego
nowego dnia specjalnego (`employeeMode` w `employee-special-days.component.ts`), więc wybór z kroku 5
nie może przepaść. Normalnie ustawia go `SetEmployeeScheduleCommand`, którego w tej ścieżce nie ma.

Stan kreatora liczy krok grafiku jako domknięty gdy `hasSchedule || UsesAdHocSchedule`: sam brak
grafiku nie odróżnia „jeszcze nie ustawiłam" od „świadomie nie prowadzę".

W tym trybie znika ograniczenie „dzień specjalny nie umie powiedzieć «wolne»" (pusty wyjątek jest
kasowany i dzień wraca do grafiku; zamknięcie normalnie otwartego dnia wymaga urlopu) — domyślnie
nie pracujesz, więc dodajesz wyłącznie dni, w które pracujesz.

**Zamknięte 2026-08-07 — checklista skasowana.** `SetupChecklistComponent` liczył krok „Ustaw kiedy
pracujesz" z `getEmployeeSchedules()`, czyli wyłącznie z grafiku POWTARZALNEGO: właścicielka
pracująca z kartki nie mogła go zaliczyć nigdy, a pasek postępu zatrzymywał się na stałe. Zamiast
uczyć checklistę trybu ad-hoc, usunęliśmy ją — trzy z czterech jej kroków (grafik, usługi, zasady)
przejął kreator, więc dublowała pracę i witała świeży salon listą rzeczy właśnie zrobionych.
Zostało jedno zadanie, którego kreator nie wykona za właścicielkę: rozesłać link. Niesie je
`FirstBookingCardComponent` — jeden `hasAnyAppointment()` zamiast czterech zapytań (checklista
dublowała `getEmployeeSchedules()` samego kalendarza), slug z cache `OnboardingStateService`,
znikanie po pierwszej wizycie zamiast flagi w localStorage. Akapit „Już za Ciebie ustawione"
przeniósł się na ekran „Gotowe", gdzie odpowiada na pytanie zadawane w tamtej chwili.

**Otwarte:** copy huba dostępności nadal przeczy trybowi ad-hoc („Twoje godziny pracy, które
powtarzają się co tydzień. To je klienci widzą jako wolne terminy."), a karta dnia specjalnego
obiecuje „lub wolne", czego dzień specjalny nie umie. Katalog przewodników też nie zna wyboru
z kreatora: właścicielce ad-hoc podaje „Ustawmy grafik powtarzalny" jako pierwszy kafelek
(filtr `guidesForRole` patrzy wyłącznie na rolę i trasę).

Jedno zdanie, które musi paść na tym ekranie:
**„Godziny pracy pracownika = terminy widoczne dla klienta. Salon nie ma osobnych godzin otwarcia."**

## Szablony branżowe

Źródło wzorca: `DemoDataSeeder` (kategoria + 5 usług paznokciowych z cenami i czasami + grafik Pn–Sob 9–17).

Nowy `IndustryTemplateCatalog` w `App.Application/Onboarding/` — **statyczny katalog w kodzie**,
nie tabela w bazie (to treść produktowa, nie dane klienta).

Branże: stylizacja paznokci, rzęsy i brwi, fryzjer, barber, kosmetologia, masaż,
makijaż permanentny, depilacja, inne (pusty szablon).

Klik w branżę **nic nie zapisuje**. Następny ekran pokazuje usługi jako zaznaczone karty
z edytowalną ceną i czasem inline; dopiero „Dalej" woła `ApplyIndustryTemplateCommand`, które tworzy
`ServiceCategory`, `Service` i `EmployeeService`. Salon nigdy nie ma usług, których właściciel nie widział.

## Persystencja stanu

**Hasła nie zapisujemy w localStorage nigdy.** Numeru telefonu też nie — to PII lądujące na dysku,
często na współdzielonym komputerze.

Po utworzeniu tenanta **stanem jest baza, nie przeglądarka**. Kreator zapisuje każdy krok od razu
(profil → `Employee`, branża → `Tenant.Industry`, usługi → `Service`, nazwa/link → `Tenant`,
grafik → `EmployeeSchedule`). `GetOnboardingStateQuery` mówi, na którym kroku user jest,
a `/setup` przekierowuje. Powrót na innym urządzeniu wznawia dokładnie tam, gdzie skończył.

localStorage jest buforem wyłącznie na **niezatwierdzony draft bieżącego kroku** (np. ceny poedytowane
przed kliknięciem „Dalej").

Przed tenantem nie ma czego wznawiać poza „gdzie jesteś i na jaki e-mail poszedł link":
`zapisz.onboarding.pending = { email, stage }` (konwencja kluczy zgodna z `zapisz.tour.*`,
`zapisz.dashboard.*`), czyszczone po ukończeniu. Dzięki temu user, który zamknął kartę, po powrocie
widzi *„Dokończ rejestrację — wysłaliśmy link na m\*\*\*@gmail.com"* zamiast pustego ekranu logowania.

## Miny (z weryfikacji kodu)

1. **Sierota w cleanupie.** `UnconfirmedAccountCleanupHostedService` selekcjonuje przez
   `(!EmailConfirmed || !PhoneNumberConfirmed) && CreatedAt < cutoff`. User, który potwierdzi e-mail
   i telefon, ale porzuci kreator na `/setup/profile`, ma obie flagi `true` → **nigdy nie zostanie
   skasowany**. Trzeba drugiego kryterium: „potwierdzony, ale bez `Employee`, starszy niż X".
2. **Promo nie ma gdzie poczekać.** `RedeemForNewTenantAsync(code, Tenant tenant, …)` wymaga tenanta,
   a tenant powstaje później. Kod trzeba przechować → nullable `User.PendingPromoCode`,
   walidowany przy rejestracji (żeby user od razu wiedział o literówce), redemowany przy tworzeniu tenanta.
3. **Promo nigdy się nie zwalnia (istniejący bug).** `PromoCode.IncrementUses()` istnieje,
   `DecrementUses` **nie istnieje w repo**. Skasowanie tenanta usuwa `PromoCodeRedemption` przez cascade,
   ale `promo_codes.current_uses` zostaje podbite — miejsce w `MaxTotalUses` przepada.
   Każde porzucone konto z kodem zjada jedno użycie. Do naprawy przy okazji.
4. **Endpoint tworzący tenant musi sprawdzać `PhoneNumberConfirmed`.** Front to nie zabezpieczenie.
   (Telefon w kroku 1 sprawia, że gate w `Login` działa jak dziś — ale API musi bronić się samo.)
5. **`RemoveTenantDataAsync` nie usuwa `PromoCodeRedemptions` jawnie** (polega na cascade),
   w przeciwieństwie do `TenantPurgeService`. Na In-Memory cascade nie zadziała.

## Co już mamy i tylko podpinamy

Trzy gotowe rzeczy leżą w repo nieużywane:

- ~~**`SetupChecklistComponent`** — kompletna checklista 4 kroków z paskiem postępu i auto-detekcją stanu
  z backendu (`hasAnyAppointment()`, `getEmployeeSchedules()`, `getServices()`). Osierocona przy usuwaniu
  Dashboardu. Wystarczy zaimportować w `visit-schedule` i przeciąć jej kroki z tym, co kreator już zrobił.~~
  **Nieaktualne — podpięta, a potem skasowana 2026-08-07** (patrz „Zamknięte" wyżej). „Przecięcie jej
  kroków z tym, co kreator już zrobił" zostawiało jeden krok, więc checklista straciła rację bytu.
- **`CALENDAR_TOUR`** — gotowa definicja przewodnika driver.js. Kalendarz ma już atrybuty `data-tour`,
  brakuje tylko `TourLauncherComponent`.
- **`EmptyStateComponent`** — wspólny komponent pustego stanu, nieimportowany nigdzie, mimo że pięć list
  ma ręcznie klepane empty-state'y.

Steppera nie ma, PrimeNG Stepper nie jest zainstalowany. Repo buduje UI ręcznie na Tailwindzie
(progresywne ujawnianie na `<details>`), więc: lekki `app-wizard-shell` (pasek postępu, Wstecz/Dalej,
zapis po każdym kroku) zamiast nowej zależności.

## Ręczne testowanie w dev

```
/register (dowolny nowy e-mail)
   ↓  DevTools → Network → register-owner → pole `confirmEmailUrl`
      (albo log backendu: [DEV] Confirm-email URL: …)
klik w link
   ↓  log backendu: [DEV BACKDOOR] Phone OTP — kod=000000
/confirm-phone → wpisz 000000
   ↓
/setup/profile … kreator
```

**Link potwierdzający jest w odpowiedzi `POST /api/auth/register-owner`** (pole `confirmEmailUrl`) —
skrzynka niepotrzebna, logi też nie. Pole jest **wyłącznie w `Development`**: bramka to
`IsDevelopment()`, celowo węższa niż `!IsProduction()` przy logu obok. Log zostaje na serwerze,
a to leci po sieci do wywołującego — poza dev oznaczałoby, że każdy zarejestruje konto na CUDZY
adres i potwierdzi je bez dostępu do skrzynki. Nie rozszerzać na LOCAL_PROD ani Staging;
pinują to testy w `DevConfirmEmailUrlIntegrationTests` (obie strony bramki).

**Kod OTP w dev jest stały: `000000`** (`Auth:DevFixedOtpCode` w `appsettings.Development.json`).
Wymuszany przy GENEROWANIU kodu (`SendPhoneOtpCommandHandler`) — kod jest normalnie hashowany
i zapisywany, więc `ConfirmPhoneCommand` (TTL, licznik prób, lockout) działa identycznie jak na prod.
Flaga jest czytana wyłącznie gdy `IsDevelopment()`; `ValidateProductionConfiguration` **ubija start
Production**, jeśli klucz w ogóle występuje w configu — bez wyjątku dla LOCAL_PROD.

Realny SMS i tak nie leci w dev (`Sms:DevBackdoorLogCode: true` → `LoggingPhoneOtpSender`).
E-mail nie ma kodu (link z tokenem DataProtection), ale w każdym env poza Production jego URL ląduje
w logu — inbox niepotrzebny.

**Powtarzanie kreatora bez zakładania kont:**

```
POST /api/_dev/reset-onboarding     (zalogowany właściciel, dev-only → poza dev 404)
   ↓  kasuje Tenant+Employee+usługi+grafik, ZOSTAWIA potwierdzone konto
/setup/profile … kreator od nowa
```

Po co: każde świeże konto kosztuje unikalny e-mail i jedno z **15 OTP/godzinę na IP**
(`BookingOtpProtection`) — reset zdejmuje oba limity z iteracji. Pod spodem to
`ITenantPurgeService.PurgeAsync(..., deleteOrphanedUsers: false)`; domyślne `true` (hard-delete
salonu z panelu admina, cleanup demo) usuwa konta jak dotąd.

Ograniczenie: `User.PendingPromoCode` jest czyszczony przy pierwszym przejściu kreatora, więc
kolejny przebieg tworzy salon **bez promo** — testy kodów promocyjnych wymagają świeżej rejestracji.

## Podział na PR-y

**PR 1 — backend: slim rejestracja.**
`RegisterOwnerRequest` → `{ Email, Password, PhoneNumber, TurnstileToken?, PromoCode? }`.
Przeniesienie tworzenia Tenant/Employee/VAT/promo do kroku kreatora. `User.PendingPromoCode` (migracja).
Drugie kryterium w cleanupie (sierota bez `Employee`). `PromoCode.DecrementUses()` + wywołanie
w cleanupie i `TenantPurgeService`. Reset hasła skrócony do 1h.
**Aktualizacja ~41 testów integracyjnych w 8 plikach** — rdzeń: `RegisterOwnerConfirmationFlowIntegrationTests`,
`AuthFlowsIntegrationTests`, `AuthApiIntegrationTests` (27 testów).

**PR 2 — backend: moduł onboardingu.**
`App.Application/Onboarding/`: `GetOnboardingStateQuery`, `CompleteProfileCommand` (tworzy Tenant+Employee,
sprawdza `PhoneNumberConfirmed`), `GetIndustryTemplatesQuery`, `ApplyIndustryTemplateCommand`,
`SetOnboardingScheduleCommand`, `CompleteOnboardingCommand`.
Migracja: `Tenant.Industry` + `Tenant.OnboardingCompletedAt`. `IndustryTemplateCatalog`.

**PR 3 — front: kreator.**
`app-wizard-shell`, dziewięć ekranów, `onboardingGuard` na `/admin/**` i `setupGuard` na `/setup`,
przebudowa `/register`, przepięcie `/confirm-email` na `/setup/phone/verify`,
obsługa sesji z `TenantId == null`, banner `zapisz.onboarding.pending`.

**PR 4 — front: przewodniki po panelu.**
Podpięcie `SetupChecklistComponent` i `CALENDAR_TOUR`, ujednolicenie empty-state'ów.

PR 1 i 3 **muszą wyjść na produkcję razem** — inaczej stary formularz rejestracji strzela do nowego
kontraktu. To dokładnie ta pułapka: zmiana kontraktu API bez przebudowy klienta
kończy się błędem dopiero w runtime.
