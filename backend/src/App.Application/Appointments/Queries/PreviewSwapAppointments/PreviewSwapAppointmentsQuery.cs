using App.Application.Appointments;
using App.Application.Common;
using App.Application.Common.Security;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Appointments.Queries.PreviewSwapAppointments;

/// <summary>
/// Podgląd zamiany dwóch wizyt — bez zapisu. Mówi UI, czy zamiana „jak jest" się mieści,
/// a jeśli nie (różne długości), czy możliwe jest skrócenie dłuższej wizyty do usługi krótszej
/// i czy po skróceniu obie się zmieszczą. Na tej podstawie panel decyduje, czy poprosić
/// użytkownika o potwierdzenie skrócenia.
/// </summary>
public record PreviewSwapAppointmentsQuery(Guid FirstAppointmentId, Guid SecondAppointmentId)
  : IRequest<SwapPreviewDto>;

public record SwapPreviewDto(
  bool EqualDuration,
  bool PlainSwapFits,
  bool HarmonizationAvailable,
  bool HarmonizedSwapFits,
  Guid? ServiceChangeAppointmentId,
  string? FromServiceName,
  string? ToServiceName,
  decimal? OldPrice,
  decimal? NewPrice);

public class PreviewSwapAppointmentsHandler : TenantHandler<PreviewSwapAppointmentsQuery, SwapPreviewDto>
{
  private readonly IAppointmentRepository _repository;
  private readonly IStaffAccessPolicy _access;
  private readonly IEmployeeRepository _employeeRepository;
  private readonly IServiceRepository _serviceRepository;
  private readonly IAppointmentService _appointmentService;

  public PreviewSwapAppointmentsHandler(
    IAppointmentRepository repository,
    IEmployeeRepository employeeRepository,
    IServiceRepository serviceRepository,
    IStaffAccessPolicy access,
    ICurrentTenantService currentTenantService,
    IAppointmentService appointmentService)
      : base(currentTenantService)
  {
    _access = access;
    _repository = repository;
    _employeeRepository = employeeRepository;
    _serviceRepository = serviceRepository;
    _appointmentService = appointmentService;
  }

  public override async Task<SwapPreviewDto> Handle(PreviewSwapAppointmentsQuery request, CancellationToken ct)
  {
    var first = await _repository.GetByIdAsync(request.FirstAppointmentId)
      ?? throw new NotFoundException(nameof(Appointment), request.FirstAppointmentId);
    var second = await _repository.GetByIdAsync(request.SecondAppointmentId)
      ?? throw new NotFoundException(nameof(Appointment), request.SecondAppointmentId);

    // Podgląd zamiany ujawnia szczegóły obu wizyt — ta sama bramka co przy samej zamianie.
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

    var plainFirst = AppointmentSwapPlanner.BuildTarget(first, firstEmployee, firstService, second.Date, second.StartTime);
    var plainSecond = AppointmentSwapPlanner.BuildTarget(second, secondEmployee, secondService, first.Date, first.StartTime);
    var plainFits = await AppointmentSwapPlanner.FitsAsync(_appointmentService, TenantId, plainFirst, plainSecond);

    var plan = AppointmentSwapPlanner.PlanHarmonization(first, firstService, second, secondService);
    var equalDuration = plan is null;

    if (plan is null)
    {
      return new SwapPreviewDto(EqualDuration: true, plainFits, false, false, null, null, null, null, null);
    }

    var longerOnFirst = plan.LongerAppointment.Id == first.Id;
    try
    {
      var harmonizedFirst = longerOnFirst
        ? AppointmentSwapPlanner.BuildTarget(first, firstEmployee, plan.ShorterService, second.Date, second.StartTime)
        : plainFirst;
      var harmonizedSecond = longerOnFirst
        ? plainSecond
        : AppointmentSwapPlanner.BuildTarget(second, secondEmployee, plan.ShorterService, first.Date, first.StartTime);

      var harmonizedFits = await AppointmentSwapPlanner.FitsAsync(_appointmentService, TenantId, harmonizedFirst, harmonizedSecond);
      var newPrice = (longerOnFirst ? harmonizedFirst : harmonizedSecond).Price.Amount;

      return new SwapPreviewDto(
        EqualDuration: false,
        plainFits,
        HarmonizationAvailable: true,
        harmonizedFits,
        ServiceChangeAppointmentId: plan.LongerAppointment.Id,
        FromServiceName: plan.LongerService.Name,
        ToServiceName: plan.ShorterService.Name,
        OldPrice: plan.LongerAppointment.TotalPrice.Amount,
        NewPrice: newPrice);
    }
    catch (EmployeeServiceMissingException)
    {
      // Pracownik dłuższej wizyty nie oferuje krótszej usługi — skrócenie niemożliwe.
      return new SwapPreviewDto(EqualDuration: false, plainFits, HarmonizationAvailable: false, false, null, null, null, null, null);
    }
  }
}
