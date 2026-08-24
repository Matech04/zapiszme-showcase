using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;

namespace App.Infrastructure.Persistence;

/// <summary>
/// Wypełnia świeżo utworzonego demo-tenanta realistycznym zestawem danych salonu
/// kosmetycznego SOLO (jedna stylistka = właściciel): usługi, klientki i wizyty rozłożone
/// wokół „dziś", tak by Pulpit, Kalendarz i Klienci były „żywe" od pierwszego wejścia.
///
/// Insert-only: dodaje encje do przekazanego <see cref="ApplicationDbContext"/>, ale ich nie
/// zapisuje — zapis i transakcję trzyma wywołujący (DemoController), tak jak robi to
/// register-owner. Nie wykonuje odczytów (globalne query-filtry przy braku rozwiązanego
/// tenanta odcięłyby wszystko).
/// </summary>
public interface IDemoDataSeeder
{
  /// <summary>
  /// Dosiewa dane domenowe dla demo-tenanta. <paramref name="owner"/> to już utworzony
  /// (powiązany z userem Identity) pracownik-właściciel — seeder dodaje mu grafik,
  /// przypisuje usługi i tworzy wizyty.
  /// </summary>
  void Seed(ApplicationDbContext context, Tenant tenant, Employee owner, DateTime nowUtc);
}

public sealed class DemoDataSeeder : IDemoDataSeeder
{
  private sealed record ServiceSpec(string Name, decimal Price, int DurationMinutes);

  // Katalog usług typowego mikro-studia kosmetycznego (paznokcie / brwi / rzęsy).
  private static readonly ServiceSpec[] ServiceCatalog =
  [
    new("Manicure hybrydowy", 120m, 60),
    new("Uzupełnienie żelowe", 160m, 90),
    new("Pedicure kosmetyczny", 150m, 75),
    new("Stylizacja brwi (henna pudrowa)", 80m, 45),
    new("Laminacja rzęs", 140m, 75),
  ];

  // Klientki demo — realistyczne polskie dane (telefony w bezpiecznym zakresie +48 50x).
  private static readonly (string First, string Last)[] CustomerNames =
  [
    ("Anna", "Kowalska"), ("Magdalena", "Nowak"), ("Katarzyna", "Wiśniewska"),
    ("Agnieszka", "Wójcik"), ("Joanna", "Kowalczyk"), ("Natalia", "Kamińska"),
    ("Aleksandra", "Lewandowska"), ("Patrycja", "Zielińska"), ("Karolina", "Szymańska"),
    ("Monika", "Woźniak"), ("Paulina", "Dąbrowska"), ("Sylwia", "Kozłowska"),
    ("Ewa", "Jankowska"), ("Marta", "Mazur"), ("Dominika", "Kwiatkowska"),
  ];

  public void Seed(ApplicationDbContext context, Tenant tenant, Employee owner, DateTime nowUtc)
  {
    var currency = tenant.Currency;

    // 1. VAT + kategoria
    var vat = new VatRate(tenant.Id, "23%", 0.23m, true);
    var category = new ServiceCategory(tenant.Id, "Usługi", 1);
    context.Set<VatRate>().Add(vat);
    context.Set<ServiceCategory>().Add(category);

    // 2. Usługi
    var services = ServiceCatalog
      .Select(spec => new Service(
        tenant.Id, category.Id, vat.Id, spec.Name, new Money(spec.Price, currency), spec.DurationMinutes))
      .ToArray();
    context.Services.AddRange(services);

    // 3. Grafik właściciela (pon–sob 9:00–17:00) + przypisanie wszystkich usług.
    var workWeek = new[]
    {
      DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
      DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
    };
    var scheduleDays = workWeek
      .Select(day => new ScheduleDay(
        new[] { new TimeRange(new TimeOnly(9, 0), new TimeOnly(17, 0)) },
        cycleIndex: (int)day))
      .ToList();

    var today = LocalToday(tenant, nowUtc);
    owner.AddSchedule(new DateRange(today, DateOnly.MaxValue), 1, scheduleDays);

    foreach (var (service, spec) in services.Zip(ServiceCatalog))
    {
      owner.AssignService(tenant.Id, service.Id, spec.DurationMinutes, new Money(spec.Price, currency));
    }

    // 4. Klientki
    var customers = CustomerNames
      .Select((c, i) => new Customer(
        tenant.Id,
        c.First,
        c.Last,
        $"{Slugify(c.First)}.{Slugify(c.Last)}@demo.local",
        // +48 500 100 1NN — poprawny polski numer komórkowy, unikalny w obrębie demo.
        new PhoneNumber($"+48{500_100_100 + i}"),
        string.Empty))
      .ToArray();
    context.Customers.AddRange(customers);

    // 5. Wizyty rozłożone wokół „dziś" (przeszłe Completed, dzisiejsze Booked/InProgress,
    //    przyszłe Booked). Sloty dobrane tak, by nie kolidowały w obrębie dnia.
    var nowLocal = LocalNow(tenant, nowUtc);
    var appointments = BuildAppointments(tenant, owner, services, customers, today, nowLocal, currency);
    context.Appointments.AddRange(appointments);
  }

  private static List<Appointment> BuildAppointments(
    Tenant tenant,
    Employee owner,
    Service[] services,
    Customer[] customers,
    DateOnly today,
    DateTimeOffset nowLocal,
    string currency)
  {
    // (dayOffset, startHour, serviceIndex). Godziny dobrane bez nakładania w jednym dniu.
    (int DayOffset, int Hour, int ServiceIndex)[] plan =
    [
      // przeszłość — zakończone wizyty (budują historię klientek + statystyki)
      (-6, 10, 0), (-6, 13, 3),
      (-4, 9, 1), (-4, 14, 4),
      (-2, 11, 2), (-2, 15, 0),
      (-1, 10, 3), (-1, 12, 1),
      // dziś — część może być „w trakcie", reszta zaplanowana
      (0, 9, 0), (0, 11, 4), (0, 14, 2), (0, 16, 3),
      // przyszłość — nadchodzące rezerwacje
      (1, 10, 1), (1, 13, 0),
      (2, 9, 4), (2, 12, 2),
      (4, 11, 0), (5, 15, 3),
      (7, 10, 1), (8, 14, 4),
    ];

    var result = new List<Appointment>();
    var customerCursor = 0;

    foreach (var (dayOffset, hour, serviceIndex) in plan)
    {
      var date = NextWorkingDay(today.AddDays(dayOffset));
      var service = services[serviceIndex];
      var customer = customers[customerCursor % customers.Length];
      customerCursor++;

      var start = new TimeOnly(hour, 0);
      var end = start.AddMinutes(service.DurationInMinutes);
      if (end <= start || end > new TimeOnly(17, 0))
      {
        continue; // poza godzinami pracy — pomiń (defensywnie)
      }

      var status = ResolveStatus(date, start, end, today, nowLocal);

      result.Add(new Appointment(
        tenant.Id,
        owner.Id,
        service.Id,
        customer.Id,
        date,
        start,
        end,
        status,
        new Money(service.Price.Amount, currency),
        appointmentNotes: string.Empty,
        lease: null,
        source: AppointmentSource.Panel));
    }

    return result;
  }

  private static AppointmentStatus ResolveStatus(
    DateOnly date, TimeOnly start, TimeOnly end, DateOnly today, DateTimeOffset nowLocal)
  {
    if (date < today)
    {
      return AppointmentStatus.Completed;
    }

    if (date > today)
    {
      return AppointmentStatus.Booked;
    }

    // dziś — zależnie od pory dnia
    var nowTime = TimeOnly.FromDateTime(nowLocal.DateTime);
    if (end <= nowTime)
    {
      return AppointmentStatus.Completed;
    }
    if (start <= nowTime && nowTime < end)
    {
      return AppointmentStatus.InProgress;
    }
    return AppointmentStatus.Booked;
  }

  /// <summary>Przesuwa datę do najbliższego dnia roboczego (salon SOLO nie pracuje w niedziele).</summary>
  private static DateOnly NextWorkingDay(DateOnly date) =>
    date.DayOfWeek == DayOfWeek.Sunday ? date.AddDays(1) : date;

  private static DateOnly LocalToday(Tenant tenant, DateTime nowUtc) =>
    DateOnly.FromDateTime(LocalNow(tenant, nowUtc).DateTime);

  private static DateTimeOffset LocalNow(Tenant tenant, DateTime nowUtc)
  {
    var tz = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZoneId);
    return TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)), tz);
  }

  private static string Slugify(string value) =>
    value.ToLowerInvariant()
      .Replace("ą", "a").Replace("ć", "c").Replace("ę", "e").Replace("ł", "l")
      .Replace("ń", "n").Replace("ó", "o").Replace("ś", "s").Replace("ź", "z").Replace("ż", "z");
}
