using App.Application.Common.Interfaces;
using App.Application.Appointments;
using App.Application.Common;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Appointments.Commands.ApplyReschedule;

/// <summary>
/// <paramref name="IsSelfService"/> = true gdy zmiana pochodzi z panelu samoobsługi klienta —
/// wtedy NIE publikujemy zdarzenia powiadomień (robi to handler self-service). Domyślnie false
/// (zmiana z panelu salonu → powiadamiamy klienta).
/// </summary>
/// <summary>
/// Wspólny rdzeń przełożenia wizyty. `public` wyłącznie pod skanowanie FluentValidation.
/// Personel wchodzi przez `RescheduleAppointmentCommand` (autoryzacja); publiczny booking i
/// self-service klientki wołają ten rdzeń wprost — mają własne bramki (OTP, lease, slug→tenant).
/// </summary>
public record ApplyRescheduleCommand(
  Guid AppointmentId,
  Guid EmployeeId,
  IReadOnlyList<Guid> ServiceIds,
  DateOnly Date,
  TimeOnly StartTime,
  bool IsSelfService = false,
  // Zmiana terminu „poza grafikiem" z panelu — pomija godziny pracy/urlop, blokuje tylko kolizja.
  // Ścieżka self-service klienta tego nie ustawia (zostaje false).
  bool IgnoreSchedule = false,
  // Egzekwuje widoczność terminu dla KLIENTA (horyzont rezerwacji + publikacja miesiąca). Ustawiane
  // przez ścieżki klienta: self-service oraz PATCH holdu z publicznego bookingu. Panel przekłada
  // wizyty bez ograniczenia. Świadomie osobny znacznik, a nie IsSelfService — tamten steruje
  // wyłącznie publikacją powiadomienia i PATCH holdu ma go na false.
  bool EnforcePublicVisibility = false,
  // Niestandardowy czas trwania wizyty (minuty). Null = zachowaj bieżący override (samo przesunięcie
  // terminu nie kasuje niestandardowego bloku); wartość = ustaw nowy czas przy zmianie terminu.
  int? CustomDurationMinutes = null
  ) : IRequest<Guid>, IAppointmentWriteRequest;

internal class ApplyRescheduleHandler : TenantHandler<ApplyRescheduleCommand, Guid>
{
  private readonly IAppointmentRepository _repository;
  private readonly IEmployeeRepository _employeeRepository;
  private readonly IServiceRepository _serviceRepository;
  private readonly ITenantRepository _tenantRepository;
  private readonly IUnitOfWork _uow;
  private readonly IAppointmentService _appointmentService;
  private readonly IPublisher _publisher;
  private readonly ILogger<ApplyRescheduleHandler> _logger;

  public ApplyRescheduleHandler(
    IAppointmentRepository repository,
    IEmployeeRepository employeeRepository,
    IServiceRepository serviceRepository,
    ITenantRepository tenantRepository,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService,
    IAppointmentService appointmentService,
    IPublisher publisher,
    ILogger<ApplyRescheduleHandler> logger
    )
      : base(currentTenantService)
  {
    _repository = repository;
    _employeeRepository = employeeRepository;
    _serviceRepository = serviceRepository;
    _tenantRepository = tenantRepository;
    _uow = uow;
    _appointmentService = appointmentService;
    _publisher = publisher;
    _logger = logger;
  }
  public override async Task<Guid> Handle(ApplyRescheduleCommand request, CancellationToken ct)
  {
    var tenant = await _tenantRepository.GetByIdAsync(TenantId)
      ?? throw new NotFoundException(nameof(Tenant), TenantId);

    var appointment = await _repository.GetByIdAsync(request.AppointmentId)
        ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

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
    if (appointment.TenantId != TenantId || employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    ComboCompositionValidator.EnsureValidComposition(services);

    AppointmentScheduleGuard.EnsureStartNotInPast(tenant.TimeZoneId, request.Date, request.StartTime);

    // Widoczność terminu dla KLIENTA — patrz bliźniacze sprawdzenie w PlaceAppointmentCommand.
    // Bez tego łatka na tworzenie rezerwacji obchodzi się jednym PATCH-em: hold zakładany na
    // dozwolony dzień, a potem przesuwany do zamkniętego miesiąca.
    if (request.EnforcePublicVisibility
        && !employee.IsDateOpenForOnlineBooking(
              request.Date,
              AppointmentScheduleGuard.TodayInBusinessTimeZone(tenant.TimeZoneId),
              tenant.BookingHorizonDays))
    {
      throw new AppointmentSlotUnavailableException();
    }

    var lines = AppointmentComboBuilder.BuildLines(employee, services);
    var standardDuration = AppointmentComboBuilder.TotalDurationMinutes(lines);
    // Null = zachowaj istniejący override (przesunięcie terminu nie kasuje niestandardowego bloku).
    var effectiveDuration = request.CustomDurationMinutes ?? appointment.CustomDurationMinutes ?? standardDuration;
    TimeOnly endTime = request.StartTime.AddMinutes(effectiveDuration);

    // KLUCZOWE: Przekazujemy appointment.Id do zignorowania przy sprawdzaniu kolizji
    var isAvailable = await _appointmentService.IsAvailableAsync(employee, request.StartTime, endTime, request.Date, TenantId, appointment.Id, ignoreSchedule: request.IgnoreSchedule);

    if (!isAvailable)
    {
      throw new AppointmentSlotUnavailableException();
    }

    var oldDate = appointment.Date;
    var oldStartTime = appointment.StartTime;

    appointment.Reschedule(request.EmployeeId, request.Date, request.StartTime, lines, request.CustomDurationMinutes);

    await _uow.SaveChangesAsync(ct);

    // Zmiana z panelu salonu → powiadom klienta (best-effort). Ścieżka self-service publikuje
    // własne zdarzenie i ustawia IsSelfService = true, by uniknąć podwójnego powiadomienia.
    if (!request.IsSelfService)
    {
      try
      {
        await _publisher.Publish(
          new AppointmentRescheduledBySalonEvent(appointment.TenantId, appointment.Id, oldDate, oldStartTime),
          ct);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        _logger.LogWarning(ex, "Publikacja AppointmentRescheduledBySalonEvent dla wizyty {Id} nie powiodła się", appointment.Id);
      }
    }

    return appointment.Id;
  }
}
