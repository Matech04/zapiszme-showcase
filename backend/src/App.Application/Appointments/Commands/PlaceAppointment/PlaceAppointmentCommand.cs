// Wspólny rdzeń tworzenia wizyty: reguły domenowe (grafik, kolizje, ceny, zdarzenia).
// NIE autoryzuje — ma DWÓCH wywołujących o różnych regułach dostępu:
//   * personel  → CreateAppointmentCommand (osłona z IStaffAccessPolicy),
//   * publiczny booking → CreateBookingAppointmentCommand (slug→tenant, hold lease, OTP, rate-limit).
// Rozdzielenie wyszło przy przenoszeniu autoryzacji z kontrolera: strażnik w tym handlerze
// odrzucał anonimowe rezerwacje, bo klientka nie ma powiązanego pracownika.
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Application.Common;
using App.Application.Notifications.Events;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Appointments.Commands.PlaceAppointment;

/// <summary>
/// Wspólny rdzeń tworzenia wizyty. `public` wyłącznie dlatego, że FluentValidation skanuje typy
/// publiczne — NIE jest to zaproszenie do wołania z zewnątrz. Personel wchodzi przez
/// `CreateAppointmentCommand` (autoryzacja), publiczny booking przez `CreateBookingAppointmentCommand`.
/// </summary>
public record PlaceAppointmentCommand(
  Guid EmployeeId,
  IReadOnlyList<Guid> ServiceIds,
  DateOnly Date,
  TimeOnly StartTime,
  Guid? CustomerId,
  string? CustomerPhone,
  bool CreateAsBooked = false,
  bool CreateAsAwaitingOtp = false,
  bool CreateAsCompleted = false,
  HoldLease? HoldLease = null,
  AppointmentSource Source = AppointmentSource.Panel,
  // Zapis „poza grafikiem": personel świadomie pomija godziny pracy/urlop — blokuje tylko
  // kolizja z inną wizytą. Honorowane wyłącznie dla Source = Panel (patrz handler).
  bool IgnoreSchedule = false,
  // Zdjęcia inspiracji (≤3) dołączone przez klientkę w publicznym bookingu — już przetworzone
  // i wgrane przez anonimowy endpoint uploadu. Cap egzekwuje agregat (SetInspirationImages).
  IReadOnlyList<AppointmentInspirationLine>? InspirationImages = null,
  // Niestandardowy czas trwania wizyty (minuty) ustawiony przez personel — nadpisuje długość bloku
  // w kalendarzu. Null = czas standardowy (suma czasów usług). Rezerwacja online tego nie podaje.
  int? CustomDurationMinutes = null) : IRequest<Guid>, IAppointmentWriteRequest;

internal class PlaceAppointmentHandler : TenantHandler<PlaceAppointmentCommand, Guid>
{
  private readonly IAppointmentRepository _repository;
  private readonly IEmployeeRepository _employeeRepository;
  private readonly IServiceRepository _serviceRepository;
  private readonly ICustomerRepository _customerRepository;
  private readonly ITenantRepository _tenantRepository;
  private readonly IUnitOfWork _uow;
  private readonly IAppointmentService _appointmentService;
  private readonly IPublisher _publisher;

  public PlaceAppointmentHandler(
    IAppointmentRepository repository,
    IEmployeeRepository employeeRepository,
    IServiceRepository serviceRepository,
    ICustomerRepository customerRepository,
    ITenantRepository tenantRepository,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService,
    IAppointmentService appointmentService,
    IPublisher publisher
    )
      : base(currentTenantService)
  {
    _repository = repository;
    _employeeRepository = employeeRepository;
    _serviceRepository = serviceRepository;
    _customerRepository = customerRepository;
    _tenantRepository = tenantRepository;
    _uow = uow;
    _appointmentService = appointmentService;
    _publisher = publisher;
  }
  public override async Task<Guid> Handle(PlaceAppointmentCommand request, CancellationToken ct)
  {
    var tenant = await _tenantRepository.GetByIdAsync(TenantId)
      ?? throw new NotFoundException(nameof(Tenant), TenantId);

    var employee = await _employeeRepository.GetByIdAsync(request.EmployeeId)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    // Wczytaj usługi combo zachowując kolejność (pierwsza = główna).
    var services = new List<Service>(request.ServiceIds.Count);
    foreach (var serviceId in request.ServiceIds)
    {
      var svc = await _serviceRepository.GetByIdAsync(serviceId)
          ?? throw new NotFoundException(nameof(Service), serviceId);
      if (svc.TenantId != TenantId)
      {
        throw new TenantViolation();
      }
      services.Add(svc);
    }

    // Tenant Safety Check
    if (employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    // Reguła składu combo (m.in. zakaz dwóch usług z tej samej grupy wariantów).
    ComboCompositionValidator.EnsureValidComposition(services);

    if (request.CreateAsCompleted)
    {
      AppointmentScheduleGuard.EnsureStartInPast(tenant.TimeZoneId, request.Date, request.StartTime);
    }
    else
    {
      AppointmentScheduleGuard.EnsureStartNotInPast(tenant.TimeZoneId, request.Date, request.StartTime);
    }

    var status = request.CreateAsCompleted
      ? AppointmentStatus.Completed
      : request.CreateAsBooked
        ? AppointmentStatus.Booked
        : request.CreateAsAwaitingOtp
          ? AppointmentStatus.AwaitingOtp
          : AppointmentStatus.Pending;
    var customerId = await ResolveCustomerId(request.CustomerId, request.CustomerPhone, ct);

    // Pozycje combo: czas i cena per usługa rozwiązane wobec pracownika; czas wizyty = suma
    // (albo niestandardowy czas ustawiony przez personel — nadpisuje długość bloku).
    var lines = AppointmentComboBuilder.BuildLines(employee, services);
    var standardDuration = AppointmentComboBuilder.TotalDurationMinutes(lines);
    var effectiveDuration = request.CustomDurationMinutes ?? standardDuration;
    TimeOnly endTime = request.StartTime.AddMinutes(effectiveDuration);

    // „Poza grafikiem" honorujemy tylko dla zapisu z panelu (personel). Online nie może go podać,
    // nawet gdyby wstrzyknął flagę w body — broni przed pominięciem godzin pracy w publicznym flow.
    var ignoreSchedule = request.IgnoreSchedule && request.Source == AppointmentSource.Panel;

    // Validating availability and collisions
    var isAvailable = await _appointmentService.IsAvailableAsync(employee, request.StartTime, endTime, request.Date, TenantId, ignoreSchedule: ignoreSchedule);

    if (!isAvailable)
    {
      throw new AppointmentSlotUnavailableException();
    }

    // Widoczność terminu dla KLIENTA: horyzont rezerwacji salonu + publikacja miesiąca.
    // Reguła świadomie NIE żyje w IsAvailable/GetWorkingRanges (te obsługują też panel), więc każda
    // ścieżka klienta musi zawołać ją jawnie — tu i w ApplyRescheduleCommand. Bez tego sprawdzenia
    // zapytania o sloty poprawnie ukrywały zamknięty miesiąc, ale POST /hold i tak tworzył w nim wizytę:
    // panel deklarował „zamknięte", a rezerwacje wchodziły.
    if (request.Source == AppointmentSource.Online
        && !employee.IsDateOpenForOnlineBooking(
              request.Date,
              AppointmentScheduleGuard.TodayInBusinessTimeZone(tenant.TimeZoneId),
              tenant.BookingHorizonDays))
    {
      throw new AppointmentSlotUnavailableException();
    }

    // Tryb stałych slotów: rezerwacja ONLINE może trafić wyłącznie w zdefiniowane godziny.
    // Panel (personel) bez ograniczeń — to jego kalendarz, może wpisać dowolną godzinę.
    if (request.Source == AppointmentSource.Online
        && employee.IsFixedForDate(request.Date)
        && !employee.GetFixedStartTimes(request.Date).Contains(request.StartTime))
    {
      throw new AppointmentSlotUnavailableException();
    }

    var appointment = new Appointment(
        TenantId,
        request.EmployeeId,
        customerId,
        request.Date,
        request.StartTime,
        status,
        "",
        request.HoldLease,
        lines,
        request.Source,
        request.CustomDurationMinutes);

    // Zdjęcia inspiracji (publiczny booking): cap egzekwuje agregat. Brak = no-op.
    if (request.InspirationImages is { Count: > 0 })
    {
      appointment.SetInspirationImages(request.InspirationImages);
    }

    await _repository.AddAsync(appointment);
    try
    {
      await _uow.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (IsUniqueSlotViolation(ex))
    {
      // Wyścig z partial-unique-index (EmployeeId, Date, StartTime WHERE Status<>Canceled):
      // slot zajął właśnie ktoś inny, ALBO blokuje go nieposprzątany wygasły hold (HasCollisionAsync
      // celowo ignoruje wygasłe holdy, lecz indeks już nie — porzucony hold znika dopiero w jobie
      // cyklu życia, ~1 min). Zamień generyczny błąd zapisu (persistence.failed) na ten sam
      // semantyczny wyjątek co zwykła kolizja → errorCode appointment.slot_unavailable, na który
      // front reaguje „termin zajęty, zmień termin". Inne DbUpdateException propagują dalej.
      throw new AppointmentSlotUnavailableException();
    }

    // Pracownik ręcznie wystawił klientowi wizytę w panelu (Panel) i od razu jest Booked
    // — wyślij klientowi potwierdzenie (gated po NotificationSettings w handlerze zdarzenia).
    // Pomijamy wizyty z przeszłości (Completed) i rezerwacje online (osobny flow z OTP).
    if (request.Source == AppointmentSource.Panel && status == AppointmentStatus.Booked)
    {
      await _publisher.Publish(new StaffBookedAppointmentEvent(TenantId, appointment.Id), ct);
    }

    return appointment.Id;
  }

  // Npgsql PostgresException.SqlState "23505" = unique_violation. Sprawdzamy bez zależności od
  // Npgsql w warstwie Application — po property SqlState (refleksja) w łańcuchu inner-exceptions.
  private static bool IsUniqueSlotViolation(DbUpdateException ex)
  {
    for (Exception? e = ex; e is not null; e = e.InnerException)
    {
      if (e.GetType().GetProperty("SqlState")?.GetValue(e) as string == "23505")
      {
        return true;
      }
    }
    return false;
  }

  private async Task<Guid?> ResolveCustomerId(Guid? customerId, string? customerPhone, CancellationToken ct)
  {
    if (customerId.HasValue)
    {
      var customer = await _customerRepository.GetByIdAsync(customerId.Value);
      if (customer == null)
      {
        throw new NotFoundException(nameof(Customer), customerId.Value);
      }
      if (customer.TenantId != TenantId)
      {
        throw new TenantViolation();
      }
      return customerId.Value;
    }

    if (string.IsNullOrWhiteSpace(customerPhone))
    {
      return null;
    }

    // Sam numer telefonu (szybki zapis gościa z panelu). Parsujemy do E.164 i — analogicznie do
    // publicznego flow (VerifyOtpCommand) — wiążemy z istniejącym klientem po numerze; gdy takiego
    // nie ma, zakładamy klienta-szkielet (Source = Manual, bez imienia/nazwiska). Dzięki temu numer
    // nie ginie, a przypomnienia SMS mają adresata. Walidator gwarantuje poprawność numeru.
    var phone = new PhoneNumber(customerPhone);
    var existing = await _customerRepository.GetByPhoneNumber(TenantId, phone, ct);
    if (existing is not null)
    {
      return existing.Id;
    }

    var created = Customer.CreateQuickFromPanel(TenantId, phone);
    await _customerRepository.AddAsync(created);
    return created.Id;
  }
}