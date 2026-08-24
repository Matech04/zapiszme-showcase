using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence;

public static class DbSeeder
{
  /// <summary>Ile dni roboczych wstecz od dnia seedu wypełniamy wizytami (historia dla statystyk i kartotek).</summary>
  private const int SeedPastDays = 31;

  /// <summary>Ile dni w przód wypełniamy wizytami (nadchodzące rezerwacje).</summary>
  private const int SeedFutureDays = 31;

  /// <summary>Wielkość puli klientów na salon — przy ~2 miesiącach gęstego kalendarza daje realne kartoteki.</summary>
  private const int SeedCustomersPerTenant = 60;

  /// <summary>Urlop właściciela salonu A — offsety dniowe od dnia seedu. Współdzielone przez generator wizyt i seed urlopu.</summary>
  private const int SalonALeaveStartOffset = 7;
  private const int SalonALeaveEndOffset = 10;

  /// <summary>
  /// Twardy limit SMS salonów dev — praktycznie wyłączony. <c>SmsUsageGuard</c> blokuje CAŁĄ wysyłkę
  /// salonu (łącznie z OTP publicznej rezerwacji) gdy zużycie osiągnie <c>EffectiveMonthlySmsCap</c>,
  /// a siedzi przed klientem smsapi, więc <c>Sms:TestMode=true</c> go NIE omija.
  ///
  /// Bez tego dev rozsypuje się po cichu po ~2 tygodniach: job przypomnień dosypuje ~2 SMS na każdą
  /// zasianą przyszłą wizytę, więc gęsty kalendarz przekracza limit z planu (200 + 150 × stanowisko)
  /// i OTP przestaje działać bez śladu w logach. Limit z planu i tak steruje nadwyżką pokazywaną
  /// na stronie „Zużycie" — ta stała rozjeżdża tylko blokadę, nie raport.
  /// </summary>
  private const int DevSmsHardCap = 100_000;

  private static readonly Guid SalonATenantId = new("00000000-0000-0000-0000-000000000001");
  private static readonly Guid SalonBTenantId = new("00000000-0000-0000-0000-000000000002");

  /// <summary>Salon C (dev seed) — 10 pracowników, do testów kalendarza przy dużej kadrze.</summary>
  private static readonly Guid SalonCTenantId = new("00000000-0000-0000-0000-000000000003");

  /// <summary>Identity user id — właściciel salonu C.</summary>
  private static readonly Guid OwnerCUserId = new("4d5e6f7a-8b9c-40d1-9e2f-3a4b5c6d7e8f");

  private static readonly string[] IdentityRoles = ["Admin", "Owner", "Manager", "Employee", "Kiosk"];

  /// <summary>Identity user id — właściciel salonu A.</summary>
  private static readonly Guid OwnerAUserId = new("55eb4ae2-0cbe-400a-b5b4-678ab42d151a");

  /// <summary>Identity user id — właściciel salonu B.</summary>
  private static readonly Guid OwnerBUserId = new("04d3f2ae-6dc1-4c76-b9c4-75b31819104c");

  /// <summary>Identity user id — manager salonu B.</summary>
  private static readonly Guid ManagerBUserId = new("1a2b3c4d-5e6f-47a8-9b0c-1d2e3f4a5b6c");

  /// <summary>Identity user id — pracownik salonu B (1).</summary>
  private static readonly Guid WorkerB1UserId = new("2b3c4d5e-6f7a-48b9-9c0d-1e2f3a4b5c6d");

  /// <summary>Identity user id — pracownik salonu B (2).</summary>
  private static readonly Guid WorkerB2UserId = new("3c4d5e6f-7a8b-49c0-9d1e-2f3a4b5c6d7e");

  /// <summary>Identity user id — demo system admin (dev seed only).</summary>
  private static readonly Guid DemoAdminUserId = new("99999999-9999-9999-9999-999999999999");

  public static async Task SeedAsync(ApplicationDbContext context)
  {
    await SeedIdentityRolesAsync(context);

    if (!await context.Tenants.AnyAsync())
    {
      await SeedFullDemoAsync(context);
    }

    // Idempotentny dosiew demo-admina — pokrywa przypadek gdy dev-DB istniała przed
    // dodaniem admina do SeedFullDemoAsync (świeży seed by go wstawił, ale stara baza
    // już ma tenanty więc SeedFullDemoAsync nie odpali).
    await EnsureDemoAdminAsync(context);

    await EnsureDemoTenantsOnboardedAsync(context);

    await SeedSalonAEmployeeLeavesIfMissingAsync(context);
  }

  /// <summary>
  /// Stempluje salony demo jako „po kreatorze". Wydzielone, bo woła to i świeży seed, i dosiew
  /// dla istniejących dev-baz — w obu miejscach obowiązuje ta sama reguła.
  /// </summary>
  private static void MarkDemoTenantsOnboarded(params Tenant[] tenants)
  {
    var nowUtc = DateTime.UtcNow;
    foreach (var tenant in tenants)
    {
      tenant.MarkOnboardingCompleted(nowUtc);
    }
  }

  /// <summary>
  /// Idempotentny dosiew stempla onboardingu dla dev-baz zasianych, ZANIM seed zaczął go ustawiać.
  /// Bez tego istniejące bazy demo zostają z <c>onboarding_completed_at = NULL</c> i każde
  /// logowanie na salon demo ląduje w kreatorze zakładania salonu.
  ///
  /// Ruszamy WYŁĄCZNIE trzy znane Id salonów demo — nigdy cudzych tenantów, nawet jeśli seed
  /// odpali się na bazie z realnymi danymi.
  /// </summary>
  private static async Task EnsureDemoTenantsOnboardedAsync(ApplicationDbContext context)
  {
    Guid[] demoTenantIds = [SalonATenantId, SalonBTenantId, SalonCTenantId];

    var stale = await context.Tenants
      .Where(t => demoTenantIds.Contains(t.Id) && t.OnboardingCompletedAt == null)
      .ToListAsync();

    if (stale.Count == 0)
    {
      return;
    }

    MarkDemoTenantsOnboarded([.. stale]);
    await context.SaveChangesAsync();
  }

  private static async Task EnsureDemoAdminAsync(ApplicationDbContext context)
  {
    if (await context.Users.AnyAsync(u => u.Id == DemoAdminUserId)) return;

    var adminRoleId = await context.Roles
      .Where(r => r.NormalizedName == "ADMIN")
      .Select(r => r.Id)
      .SingleAsync();

    var adminUser = CreateDemoUser(DemoAdminUserId, "admin@dev.local", "Demo Admin");
    context.Users.Add(adminUser);
    context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = adminUser.Id, RoleId = adminRoleId });
    await context.SaveChangesAsync();
  }

  /// <summary>
  /// SeedAsync uruchamia się tylko w dev / demo. Dla prod jest osobna ścieżka, która:
  /// 1) Zapewnia, że role są utworzone.
  /// 2) Jeśli ENV-y BOOKING_ADMIN_EMAIL + BOOKING_ADMIN_PASSWORD są ustawione i admina
  ///    o tym mailu nie ma w bazie — tworzy konto Admin (idempotentnie).
  /// Hasło NIGDY nie jest hard-coded — bierzemy je z env-var (Key Vault → App Service env).
  /// </summary>
  public static async Task EnsureProductionBootstrapAsync(ApplicationDbContext context)
  {
    await SeedIdentityRolesAsync(context);

    var email = Environment.GetEnvironmentVariable("BOOKING_ADMIN_EMAIL");
    var password = Environment.GetEnvironmentVariable("BOOKING_ADMIN_PASSWORD");

    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
      // Brak konfiguracji → nie blokujemy startu; ops dodaje admina przez migrate-step lub kolejny restart.
      return;
    }

    var normalized = email.ToUpperInvariant();
    if (await context.Users.AnyAsync(u => u.NormalizedEmail == normalized))
    {
      return;
    }

    // Fail-fast na słabe hasło admina — hashujemy dopiero po tym checku. Dotyczy tylko TWORZENIA
    // nowego konta (istniejący admin zwraca wyżej), więc nie wywróci istniejących deployów.
    if (password.Length < 12)
    {
      throw new InvalidOperationException(
        "BOOKING_ADMIN_PASSWORD musi mieć co najmniej 12 znaków — odmawiam utworzenia konta admina ze słabym hasłem.");
    }

    var adminRoleId = await context.Roles
      .Where(r => r.NormalizedName == "ADMIN")
      .Select(r => r.Id)
      .SingleAsync();

    var user = new User(email, email)
    {
      Id = Guid.NewGuid(),
      EmailConfirmed = true,
      NormalizedEmail = normalized,
      NormalizedUserName = normalized,
      SecurityStamp = Guid.NewGuid().ToString(),
      ConcurrencyStamp = Guid.NewGuid().ToString(),
    };
    user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);

    context.Users.Add(user);
    context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = adminRoleId });
    await context.SaveChangesAsync();
  }

  private static async Task SeedFullDemoAsync(ApplicationDbContext context)
  {
    var roleIdByName = await context.Roles
      .Where(r => r.NormalizedName == "ADMIN"
               || r.NormalizedName == "OWNER"
               || r.NormalizedName == "MANAGER"
               || r.NormalizedName == "EMPLOYEE")
      .ToDictionaryAsync(r => r.NormalizedName!, r => r.Id);

    var adminRoleId = roleIdByName["ADMIN"];
    var ownerRoleId = roleIdByName["OWNER"];
    var managerRoleId = roleIdByName["MANAGER"];
    var employeeRoleId = roleIdByName["EMPLOYEE"];

    // Demo admin (dev only) — uzupełnia brak SystemAdmin w demo seed. Hasło Password123! (jak pozostali).
    // Email "admin@dev.local" jest jawnie testowy; produkcyjny admin idzie ścieżką EnsureProductionBootstrapAsync.
    var adminUser = CreateDemoUser(DemoAdminUserId, "admin@dev.local", "Demo Admin");
    var ownerAUser = CreateDemoUser(OwnerAUserId, "owner-a@salon-a.local", "Właściciel Salonu A");
    var ownerBUser = CreateDemoUser(OwnerBUserId, "owner-b@salon-b.local", "Właściciel Salonu B");
    var managerBUser = CreateDemoUser(ManagerBUserId, "manager-b@salon-b.local", "Manager Salonu B");
    var workerB1User = CreateDemoUser(WorkerB1UserId, "worker-b1@salon-b.local", "Pracownik B1");
    var workerB2User = CreateDemoUser(WorkerB2UserId, "worker-b2@salon-b.local", "Pracownik B2");

    await context.Users.AddRangeAsync(adminUser, ownerAUser, ownerBUser, managerBUser, workerB1User, workerB2User);
    await context.UserRoles.AddRangeAsync(
      new IdentityUserRole<Guid> { UserId = adminUser.Id, RoleId = adminRoleId },
      new IdentityUserRole<Guid> { UserId = ownerAUser.Id, RoleId = ownerRoleId },
      new IdentityUserRole<Guid> { UserId = ownerBUser.Id, RoleId = ownerRoleId },
      new IdentityUserRole<Guid> { UserId = managerBUser.Id, RoleId = managerRoleId },
      new IdentityUserRole<Guid> { UserId = workerB1User.Id, RoleId = employeeRoleId },
      new IdentityUserRole<Guid> { UserId = workerB2User.Id, RoleId = employeeRoleId });

    // 1. TENANTS — dwa niezależne salony (ustalone Id pod klienta / demo)
    var salonA = new Tenant("Salon A", "salon-a", "Europe/Warsaw", "PLN");
    typeof(Entity).GetProperty("Id")!.SetValue(salonA, SalonATenantId);

    // Salon B trzymamy w tej samej strefie/walucie co salon A — dzięki temu testy ról
    // w kalendarzu nie mieszają się z konwersjami stref czasowych (w NY tz slot 11:00
    // jest „w przyszłości" gdy w Warszawie już jest 17:00). Ceny usług i tak były w PLN,
    // więc Currency=USD było wewnętrznie niespójne. Jeśli kiedyś będzie potrzeba testu
    // multi-TZ — wystarczy zmienić tylko TimeZoneId tutaj.
    var salonB = new Tenant("Salon B", "salon-b", "Europe/Warsaw", "PLN");
    typeof(Entity).GetProperty("Id")!.SetValue(salonB, SalonBTenantId);

    // Salony demo są z definicji „po kreatorze" — mają komplet usług, grafików i wizyt.
    // Bez tego stempla `onboardingGuard` wypycha KAŻDE logowanie demo na /setup, bo backfill
    // z migracji AddOnboardingFields jest jednorazowy (UPDATE istniejących wierszy) i na
    // świeżej bazie nie ma czego zaktualizować — seed wstawia tenanty dopiero po nim.
    MarkDemoTenantsOnboarded(salonA, salonB);

    await context.Tenants.AddRangeAsync(salonA, salonB);

    // 2. VAT
    var vat23A = new VatRate(salonA.Id, "23%", 0.23m, true);
    var vat23B = new VatRate(salonB.Id, "23%", 0.23m, true);

    await context.Set<VatRate>().AddRangeAsync(vat23A, vat23B);

    // 3. Kategorie i usługi
    var catA = new ServiceCategory(salonA.Id, "Usługi", 1);
    var catB = new ServiceCategory(salonB.Id, "Usługi", 1);

    await context.Set<ServiceCategory>().AddRangeAsync(catA, catB);

    var serviceA = new Service(salonA.Id, catA.Id, vat23A.Id, "Strzyżenie", new Money(100m, "PLN"), 45);
    var serviceB = new Service(salonB.Id, catB.Id, vat23B.Id, "Strzyżenie", new Money(80m, "PLN"), 40);

    await context.Services.AddRangeAsync(serviceA, serviceB);

    // 4. Właściciele powiązani z użytkownikami Identity.
    var ownerA = new Employee(salonA.Id, ownerAUser.Id, "Właściciel", "Salonu A", ownerAUser.Email!);
    var ownerB = new Employee(salonB.Id, ownerBUser.Id, "Właściciel", "Salonu B", ownerBUser.Email!);

    // Domyślny grafik bezterminowy (od dziś), tak by demo "działało od ręki".
    // Użytkownik może go skrócić/usunąć i dodać własne grafiki przez UI.
    var workWeek = new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>>();
    var dayRange = new List<TimeRange> { new TimeRange(new TimeOnly(9, 0), new TimeOnly(17, 0)) };
    foreach (var day in new[]
             {
               DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
             })
    {
      workWeek[day] = dayRange;
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    // Uwaga: zakres bierzemy z SeededScheduleRange(today) PRZY KAŻDYM grafiku — patrz komentarz
    // przy tej metodzie. Współdzielenie jednej instancji cicho gubi active_from wszystkim
    // pracownikom poza pierwszym.

    ownerA.AddSchedule(SeededScheduleRange(today), 1, BuildSeededScheduleDays(workWeek));
    ownerB.AddSchedule(SeededScheduleRange(today), 1, BuildSeededScheduleDays(workWeek));

    ownerA.AssignService(salonA.Id, serviceA.Id, 45, new Money(100m, "PLN"));
    ownerB.AssignService(salonB.Id, serviceB.Id, 40, new Money(80m, "PLN"));

    // Dodatkowa kadra salonu B: manager + 2 pracowników — wszyscy z domyślnym grafikiem 9-17 pon-pt
    // i przypisaną usługą "Strzyżenie", żeby kalendarz / role w UI miały sensowne demo data.
    var managerB = new Employee(salonB.Id, managerBUser.Id, "Anna", "Kowalska", managerBUser.Email!);
    var workerB1 = new Employee(salonB.Id, workerB1User.Id, "Jan", "Nowak", workerB1User.Email!);
    var workerB2 = new Employee(salonB.Id, workerB2User.Id, "Piotr", "Wiśniewski", workerB2User.Email!);

    managerB.AddSchedule(SeededScheduleRange(today), 1, BuildSeededScheduleDays(workWeek));
    workerB1.AddSchedule(SeededScheduleRange(today), 1, BuildSeededScheduleDays(workWeek));
    workerB2.AddSchedule(SeededScheduleRange(today), 1, BuildSeededScheduleDays(workWeek));

    managerB.AssignService(salonB.Id, serviceB.Id, 40, new Money(80m, "PLN"));
    workerB1.AssignService(salonB.Id, serviceB.Id, 40, new Money(80m, "PLN"));
    workerB2.AssignService(salonB.Id, serviceB.Id, 40, new Money(80m, "PLN"));

    await context.Employees.AddRangeAsync(ownerA, ownerB, managerB, workerB1, workerB2);

    // 5. Klienci demo
    var custA = new Customer(salonA.Id, "Klient", "A", "klient@salon-a.local", new PhoneNumber("+48500100100"), "");
    var custB = new Customer(salonB.Id, "Klient", "B", "klient@salon-b.local", new PhoneNumber("+48500200200"), "");

    await context.Customers.AddRangeAsync(custA, custB);

    // 6. Pula klientów + gęsty kalendarz wizyt (miesiąc wstecz i w przód), powiadomienia do dzwonka
    //    oraz dziennik zużycia SMS/e-mail. Status liczony wg LOKALNEGO „teraz" (oba salony
    //    Europe/Warsaw), spójnie z lifecycle: przeszłe sloty = Completed, slot trwający = InProgress,
    //    przyszłe = Booked/Pending.
    var warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    var nowUtc = DateTime.UtcNow;
    var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, warsaw);

    // Liczba stanowisk = realna kadra. Steruje limitem SMS (200 + 150 × kolejne stanowisko),
    // więc bez tego strona „Zużycie" pokazywałaby salonowi na 10 osób limit dla jednoosobowego.
    salonB.Subscription.ChangeSeats(4);

    salonA.Subscription.SetMonthlySmsHardCap(DevSmsHardCap);
    salonB.Subscription.SetMonthlySmsHardCap(DevSmsHardCap);

    var appointmentsA = await SeedDemoCustomersAndAppointmentsAsync(
      context, today, nowLocal, salonA.Id, serviceA.Id, durationMinutes: 45, priceAmount: 100m,
      currency: "PLN", employeeIds: new[] { ownerA.Id }, phoneBase: 500_300_000,
      leaveWindow: (ownerA.Id, today.AddDays(SalonALeaveStartOffset), today.AddDays(SalonALeaveEndOffset)));

    var appointmentsB = await SeedDemoCustomersAndAppointmentsAsync(
      context, today, nowLocal, salonB.Id, serviceB.Id, durationMinutes: 40, priceAmount: 80m,
      currency: "PLN", employeeIds: new[] { ownerB.Id, managerB.Id, workerB1.Id, workerB2.Id },
      phoneBase: 500_400_000);

    await SeedNotificationsAsync(
      context, salonA.Id, new[] { (OwnerAUserId, ownerA.Id) }, appointmentsA, today, nowUtc);

    await SeedNotificationsAsync(
      context,
      salonB.Id,
      new[]
      {
        (OwnerBUserId, ownerB.Id), (ManagerBUserId, managerB.Id),
        (WorkerB1UserId, workerB1.Id), (WorkerB2UserId, workerB2.Id),
      },
      appointmentsB,
      today,
      nowUtc);

    // Salon A celowo PRZEKRACZA limit z planu (1 stanowisko = 200 SMS) — strona „Zużycie" ma pokazywać
    // realną nadwyżkę i jej koszt, a nie zawsze zielony licznik. Blokady to nie wywołuje, bo twardy
    // limit jest podniesiony do DevSmsHardCap (patrz komentarz przy tej stałej).
    await SeedNotificationUsageAsync(
      context, salonA.Id, smsThisMonth: 243, emailThisMonth: 120, smsPrevMonth: 180, emailPrevMonth: 95, nowUtc);

    await SeedNotificationUsageAsync(
      context, salonB.Id, smsThisMonth: 410, emailThisMonth: 260, smsPrevMonth: 520, emailPrevMonth: 300, nowUtc);

    // ── Salon C — 10 pracowników (właściciel + 9 zasobów kalendarza), do testów kalendarza
    //    przy dużej kadrze (kolumny, przewijanie, filtr pracowników). Wszyscy z grafikiem 9-17
    //    pon-pt i usługą „Strzyżenie", plus wizyty demo — kalendarz ma realnie wyglądać. ──
    var ownerCUser = CreateDemoUser(OwnerCUserId, "owner-c@salon-c.local", "Właściciel Salonu C");
    context.Users.Add(ownerCUser);
    context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = ownerCUser.Id, RoleId = ownerRoleId });

    var salonC = new Tenant("Salon C", "salon-c", "Europe/Warsaw", "PLN");
    typeof(Entity).GetProperty("Id")!.SetValue(salonC, SalonCTenantId);
    MarkDemoTenantsOnboarded(salonC);
    context.Tenants.Add(salonC);

    var vat23C = new VatRate(salonC.Id, "23%", 0.23m, true);
    context.Set<VatRate>().Add(vat23C);
    var catC = new ServiceCategory(salonC.Id, "Usługi", 1);
    context.Set<ServiceCategory>().Add(catC);
    var serviceC = new Service(salonC.Id, catC.Id, vat23C.Id, "Strzyżenie", new Money(90m, "PLN"), 45);
    context.Services.Add(serviceC);

    var ownerC = new Employee(salonC.Id, ownerCUser.Id, "Właściciel", "Salonu C", ownerCUser.Email!);
    ownerC.AddSchedule(SeededScheduleRange(today), 1, BuildSeededScheduleDays(workWeek));
    ownerC.AssignService(salonC.Id, serviceC.Id, 45, new Money(90m, "PLN"));

    var salonCStaffNames = new (string First, string Last)[]
    {
      ("Magda", "Lewandowska"), ("Ola", "Zielińska"), ("Kasia", "Szymańska"),
      ("Ewa", "Woźniak"), ("Natalia", "Dąbrowska"), ("Karolina", "Kozłowska"),
      ("Paulina", "Jankowska"), ("Marta", "Mazur"), ("Aga", "Krawczyk"),
    };

    var salonCEmployees = new List<Employee> { ownerC };
    var staffIndex = 1;
    foreach (var (first, last) in salonCStaffNames)
    {
      var emp = new Employee(salonC.Id, null, first, last, $"staff{staffIndex}@salon-c.local");
      emp.AddSchedule(SeededScheduleRange(today), 1, BuildSeededScheduleDays(workWeek));
      emp.AssignService(salonC.Id, serviceC.Id, 45, new Money(90m, "PLN"));
      salonCEmployees.Add(emp);
      staffIndex++;
    }

    await context.Employees.AddRangeAsync(salonCEmployees);

    salonC.Subscription.ChangeSeats(salonCEmployees.Count);
    salonC.Subscription.SetMonthlySmsHardCap(DevSmsHardCap);

    var appointmentsC = await SeedDemoCustomersAndAppointmentsAsync(
      context, today, nowLocal, salonC.Id, serviceC.Id, durationMinutes: 45, priceAmount: 90m,
      currency: "PLN", employeeIds: salonCEmployees.Select(e => e.Id).ToArray(),
      phoneBase: 500_500_000);

    // Kadra salonu C poza właścicielem nie ma kont Identity, więc dzwonek dostaje tylko właściciel.
    await SeedNotificationsAsync(
      context, salonC.Id, new[] { (OwnerCUserId, ownerC.Id) }, appointmentsC, today, nowUtc);

    await SeedNotificationUsageAsync(
      context, salonC.Id, smsThisMonth: 980, emailThisMonth: 600, smsPrevMonth: 1210, emailPrevMonth: 700, nowUtc);

    await context.SaveChangesAsync();
  }

  /// <summary>
  /// Seeduje pulę klientów i gęsty kalendarz wizyt dla jednego salonu — wszystkie dni robocze
  /// od <see cref="SeedPastDays"/> wstecz do <see cref="SeedFutureDays"/> w przód. Zwraca zasiane
  /// wizyty, żeby wywołujący mógł podpiąć je jako <c>ReferenceId</c> powiadomień (deep-link z dzwonka).
  ///
  /// Gęstość dnia jest zmienna (<see cref="DayFillRatio"/>): większość dni ~70-90%, część dni
  /// w komplecie (test „brak wolnych terminów"), część luźna (test rezerwacji i gap fillingu).
  /// Dziury w siatce są celowe — <c>PreferAdjacent</c> ma co preferować.
  ///
  /// Status liczony wg rzeczywistego czasu slotu vs <paramref name="nowLocal"/> (nie wg samej daty),
  /// spójnie z <c>AppointmentStatusLifecycleRules</c>: slot zakończony = Completed (część Canceled),
  /// trwający = InProgress, przyszły = Booked (część Pending).
  ///
  /// Sloty w obrębie (pracownik, dzień) są rozłączne i unikalne, więc filtrowany UNIQUE
  /// (EmployeeId, Date, StartTime) WHERE Status &lt;&gt; 'Canceled' nie jest naruszony.
  /// </summary>
  private static async Task<List<Appointment>> SeedDemoCustomersAndAppointmentsAsync(
    ApplicationDbContext context,
    DateOnly today,
    DateTime nowLocal,
    Guid tenantId,
    Guid serviceId,
    int durationMinutes,
    decimal priceAmount,
    string currency,
    IReadOnlyList<Guid> employeeIds,
    long phoneBase,
    (Guid EmployeeId, DateOnly Start, DateOnly End)? leaveWindow = null)
  {
    var customers = BuildDemoCustomers(tenantId, phoneBase);
    await context.Customers.AddRangeAsync(customers);

    var dayStart = new TimeOnly(9, 0);
    var dayEnd = new TimeOnly(17, 0);
    var slotCount = (int)((dayEnd - dayStart).TotalMinutes / durationMinutes);

    var appointments = new List<Appointment>();
    var custIdx = 0;

    for (var e = 0; e < employeeIds.Count; e++)
    {
      var employeeId = employeeIds[e];

      for (var dayOffset = -SeedPastDays; dayOffset <= SeedFutureDays; dayOffset++)
      {
        var date = today.AddDays(dayOffset);
        if (!IsWeekday(date))
        {
          continue; // grafik demo to pon-pt
        }

        var onLeave = leaveWindow is { } lw
                      && lw.EmployeeId == employeeId
                      && date >= lw.Start
                      && date <= lw.End;

        // Urlop: normalnie pusto. Zostawiamy JEDNĄ celową wizytę w pierwszym dniu urlopu, żeby alert
        // „poza grafikiem" w dzwonku miał co pokazać — pełna siatka w urlopie zalałaby go dziesiątkami
        // alertów i uczyniła bezużytecznym.
        if (onLeave && date != leaveWindow!.Value.Start)
        {
          continue;
        }

        for (var slot = 0; slot < slotCount; slot++)
        {
          if (onLeave)
          {
            if (slot != 2)
            {
              continue;
            }
          }
          else if (Roll(e, dayOffset, slot + 1) >= DayFillRatio(e, dayOffset))
          {
            continue; // slot zostaje wolny
          }

          var start = dayStart.AddMinutes(slot * durationMinutes);
          var end = start.AddMinutes(durationMinutes);
          var customer = customers[custIdx++ % customers.Count];

          var startLocal = date.ToDateTime(start);
          var endLocal = date.ToDateTime(end);
          AppointmentStatus status;
          if (endLocal <= nowLocal)
          {
            status = Roll(e, dayOffset, slot + 500) < 12 ? AppointmentStatus.Canceled : AppointmentStatus.Completed;
          }
          else if (startLocal <= nowLocal)
          {
            status = AppointmentStatus.InProgress;
          }
          else
          {
            status = Roll(e, dayOffset, slot + 900) < 15 ? AppointmentStatus.Pending : AppointmentStatus.Booked;
          }

          var source = Roll(e, dayOffset, slot + 300) < 40 ? AppointmentSource.Online : AppointmentSource.Panel;

          // Osobna instancja Money per wizyta — owned type EF nie może współdzielić referencji
          // (współdzielona instancja → kolumny total_price_* lądują jako NULL).
          appointments.Add(new Appointment(
            tenantId, employeeId, serviceId, customer.Id,
            date, start, end, status, new Money(priceAmount, currency), "", null, source));
        }
      }
    }

    await context.Appointments.AddRangeAsync(appointments);
    return appointments;
  }

  private static List<Customer> BuildDemoCustomers(Guid tenantId, long phoneBase)
  {
    string[] firstNames =
      ["Anna", "Katarzyna", "Maria", "Magdalena", "Agnieszka", "Barbara", "Ewa", "Joanna", "Monika", "Zofia"];
    string[] lastNames =
      ["Nowak", "Kowalska", "Wiśniewska", "Wójcik", "Kamińska", "Lewandowska", "Zielińska", "Szymańska", "Woźniak", "Dąbrowska"];

    var customers = new List<Customer>(SeedCustomersPerTenant);
    for (var i = 0; i < SeedCustomersPerTenant; i++)
    {
      // 10 imion × 10 nazwisk — para (i % 10, i / 10) jest unikalna dla i < 100.
      var first = firstNames[i % firstNames.Length];
      var last = lastNames[(i / firstNames.Length) % lastNames.Length];
      var phone = new PhoneNumber($"+48{phoneBase + i}");
      var email = $"klient{i + 1}.{tenantId.ToString()[..4]}@example.com";
      customers.Add(new Customer(tenantId, first, last, email, phone, ""));
    }

    return customers;
  }

  /// <summary>
  /// Docelowe wypełnienie dnia w procentach (0-100) dla danego pracownika. Deterministyczne —
  /// ten sam seed daje ten sam kalendarz, ale rozkład wygląda organicznie. Średnia ~78%.
  /// </summary>
  private static int DayFillRatio(int employeeIndex, int dayOffset) =>
    Roll(employeeIndex, dayOffset, 0) switch
    {
      < 12 => 100, // komplet — test „brak wolnych terminów"
      < 24 => 40,  // luźny dzień — test rezerwacji / gap fillingu
      < 45 => 70,
      < 75 => 80,
      _ => 90,
    };

  /// <summary>
  /// Deterministyczny „rzut kostką" 0-99 z trzech liczb. Zamiast <c>Random</c>, żeby seed był
  /// powtarzalny (ten sam dzień → ten sam kalendarz) i niezależny od kolejności wywołań.
  /// </summary>
  private static int Roll(int a, int b, int salt)
  {
    unchecked
    {
      var h = 17;
      h = (h * 31) + a;
      h = (h * 31) + b;
      h = (h * 31) + salt;
      h ^= h >> 15;
      h *= 0x2c1b3c6d;
      h ^= h >> 12;
      return (h & 0x7fffffff) % 100;
    }
  }

  private static bool IsWeekday(DateOnly d) =>
    d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday;

  /// <summary>
  /// Zakres obowiązywania grafiku demo — ŚWIEŻA instancja na każde wywołanie. Startuje przed oknem
  /// seedowanych wizyt, żeby zasiana historia mieściła się w godzinach pracy (inaczej kalendarz rysuje
  /// przeszłe dni jako nierobocze z wizytami na nich, a dzwonek zalewa się alertami „poza grafikiem").
  ///
  /// Instancji NIE WOLNO współdzielić między pracownikami: <c>DateRange</c> jest owned-typem EF
  /// (<c>OwnsOne(x =&gt; x.ActiveRange)</c>), a EF deduplikuje owned-encje po referencji — jedna
  /// instancja wpięta w kilka grafików oznacza, że tylko pierwszy dostaje swoje <c>active_from</c>,
  /// a pozostałym po cichu ląduje default bazy ('-infinity'). Bez wyjątku i bez błędu przy zapisie.
  /// </summary>
  private static DateRange SeededScheduleRange(DateOnly today) =>
    new(today.AddDays(-(SeedPastDays + 7)), DateOnly.MaxValue);

  private sealed record NotificationSpec(NotificationType Type, string Subject, string ShortText);

  /// <summary>
  /// Po jednym powiadomieniu każdego <see cref="NotificationType"/> — dzwonek pokazuje pełną
  /// taksonomię ikon/kolorów bez czekania, aż zdarzenie wystąpi naturalnie.
  /// </summary>
  private static readonly NotificationSpec[] NotificationCatalog =
  [
    new(NotificationType.NewBookingToSalon, "Nowa rezerwacja online", "Klientka zarezerwowała termin przez stronę."),
    new(NotificationType.AwaitingConfirmationToSalon, "Rezerwacja czeka na potwierdzenie", "Tryb ręczny — wizyta czeka na Twoją decyzję."),
    new(NotificationType.CancellationToSalon, "Klientka odwołała wizytę", "Termin zwolnił się w kalendarzu."),
    new(NotificationType.RescheduleToSalon, "Klientka przełożyła wizytę", "Wizyta ma nowy termin."),
    new(NotificationType.BookingConfirmationToCustomer, "Wysłano potwierdzenie rezerwacji", "Klientka dostała potwierdzenie terminu."),
    new(NotificationType.CancellationToCustomer, "Wysłano potwierdzenie odwołania", "Klientka dostała potwierdzenie anulowania."),
    new(NotificationType.RescheduleToCustomer, "Wysłano potwierdzenie przełożenia", "Klientka dostała nowy termin wizyty."),
    new(NotificationType.AppointmentReminderToCustomer, "Przypomnienie 24h wysłane", "Klientka dostała przypomnienie dzień przed wizytą."),
    new(NotificationType.AppointmentReminder2hToCustomer, "Przypomnienie 2h wysłane", "Klientka dostała przypomnienie przed wizytą."),
    new(NotificationType.CancelledBySalonToCustomer, "Odwołano wizytę klientce", "Klientka została powiadomiona o odwołaniu."),
    new(NotificationType.RescheduledBySalonToCustomer, "Przełożono wizytę klientce", "Klientka została powiadomiona o zmianie terminu."),
    new(NotificationType.StaffBookedAppointmentToCustomer, "Wystawiono wizytę z panelu", "Klientka dostała potwierdzenie wizyty dodanej ręcznie."),
    new(NotificationType.CustomerVerificationOtp, "Wysłano kod weryfikacyjny", "Klientka potwierdza rezerwację kodem."),
    new(NotificationType.DepositLinkToCustomer, "Wysłano link do zadatku", "Klientka dostała link do opłacenia zadatku."),
  ];

  /// <summary>
  /// Zasiewa powiadomienia in-app (dzwonek) dla podanych kont. <c>RecipientUserId</c> MUSI być
  /// Identity-userem, na którego się logujesz — <c>GetNotificationsQuery</c> filtruje po nim, więc
  /// wiersz z <c>null</c> jest niewidoczny dla wszystkich poza kontem Recepcji (Kiosk).
  /// <c>ReferenceId</c> wskazuje realną wizytę tego pracownika, żeby deep-link z dzwonka działał.
  /// </summary>
  private static async Task SeedNotificationsAsync(
    ApplicationDbContext context,
    Guid tenantId,
    IReadOnlyList<(Guid UserId, Guid EmployeeId)> recipients,
    IReadOnlyList<Appointment> appointments,
    DateOnly today,
    DateTime nowUtc)
  {
    // Najnowsze kilka zostaje nieprzeczytanych — dzwonek ma pokazać licznik, a nie samo zero.
    const int UnreadNewest = 4;

    var rows = new List<Notification>(recipients.Count * NotificationCatalog.Length);

    foreach (var (userId, employeeId) in recipients)
    {
      // Wizyty blisko „dziś" — klik z dzwonka ląduje tam, gdzie użytkownik i tak patrzy.
      var refs = appointments
        .Where(a => a.EmployeeId == employeeId)
        .OrderBy(a => Math.Abs(a.Date.DayNumber - today.DayNumber))
        .Select(a => a.Id)
        .Take(NotificationCatalog.Length)
        .ToArray();

      for (var i = 0; i < NotificationCatalog.Length; i++)
      {
        var spec = NotificationCatalog[i];
        // i = 0 najnowsze; rozrzut co ~7h wstecz mieści komplet w ostatnich ~4 dniach.
        var createdAt = nowUtc.AddHours(-((i * 7) + 1));
        var referenceId = refs.Length > 0 ? refs[i % refs.Length] : (Guid?)null;

        var notification = Notification.Create(
          tenantId, userId, spec.Type, spec.Subject, spec.ShortText, referenceId, createdAt);

        if (i >= UnreadNewest)
        {
          notification.MarkRead(createdAt.AddMinutes(30));
        }

        rows.Add(notification);
      }
    }

    await context.Notifications.AddRangeAsync(rows);
  }

  /// <summary>
  /// Zasiewa dziennik wysyłek SMS/e-mail zasilający „Zużycie SMS / e-mail". Rozkłada wpisy na
  /// bieżący miesiąc (od 1. do teraz) i miesiąc poprzedni, żeby przełącznik miesiąca miał dane.
  /// </summary>
  private static async Task SeedNotificationUsageAsync(
    ApplicationDbContext context,
    Guid tenantId,
    int smsThisMonth,
    int emailThisMonth,
    int smsPrevMonth,
    int emailPrevMonth,
    DateTime nowUtc)
  {
    var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    var prevStart = monthStart.AddMonths(-1);

    var rows = new List<NotificationUsageRecord>(smsThisMonth + emailThisMonth + smsPrevMonth + emailPrevMonth);
    rows.AddRange(BuildUsageWindow(tenantId, monthStart, nowUtc, smsThisMonth, emailThisMonth, salt: 1));
    rows.AddRange(BuildUsageWindow(tenantId, prevStart, monthStart, smsPrevMonth, emailPrevMonth, salt: 2));

    await context.NotificationUsage.AddRangeAsync(rows);
  }

  private static IEnumerable<NotificationUsageRecord> BuildUsageWindow(
    Guid tenantId, DateTime windowStart, DateTime windowEnd, int smsCount, int emailCount, int salt)
  {
    // Typy realnie wychodzące danym kanałem — rozkład ma przypominać ruch salonu,
    // żeby rozbicie „wg typu" na stronie zużycia nie było płaskie.
    NotificationType[] smsTypes =
    [
      NotificationType.CustomerVerificationOtp,
      NotificationType.AppointmentReminderToCustomer,
      NotificationType.AppointmentReminder2hToCustomer,
      NotificationType.BookingConfirmationToCustomer,
      NotificationType.CancellationToCustomer,
      NotificationType.DepositLinkToCustomer,
    ];
    NotificationType[] emailTypes =
    [
      NotificationType.BookingConfirmationToCustomer,
      NotificationType.NewBookingToSalon,
      NotificationType.CancellationToSalon,
      NotificationType.RescheduleToSalon,
      NotificationType.AppointmentReminderToCustomer,
    ];

    var span = windowEnd - windowStart;

    for (var i = 0; i < smsCount; i++)
    {
      var sentAt = windowStart + (span * ((i + 0.5) / smsCount));
      // ~5% wysyłek nieudanych — panel ma pokazywać też kolumnę błędów.
      var success = Roll(i, salt, 11) >= 5;
      // Część SMS-ów wielosegmentowa (polskie znaki → 70 znaków/segment) → 2 punkty.
      var points = Roll(i, salt, 22) < 25 ? 2m : 1m;
      yield return NotificationUsageRecord.ForSms(
        tenantId, smsTypes[Roll(i, salt, 33) % smsTypes.Length], success, points, sentAt);
    }

    for (var i = 0; i < emailCount; i++)
    {
      var sentAt = windowStart + (span * ((i + 0.5) / emailCount));
      var success = Roll(i, salt, 44) >= 3;
      yield return NotificationUsageRecord.ForEmail(
        tenantId, emailTypes[Roll(i, salt, 55) % emailTypes.Length], success, sentAt);
    }
  }

  private static async Task SeedIdentityRolesAsync(ApplicationDbContext context)
  {
    foreach (var roleName in IdentityRoles)
    {
      var normalizedName = roleName.ToUpperInvariant();
      if (await context.Roles.AnyAsync(r => r.NormalizedName == normalizedName))
      {
        continue;
      }

      context.Roles.Add(new IdentityRole<Guid>
      {
        Id = Guid.NewGuid(),
        Name = roleName,
        NormalizedName = normalizedName,
        ConcurrencyStamp = Guid.NewGuid().ToString(),
      });
    }

    await context.SaveChangesAsync();
  }

  private static User CreateDemoUser(Guid id, string email, string displayName)
  {
    var user = new User(email, displayName)
    {
      Id = id,
      EmailConfirmed = true,
      // Konta demo omijają rejestrację, więc ustawiamy też telefon jako potwierdzony —
      // inaczej bramka logowania (PhoneNumberConfirmed) odrzuca każde logowanie demo.
      PhoneNumber = "+48500000000",
      PhoneNumberConfirmed = true,
      NormalizedEmail = email.ToUpperInvariant(),
      NormalizedUserName = email.ToUpperInvariant(),
      SecurityStamp = Guid.NewGuid().ToString(),
      ConcurrencyStamp = Guid.NewGuid().ToString(),
    };

    user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Password123!");
    return user;
  }

  /// <summary>
  /// Uzupełnia urlopy w salonie A, jeśli baza powstała przed dodaniem seedu urlopów.
  /// Używa IgnoreQueryFilters — przy starcie brak kontekstu HTTP / tenanta z JWT.
  /// </summary>
  private static async Task SeedSalonAEmployeeLeavesIfMissingAsync(ApplicationDbContext context)
  {
    var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    await SeedEmployeeLeaveIfMissingAsync(
      context,
      SalonATenantId,
      "owner-a@salon-a.local",
      today.AddDays(SalonALeaveStartOffset),
      today.AddDays(SalonALeaveEndOffset));
  }

  private static async Task SeedEmployeeLeaveIfMissingAsync(
    ApplicationDbContext context,
    Guid tenantId,
    string email,
    DateOnly start,
    DateOnly end)
  {
    var empId = await context.Employees
      .IgnoreQueryFilters()
      .Where(e => e.TenantId == tenantId && e.Email == email)
      .Select(e => e.Id)
      .FirstOrDefaultAsync();

    if (empId == default)
    {
      return;
    }

    var newId = Guid.NewGuid();
    var rows = await context.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO "EmployeeLeaves" ("Id", "EmployeeId", "StartDate", "EndDate")
      SELECT {newId}, {empId}, {start}, {end}
      WHERE NOT EXISTS (
        SELECT 1 FROM "EmployeeLeaves" AS el WHERE el."EmployeeId" = {empId}
      );
      """);

    if (rows == 0)
    {
      return;
    }
  }

  private static List<ScheduleDay> BuildSeededScheduleDays(Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>> workWeek)
  {
    return workWeek
      .Select(kv => new ScheduleDay(
        new[] { new TimeRange(kv.Value.First().StartTime, kv.Value.First().EndTime) },
        cycleIndex: (int)kv.Key))
      .ToList();
  }

}
