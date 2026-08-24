using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Application.Common.Security;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.SetScheduleOverride;

/// <summary>
/// Ustawia nadpisanie grafiku w danym dniu. Istniejące wizyty nie blokują operacji —
/// w odpowiedzi wraca lista wizyt poza nowym harmonogramem, żeby UI mógł pokazać je
/// pracownikowi (decyzja o anulowaniu/przeniesieniu należy do niego).
/// </summary>
public record SetScheduleOverrideCommand(
  Guid EmployeeId,
  DateOnly Date,
  SlotGenerationMode SlotGenerationMode = SlotGenerationMode.Grid,
  IReadOnlyCollection<TimeRangeDto>? WorkRanges = null,
  IReadOnlyCollection<TimeRangeDto>? Breaks = null,
  IReadOnlyCollection<TimeOnly>? FixedStartTimes = null
) : IRequest<SetScheduleOverrideResult>;

public record SetScheduleOverrideResult(
  IReadOnlyCollection<OutsideScheduleAppointmentDto> AppointmentsOutsideSchedule
);

internal class SetScheduleOverrideCommandHandler
    : TenantHandler<SetScheduleOverrideCommand, SetScheduleOverrideResult>
{
  private readonly IEmployeeRepository _repository;
  private readonly IAppointmentRepository _appointmentRepository;
  private readonly IUnitOfWork _uow;
  private readonly IStaffAccessPolicy _access;

  public SetScheduleOverrideCommandHandler(
      IEmployeeRepository repository,
      IAppointmentRepository appointmentRepository,
      IUnitOfWork uow,
      IStaffAccessPolicy access,
      ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _repository = repository;
    _appointmentRepository = appointmentRepository;
    _uow = uow;
    _access = access;
  }

  public override async Task<SetScheduleOverrideResult> Handle(SetScheduleOverrideCommand request, CancellationToken ct)
  {
    _access.EnsureSelfOrStaffManager(request.EmployeeId);

    var employee = await _repository.GetByIdAsync(request.EmployeeId)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    if (employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    var appointments = await _appointmentRepository.GetAppointmentsByDateAsync(request.EmployeeId, request.Date, TenantId);

    var isFixed = request.SlotGenerationMode == SlotGenerationMode.FixedStartTimes;
    var isDayOff = isFixed
      ? request.FixedStartTimes is null || request.FixedStartTimes.Count == 0
      : request.WorkRanges is null || request.WorkRanges.Count == 0;

    var outside = new List<OutsideScheduleAppointmentDto>();

    if (appointments.Count > 0 && isDayOff)
    {
      outside.AddRange(appointments.Select(a => new OutsideScheduleAppointmentDto(
        a.Id, a.EmployeeId, a.Date, a.StartTime, a.EndTime)));
    }

    // Tryb stałych godzin: dostępność jest permisywna (sloty = godziny startu, brak okna pracy),
    // więc istniejące wizyty nie wypadają „poza grafik" przy zmianie godzin — pomijamy wyliczanie.
    if (appointments.Count > 0 && !isDayOff && !isFixed)
    {
      var workRangesVo = request.WorkRanges!
        .Select(r => new TimeRange(r.StartTime, r.EndTime))
        .ToList();

      var breaksVo = (request.Breaks ?? Array.Empty<TimeRangeDto>())
        .Select(b => new TimeRange(b.StartTime, b.EndTime))
        .ToList();

      foreach (var appointment in appointments)
      {
        var appointmentRange = new TimeRange(appointment.StartTime, appointment.EndTime);
        var fitsInWork = workRangesVo.Any(r => r.Contains(appointmentRange));
        var hitsBreak = breaksVo.Any(br => br.OverlapsWith(appointmentRange));

        if (!fitsInWork || hitsBreak)
        {
          outside.Add(new OutsideScheduleAppointmentDto(
            appointment.Id, appointment.EmployeeId, appointment.Date, appointment.StartTime, appointment.EndTime));
        }
      }
    }

    if (isDayOff)
    {
      employee.RemoveScheduleOverride(request.Date);
      await _uow.SaveChangesAsync(ct);
      return new SetScheduleOverrideResult(outside);
    }

    ScheduleDay overrideScheduleDay;
    if (isFixed)
    {
      var fixedTimes = request.FixedStartTimes!.Distinct().OrderBy(t => t).ToList();
      overrideScheduleDay = new ScheduleDay(fixedTimes);
    }
    else
    {
      var workRanges = request.WorkRanges!
        .Select(r => new TimeRange(r.StartTime, r.EndTime))
        .ToList();
      var breaks = (request.Breaks ?? Array.Empty<TimeRangeDto>())
        .Select(b => new TimeRange(b.StartTime, b.EndTime))
        .ToList();
      overrideScheduleDay = new ScheduleDay(workRanges, breaks);
    }

    employee.SetScheduleOverride(request.Date, overrideScheduleDay);
    await _uow.SaveChangesAsync(ct);
    return new SetScheduleOverrideResult(outside);
  }
}
