using App.Application.Common.Interfaces;
using App.Application.Common;
using App.Application.Common.Security;
using App.Application.Employees.Dtos;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Exceptions;
using MediatR;


namespace App.Application.Employees.Commands.AddEmployeeLeave;

/// <summary>
/// Dodaje urlop pracownika. Istniejące wizyty nie blokują operacji — w odpowiedzi
/// wraca lista wizyt kolidujących z urlopem, żeby UI mógł poinformować pracownika.
/// </summary>
public record AddEmployeeLeaveCommand(
  Guid EmployeeId,
  DateOnly StartDate,
  DateOnly EndDate,
  AbsenceType AbsenceType = AbsenceType.Vacation) : IRequest<AddEmployeeLeaveResult>;

public record AddEmployeeLeaveResult(
  IReadOnlyCollection<OutsideScheduleAppointmentDto> AppointmentsInLeave
);

internal class AddEmployeeLeaveCommandHandler : TenantHandler<AddEmployeeLeaveCommand, AddEmployeeLeaveResult>
{
  private readonly IEmployeeRepository _employeeRepository;
  private readonly IAppointmentRepository _appointmentRepository;
  private readonly IUnitOfWork _uow;
  private readonly IStaffAccessPolicy _access;

  public AddEmployeeLeaveCommandHandler(
    IEmployeeRepository employeeRepository,
    IAppointmentRepository appointmentRepository,
    IUnitOfWork uow,
    IStaffAccessPolicy access,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _employeeRepository = employeeRepository;
    _appointmentRepository = appointmentRepository;
    _uow = uow;
    _access = access;
  }

  public override async Task<AddEmployeeLeaveResult> Handle(AddEmployeeLeaveCommand request, CancellationToken ct)
  {
    _access.EnsureSelfOrStaffManager(request.EmployeeId);

    var employee = await _employeeRepository.GetByIdWithLeavesAsync(request.EmployeeId)
        ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);

    if (employee.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    if (request.StartDate > request.EndDate)
    {
      throw new InvalidLeaveDatesException();
    }

    if (employee.HasLeaveOverlap(request.StartDate, request.EndDate))
    {
      throw new LeaveOverlapException();
    }

    var collisions = await _appointmentRepository.GetAppointmentsInDateRangeAsync(
      request.EmployeeId,
      request.StartDate,
      request.EndDate,
      TenantId);

    var conflicting = collisions.Select(a => new OutsideScheduleAppointmentDto(
      a.Id, a.EmployeeId, a.Date, a.StartTime, a.EndTime)).ToList();

    employee.AddLeave(request.StartDate, request.EndDate, request.AbsenceType);
    await _uow.SaveChangesAsync(ct);
    return new AddEmployeeLeaveResult(conflicting);
  }
}
