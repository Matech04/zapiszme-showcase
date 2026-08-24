using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Common.Validation;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Exceptions;
using FluentValidation;
using MediatR;

namespace App.Application.Appointments.Commands.SetAppointmentDuration;

/// <summary>
/// Ustawia (lub czyści) niestandardowy czas trwania wizyty — dedykowana, lekka kontrolka „reguluj czas
/// per wizyta" z panelu (arkusz szczegółów). Nie rusza pracownika/terminu/składu usług; przelicza tylko
/// długość bloku. <paramref name="DurationMinutes"/> = <c>null</c> lub równe standardowej sumie czasów
/// usług → powrót do czasu standardowego. Gdy nowy (dłuższy) blok koliduje z inną wizytą lub wychodzi
/// poza godziny pracy — <see cref="AppointmentSlotUnavailableException"/> (409), UI proponuje „Zmień termin".
/// </summary>
public record SetAppointmentDurationCommand(Guid AppointmentId, int? DurationMinutes)
  : IRequest<Guid>, IAppointmentWriteRequest;

/// <summary>
/// Waliduje override czasu PRZED handlerem (czyste 400 zamiast niekontrolowanego 500 z zawijania
/// <see cref="TimeOnly"/>). Zakres spójny z limitami usług (1..24h); <c>null</c> = powrót do standardu.
/// </summary>
public sealed class SetAppointmentDurationCommandValidator : AppValidator<SetAppointmentDurationCommand>
{
  public SetAppointmentDurationCommandValidator()
  {
    RuleFor(x => x.AppointmentId).NotEmpty();
    RuleFor(x => x.DurationMinutes!.Value)
      .GreaterThan(0)
      .LessThanOrEqualTo(Appointment.MaxCustomDurationMinutes)
      .When(x => x.DurationMinutes.HasValue);
  }
}

public class SetAppointmentDurationHandler : TenantHandler<SetAppointmentDurationCommand, Guid>
{
  private readonly IAppointmentRepository _repository;
  private readonly IEmployeeRepository _employeeRepository;
  private readonly IUnitOfWork _uow;
  private readonly IAppointmentService _appointmentService;
  private readonly IStaffAccessPolicy _access;

  public SetAppointmentDurationHandler(
    IAppointmentRepository repository,
    IEmployeeRepository employeeRepository,
    IUnitOfWork uow,
    ICurrentTenantService currentTenantService,
    IAppointmentService appointmentService,
    IStaffAccessPolicy access)
      : base(currentTenantService)
  {
    _repository = repository;
    _employeeRepository = employeeRepository;
    _uow = uow;
    _appointmentService = appointmentService;
    _access = access;
  }

  public override async Task<Guid> Handle(SetAppointmentDurationCommand request, CancellationToken ct)
  {
    var appointment = await _repository.GetByIdAsync(request.AppointmentId)
        ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    if (appointment.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    // Autoryzacja właściciela kalendarza (rola + StaffCalendarVisibilityPolicy salonu). Wizytę mamy
    // już w ręku, więc pytamy o pracownika, zamiast dociągać ją drugi raz przez policy.
    await _access.EnsureCanMutateEmployeeCalendarAsync(appointment.EmployeeId, ct);

    // Czas zmieniamy tylko dla wizyt aktywnych. Anulowana / zakończona = stan terminalny.
    if (appointment.Status == AppointmentStatus.Canceled || appointment.Status == AppointmentStatus.Completed)
    {
      throw new AppointmentBookingRuleException(
        "Nie można zmienić czasu anulowanej lub zakończonej wizyty.",
        ErrorCodes.AppointmentServicesChangeInvalidStatus);
    }

    var employee = await _employeeRepository.GetByIdAsync(appointment.EmployeeId)
      ?? throw new NotFoundException(nameof(Employee), appointment.EmployeeId);

    // Standardowa suma czasów pozycji (bez reloadu usług — snapshot trzyma czasy w pozycjach).
    var standardSum = appointment.Items.Sum(i => i.DurationMinutes);
    var effectiveDuration = request.DurationMinutes ?? standardSum;
    var endTime = appointment.StartTime.AddMinutes(effectiveDuration);

    // Nowy blok musi się zmieścić (bez kolizji, w oknie pracy) — ignorując samą tę wizytę.
    var isAvailable = await _appointmentService.IsAvailableAsync(
      employee, appointment.StartTime, endTime, appointment.Date, TenantId, appointment.Id);

    if (!isAvailable)
    {
      throw new AppointmentSlotUnavailableException();
    }

    appointment.SetCustomDuration(request.DurationMinutes);

    await _uow.SaveChangesAsync(ct);

    return appointment.Id;
  }
}
