using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Appointments;
using App.Application.Common;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Appointments.Commands.SwapAppointments;

/// <summary>
/// Zamienia miejscami terminy dwóch wizyt: każda przejmuje datę i godzinę rozpoczęcia drugiej,
/// zachowując własnego pracownika. Przy równej długości to czysta zamiana. Przy różnej długości
/// — jeśli <paramref name="HarmonizeToShorter"/> = true — dłuższa wizyta przyjmuje usługę
/// krótszej (skraca się), aby obie się zmieściły; w przeciwnym razie zamiana uda się tylko, gdy
/// dłuższa nadal mieści się w docelowym slocie.
/// </summary>
public record SwapAppointmentsCommand(
  Guid FirstAppointmentId,
  Guid SecondAppointmentId,
  bool HarmonizeToShorter = false) : IRequest<SwapAppointmentsResult>, IAppointmentWriteRequest;

public record SwapAppointmentsResult(Guid FirstAppointmentId, Guid SecondAppointmentId);

public class SwapAppointmentsHandler : TenantHandler<SwapAppointmentsCommand, SwapAppointmentsResult>
{
  private readonly IAppointmentRepository _repository;
  private readonly IStaffAccessPolicy _access;
  private readonly IEmployeeRepository _employeeRepository;
  private readonly IServiceRepository _serviceRepository;
  private readonly ITenantRepository _tenantRepository;
  private readonly IUnitOfWork _uow;
  private readonly IAppointmentService _appointmentService;
  private readonly IPublisher _publisher;
  private readonly ILogger<SwapAppointmentsHandler> _logger;

  public SwapAppointmentsHandler(
    IAppointmentRepository repository,
    IEmployeeRepository employeeRepository,
    IServiceRepository serviceRepository,
    ITenantRepository tenantRepository,
    IUnitOfWork uow,
    IStaffAccessPolicy access,
    ICurrentTenantService currentTenantService,
    IAppointmentService appointmentService,
    IPublisher publisher,
    ILogger<SwapAppointmentsHandler> logger)
      : base(currentTenantService)
  {
    _access = access;
    _repository = repository;
    _employeeRepository = employeeRepository;
    _serviceRepository = serviceRepository;
    _tenantRepository = tenantRepository;
    _uow = uow;
    _appointmentService = appointmentService;
    _publisher = publisher;
    _logger = logger;
  }

  public override async Task<SwapAppointmentsResult> Handle(SwapAppointmentsCommand request, CancellationToken ct)
  {
    var tenant = await _tenantRepository.GetByIdAsync(TenantId)
      ?? throw new NotFoundException(nameof(Tenant), TenantId);

    var first = await _repository.GetByIdAsync(request.FirstAppointmentId)
      ?? throw new NotFoundException(nameof(Appointment), request.FirstAppointmentId);
    var second = await _repository.GetByIdAsync(request.SecondAppointmentId)
      ?? throw new NotFoundException(nameof(Appointment), request.SecondAppointmentId);

    // Zamiana rusza OBA kalendarze, więc oba muszą być dozwolone (jak dwa wywołania w kontrolerze).
    await _access.EnsureCanMutateEmployeeCalendarAsync(first.EmployeeId, ct);
    await _access.EnsureCanMutateEmployeeCalendarAsync(second.EmployeeId, ct);

    var firstEmployee = await _employeeRepository.GetByIdAsync(first.EmployeeId)
      ?? throw new NotFoundException(nameof(Employee), first.EmployeeId);
    var secondEmployee = await _employeeRepository.GetByIdAsync(second.EmployeeId)
      ?? throw new NotFoundException(nameof(Employee), second.EmployeeId);

    var firstService = await _serviceRepository.GetByIdAsync(first.ServiceId)
      ?? throw new NotFoundException(nameof(Service), first.ServiceId);
    var secondService = await _serviceRepository.GetByIdAsync(second.ServiceId)
      ?? throw new NotFoundException(nameof(Service), second.ServiceId);

    if (first.TenantId != TenantId || second.TenantId != TenantId
        || firstEmployee.TenantId != TenantId || secondEmployee.TenantId != TenantId
        || firstService.TenantId != TenantId || secondService.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    EnsureSwappable(first);
    EnsureSwappable(second);

    var (firstTarget, secondTarget) = BuildTargets(
      request.HarmonizeToShorter,
      first, firstEmployee, firstService,
      second, secondEmployee, secondService);

    AppointmentScheduleGuard.EnsureStartNotInPast(tenant.TimeZoneId, firstTarget.Date, firstTarget.StartTime);
    AppointmentScheduleGuard.EnsureStartNotInPast(tenant.TimeZoneId, secondTarget.Date, secondTarget.StartTime);

    if (!await AppointmentSwapPlanner.FitsAsync(_appointmentService, TenantId, firstTarget, secondTarget))
    {
      throw new AppointmentSlotUnavailableException();
    }

    var firstOldDate = first.Date;
    var firstOldStart = first.StartTime;
    var secondOldDate = second.Date;
    var secondOldStart = second.StartTime;

    void ApplyFirstTarget() => first.Update(firstEmployee.Id, firstTarget.ServiceId, firstTarget.Date, firstTarget.StartTime, firstTarget.EndTime, firstTarget.Price);
    void ApplySecondTarget() => second.Update(secondEmployee.Id, secondTarget.ServiceId, secondTarget.Date, secondTarget.StartTime, secondTarget.EndTime, secondTarget.Price);

    if (firstEmployee.Id != secondEmployee.Id)
    {
      // Różni pracownicy → różne klucze UNIQUE(employee, date, start), brak kolizji w stanie pośrednim.
      ApplyFirstTarget();
      ApplySecondTarget();
      await _uow.SaveChangesAsync(ct);
    }
    else
    {
      // Ten sam pracownik: UNIQUE(employee, date, start) jest non-deferrable, więc bezpośrednia
      // zamiana w jednym SaveChanges narusza indeks w stanie pośrednim (jedna z wizyt na chwilę
      // dubluje slot drugiej). „Parkujemy" pierwszą wizytę na wolnym slocie, sadzamy drugą na
      // zwolniony slot, a na końcu pierwszą na docelowy — atomowo w transakcji.
      var parkStart = await FindFreeStartTimeAsync(firstEmployee.Id, firstOldDate, ct);
      await _uow.ExecuteInTransactionAsync(async token =>
      {
        // Świeża instancja Money (nie współdzielimy owned entity między zapisami/właścicielami).
        var parkPrice = new Money(first.TotalPrice.Amount, first.TotalPrice.Currency);
        first.Update(firstEmployee.Id, first.ServiceId, firstOldDate, parkStart, parkStart.AddMinutes(1), parkPrice);
        await _uow.SaveChangesAsync(token);
        ApplySecondTarget();
        await _uow.SaveChangesAsync(token);
        ApplyFirstTarget();
        await _uow.SaveChangesAsync(token);
      }, ct);
    }

    await PublishRescheduledAsync(first.TenantId, first.Id, firstOldDate, firstOldStart, ct);
    await PublishRescheduledAsync(second.TenantId, second.Id, secondOldDate, secondOldStart, ct);

    return new SwapAppointmentsResult(first.Id, second.Id);
  }

  private static void EnsureSwappable(Appointment appointment)
  {
    if (appointment.Status == AppointmentStatus.Completed || appointment.Status == AppointmentStatus.Canceled)
    {
      throw new AppointmentBookingRuleException(
        "Nie można zamienić terminu wizyty, która została już zakończona lub anulowana.",
        ErrorCodes.AppointmentSwapTerminalStatus);
    }

    // Zamiana zakłada single-service (przenosi usługę/harmonizuje). Dla wizyt-combo (wiele usług)
    // semantyka harmonizacji jest niezdefiniowana — blokujemy, zostawiając reschedule jako ścieżkę.
    if (appointment.Items.Count > 1)
    {
      throw new AppointmentBookingRuleException(
        "Zamiana terminów nie jest dostępna dla wizyt z wieloma usługami.",
        ErrorCodes.AppointmentSwapMultiServiceUnsupported);
    }
  }

  private static (AppointmentSwapPlanner.SwapTarget First, AppointmentSwapPlanner.SwapTarget Second) BuildTargets(
    bool harmonizeToShorter,
    Appointment first, Employee firstEmployee, Service firstService,
    Appointment second, Employee secondEmployee, Service secondService)
  {
    // Domyślnie każda wizyta zachowuje swoją usługę i przenosi się na slot drugiej.
    var serviceForFirst = firstService;
    var serviceForSecond = secondService;

    if (harmonizeToShorter)
    {
      // Dłuższa wizyta przyjmuje usługę krótszej (skraca się), aby obie się zmieściły.
      var plan = AppointmentSwapPlanner.PlanHarmonization(first, firstService, second, secondService);
      if (plan is not null)
      {
        if (plan.LongerAppointment.Id == first.Id)
        {
          serviceForFirst = plan.ShorterService;
        }
        else
        {
          serviceForSecond = plan.ShorterService;
        }
      }
    }

    try
    {
      var firstTarget = AppointmentSwapPlanner.BuildTarget(first, firstEmployee, serviceForFirst, second.Date, second.StartTime);
      var secondTarget = AppointmentSwapPlanner.BuildTarget(second, secondEmployee, serviceForSecond, first.Date, first.StartTime);
      return (firstTarget, secondTarget);
    }
    catch (EmployeeServiceMissingException)
    {
      // Harmonizacja wymaga, by pracownik dłuższej wizyty oferował usługę krótszej — nie oferuje.
      throw new AppointmentBookingRuleException(
        "Nie można skrócić wizyty: pracownik nie ma przypisanej krótszej usługi.",
        ErrorCodes.AppointmentSwapHarmonizationUnavailable);
    }
  }

  /// <summary>
  /// Najwcześniejszy slot (StartTime) wolny dla pracownika w danym dniu — miejsce na chwilowe
  /// „zaparkowanie" pierwszej wizyty podczas zamiany przy tym samym pracowniku.
  /// </summary>
  private async Task<TimeOnly> FindFreeStartTimeAsync(Guid employeeId, DateOnly date, CancellationToken ct)
  {
    var existing = await _repository.GetAppointmentsByDateAsync(employeeId, date, TenantId);
    var occupied = existing.Select(a => a.StartTime).ToHashSet();
    var candidate = new TimeOnly(0, 0);
    var lastUsable = new TimeOnly(23, 58); // +1 min musi dać poprawny zakres (< 23:59)
    while (occupied.Contains(candidate) && candidate < lastUsable)
    {
      candidate = candidate.AddMinutes(1);
    }
    return candidate;
  }

  private async Task PublishRescheduledAsync(Guid tenantId, Guid appointmentId, DateOnly oldDate, TimeOnly oldStart, CancellationToken ct)
  {
    try
    {
      await _publisher.Publish(
        new AppointmentRescheduledBySalonEvent(tenantId, appointmentId, oldDate, oldStart),
        ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Publikacja AppointmentRescheduledBySalonEvent dla wizyty {Id} po zamianie nie powiodła się", appointmentId);
    }
  }
}
