using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Employees.Commands.SetEmployeeSchedule;

public record SetEmployeeScheduleCommand(Guid EmployeeId, EmployeeScheduleDto Schedule) : IRequest;

internal class SetEmployeeScheduleCommandHandler : TenantHandler<SetEmployeeScheduleCommand>
{
  private readonly IEmployeeRepository _repository;
  private readonly IUnitOfWork _uow;
  private readonly IStaffAccessPolicy _access;

  public SetEmployeeScheduleCommandHandler(
    IEmployeeRepository repository,
    IUnitOfWork uow,
    IStaffAccessPolicy access,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _repository = repository;
    _uow = uow;
    _access = access;
  }

  public override async Task Handle(SetEmployeeScheduleCommand request, CancellationToken ct)
  {
    _access.EnsureSelfOrStaffManager(request.EmployeeId);

    var employee = await _repository.GetByIdAsync(request.EmployeeId)
      ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    if (employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    employee.SetSlotGenerationMode(request.Schedule.SlotGenerationMode);

    var activeRange = new DateRange(request.Schedule.ActiveFrom, request.Schedule.ActiveTo);

    List<ScheduleDay> scheduleDays;
    if (request.Schedule.SlotGenerationMode == SlotGenerationMode.FixedStartTimes)
    {
      // Tryb stałych slotów: dzień bez godzin = wolny → pomijamy (jak puste przedziały w trybie siatki).
      scheduleDays = request.Schedule.Days
        .Select(d => (d.CycleIndex, Times: (d.FixedStartTimes ?? Array.Empty<TimeOnly>()).Distinct().OrderBy(t => t).ToList()))
        .Where(x => x.Times.Count > 0)
        .Select(x => new ScheduleDay(x.Times, x.CycleIndex))
        .ToList();
    }
    else
    {
      scheduleDays = request.Schedule.Days
        .Select(d =>
        {
          var workRanges = d.WorkRanges.Select(r => new TimeRange(r.StartTime, r.EndTime)).ToList();
          var breaks = d.Breaks.Select(b => new TimeRange(b.StartTime, b.EndTime)).ToList();
          return new ScheduleDay(workRanges, breaks, d.CycleIndex);
        })
        .ToList();
    }

    if (request.Schedule.Id.HasValue && request.Schedule.Id.Value != Guid.Empty)
    {
      employee.UpdateSchedule(request.Schedule.Id.Value, activeRange, request.Schedule.NumberOfCycles, scheduleDays, request.Schedule.IsActive);
    }
    else
    {
      employee.AddSchedule(activeRange, request.Schedule.NumberOfCycles, scheduleDays, request.Schedule.IsActive);
    }

    await _uow.SaveChangesAsync(ct);
  }
}
