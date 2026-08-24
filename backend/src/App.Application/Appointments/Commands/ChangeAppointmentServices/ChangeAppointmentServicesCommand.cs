using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Common;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Appointments.Commands.ChangeAppointmentServices;

/// <summary>
/// Zmienia skład usług istniejącej wizyty ZACHOWUJĄC pracownika, datę i godzinę rozpoczęcia.
/// W odróżnieniu od <c>RescheduleAppointmentCommand</c> nie przesuwa terminu (i nie publikuje
/// zdarzenia „termin zmieniony") — to korekta samego zabiegu w obrębie zarezerwowanego slotu.
/// EndTime/TotalPrice/ServiceId są przeliczane z nowego składu; gdy nowa (dłuższa) usługa nie
/// mieści się w bieżącym terminie (kolizja / poza godzinami pracy), rzucamy
/// <see cref="AppointmentSlotUnavailableException"/> — UI proponuje wtedy „Zmień termin".
/// </summary>
public record ChangeAppointmentServicesCommand(
  Guid AppointmentId,
  IReadOnlyList<Guid> ServiceIds) : IRequest<Guid>, IAppointmentWriteRequest;

public class ChangeAppointmentServicesHandler : TenantHandler<ChangeAppointmentServicesCommand, Guid>
{
  private readonly IAppointmentRepository _repository;
  private readonly IStaffAccessPolicy _access;
  private readonly IEmployeeRepository _employeeRepository;
  private readonly IServiceRepository _serviceRepository;
  private readonly IUnitOfWork _uow;
  private readonly IAppointmentService _appointmentService;

  public ChangeAppointmentServicesHandler(
    IAppointmentRepository repository,
    IEmployeeRepository employeeRepository,
    IServiceRepository serviceRepository,
    IUnitOfWork uow,
    IStaffAccessPolicy access,
    ICurrentTenantService currentTenantService,
    IAppointmentService appointmentService)
      : base(currentTenantService)
  {
    _access = access;
    _repository = repository;
    _employeeRepository = employeeRepository;
    _serviceRepository = serviceRepository;
    _uow = uow;
    _appointmentService = appointmentService;
  }

  public override async Task<Guid> Handle(ChangeAppointmentServicesCommand request, CancellationToken ct)
  {
    var appointment = await _repository.GetByIdAsync(request.AppointmentId)
        ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    // Wizytę mamy już w ręku — pytamy o jej pracownika, zamiast dociągać ją drugi raz.
    await _access.EnsureCanMutateEmployeeCalendarAsync(appointment.EmployeeId, ct);

    if (appointment.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    // Skład usług zmieniamy tylko dla wizyt aktywnych. Anulowana / zakończona = stan terminalny.
    if (appointment.Status == AppointmentStatus.Canceled || appointment.Status == AppointmentStatus.Completed)
    {
      throw new AppointmentBookingRuleException(
        "Nie można zmienić usługi w anulowanej lub zakończonej wizycie.",
        ErrorCodes.AppointmentServicesChangeInvalidStatus);
    }

    // Pracownik i termin pochodzą z istniejącej wizyty — nie zmieniamy ich tutaj.
    var employee = await _employeeRepository.GetByIdAsync(appointment.EmployeeId)
      ?? throw new NotFoundException(nameof(Employee), appointment.EmployeeId);

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

    ComboCompositionValidator.EnsureValidComposition(services);

    var lines = AppointmentComboBuilder.BuildLines(employee, services);
    var endTime = appointment.StartTime.AddMinutes(AppointmentComboBuilder.TotalDurationMinutes(lines));

    // Sprawdź, czy nowy (przeliczony) czas wizyty mieści się w slocie — ignorując samą tę wizytę.
    var isAvailable = await _appointmentService.IsAvailableAsync(
      employee, appointment.StartTime, endTime, appointment.Date, TenantId, appointment.Id);

    if (!isAvailable)
    {
      throw new AppointmentSlotUnavailableException();
    }

    appointment.SetServices(lines, appointment.StartTime);

    await _uow.SaveChangesAsync(ct);

    return appointment.Id;
  }
}
