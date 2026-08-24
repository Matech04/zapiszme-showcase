using App.Domain.Aggregates.EmployeeAggregate;
using App.Application.Employees.Commands.CreateEmployee;
using App.Application.Employees.Commands.DeleteEmployee;
using App.Application.Employees.Commands.UpdateEmployee;
using App.Application.Employees.Dtos;
using App.Application.Employees.Queries.GetEmployeeById;
using App.Application.Employees.Commands.AssignService;
using App.Application.Employees.Commands.UnassignService;
using App.Application.Employees.Commands.UpdateEmployeeService;
using App.Application.Employees.Commands.SetEmployeeSchedule;
using App.Application.Employees.Commands.SetEmployeeScheduleActive;
using App.Application.Employees.Commands.DeleteEmployeeSchedule;
using App.Application.Employees.Commands.SetScheduleOverride;
using App.Application.Employees.Commands.RemoveScheduleOverride;
using App.Application.Employees.Commands.SetMonthPublication;
using App.Application.Employees.Commands.RemoveMonthPublication;
using App.Application.Employees.Queries.GetMonthPublications;
using App.Application.Employees.Queries.GetEmployeeSchedules;
using App.Application.Employees.Queries.GetScheduleOverrides;
using App.Application.Employees.Queries.GetEmployeeServices;
using App.Application.Employees.Queries.GetEmployees;
using App.Application.Employees.Queries.GetEmployeeLeaves;
using App.Application.Employees.Commands.AddEmployeeLeave;
using App.Application.Employees.Commands.RemoveEmployeeLeave;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

[Authorize(Policy = "GeneralAccess")]
public class EmployeesController : ApiControllerBase
{
  [HttpGet(Name = "GetEmployees")]
  public async Task<ActionResult<List<EmployeeDto>>> GetEmployees()
  {
    var result = await Mediator.Send(new GetEmployeesQuery());
    return Ok(result);
  }

  [HttpGet("{id}", Name = "GetEmployeeById")]
  public async Task<ActionResult<EmployeeDto>> GetEmployee(Guid id)
  {
    var result = await Mediator.Send(new GetEmployeeByIdQuery(id));
    return Ok(result);
  }

  [HttpPost]
  [Authorize(Policy = "StaffManagement")]
  public async Task<ActionResult<Guid>> CreateEmployee(CreateEmployeeCommand command)
  {
    var employeeId = await Mediator.Send(command);
    return Ok(employeeId);
  }

  [HttpPut("{id}")]
  public async Task<ActionResult> UpdateEmployee(Guid id, [FromBody] UpdateEmployeeRequest request)
  {
    await Mediator.Send(new UpdateEmployeeCommand(id, request.FirstName, request.LastName, request.Specialization));
    return NoContent();
  }

  [HttpDelete("{id}")]
  [Authorize(Policy = "StaffManagement")]
  public async Task<ActionResult> DeleteEmployee(Guid id)
  {
    await Mediator.Send(new DeleteEmployeeCommand(id));
    return NoContent();
  }

  [HttpGet("{id}/services")]
  public async Task<ActionResult<List<EmployeeServiceDto>>> GetEmployeeServices(Guid id, CancellationToken ct)
  {
    var result = await Mediator.Send(new GetEmployeeServicesQuery(id), ct);
    return Ok(result);
  }

  [HttpPost("{id}/services")]
  public async Task<ActionResult> AssignService(Guid id, [FromBody] AssignServiceRequest request)
  {
    await Mediator.Send(new AssignServiceCommand(id, request.ServiceId, request.CustomDuration, request.Amount, request.Currency));
    return NoContent();
  }

  [HttpPut("{id}/services/{serviceId}")]
  public async Task<ActionResult> UpdateEmployeeService(Guid id, Guid serviceId, UpdateEmployeeServiceRequest request)
  {
    await Mediator.Send(new UpdateEmployeeServiceCommand(id, serviceId, request.CustomDuration, request.Amount, request.Currency));
    return NoContent();
  }

  [HttpDelete("{id}/services/{serviceId}")]
  public async Task<ActionResult> UnassignService(Guid id, Guid serviceId)
  {
    await Mediator.Send(new UnassignServiceCommand(id, serviceId));
    return NoContent();
  }

  [HttpGet("{id}/leaves")]
  public async Task<ActionResult<List<EmployeeLeaveDto>>> GetEmployeeLeaves(Guid id, CancellationToken ct)
  {
    var result = await Mediator.Send(new GetEmployeeLeavesQuery(id), ct);
    return Ok(result);
  }

  [HttpPost("{id}/leaves")]
  public async Task<ActionResult<AddEmployeeLeaveResult>> AddEmployeeLeave(Guid id, [FromBody] AddEmployeeLeaveRequest request)
  {
    var result = await Mediator.Send(new AddEmployeeLeaveCommand(id, request.StartDate, request.EndDate, request.AbsenceType ?? AbsenceType.Vacation));
    return Ok(result);
  }

  [HttpDelete("{id}/leaves/{leaveId}")]
  public async Task<ActionResult> RemoveEmployeeLeave(Guid id, Guid leaveId)
  {
    await Mediator.Send(new RemoveEmployeeLeaveCommand(id, leaveId));
    return NoContent();
  }

  [HttpGet("{id}/employee-schedules")]
  public async Task<ActionResult<List<EmployeeScheduleDto>>> GetEmployeeSchedules(Guid id, CancellationToken ct)
  {
    var result = await Mediator.Send(new GetEmployeeSchedulesQuery(id), ct);
    return Ok(result);
  }

  [HttpPost("{id}/employee-schedules")]
  public async Task<ActionResult> SetEmployeeSchedule(Guid id, [FromBody] EmployeeScheduleDto request)
  {
    await Mediator.Send(new SetEmployeeScheduleCommand(id, request));
    return NoContent();
  }

  [HttpPut("{id}/employee-schedules/{scheduleId}/active")]
  public async Task<ActionResult> SetEmployeeScheduleActive(Guid id, Guid scheduleId, [FromBody] SetScheduleActiveRequest request)
  {
    await Mediator.Send(new SetEmployeeScheduleActiveCommand(id, scheduleId, request.IsActive));
    return NoContent();
  }

  [HttpDelete("{id}/employee-schedules/{scheduleId}")]
  public async Task<ActionResult> DeleteEmployeeSchedule(Guid id, Guid scheduleId)
  {
    await Mediator.Send(new DeleteEmployeeScheduleCommand(id, scheduleId));
    return NoContent();
  }

  [HttpGet("{id}/schedule-overrides")]
  public async Task<ActionResult<List<ScheduleOverrideDto>>> GetScheduleOverrides(Guid id, CancellationToken ct)
  {
    var result = await Mediator.Send(new GetScheduleOverridesQuery(id), ct);
    return Ok(result);
  }

  [HttpPost("{id}/schedule-overrides")]
  public async Task<ActionResult<SetScheduleOverrideResult>> SetScheduleOverride(Guid id, [FromBody] ScheduleOverrideDto request)
  {
    var result = await Mediator.Send(new SetScheduleOverrideCommand(
      id, request.Date, request.SlotGenerationMode, request.WorkRanges, request.Breaks, request.FixedStartTimes));
    return Ok(result);
  }

  [HttpDelete("{id}/schedule-overrides/{date}")]
  public async Task<ActionResult> RemoveScheduleOverride(Guid id, DateOnly date)
  {
    await Mediator.Send(new RemoveScheduleOverrideCommand(id, date));
    return NoContent();
  }

  // Autoryzacja tych trzech endpointów siedzi w handlerach (`IStaffAccessPolicy`), tak jak reszty
  // kontrolera po refaktorze — patrz notka nad klasą.
  [HttpGet("{id}/month-publications")]
  public async Task<ActionResult<List<MonthPublicationDto>>> GetMonthPublications(Guid id, CancellationToken ct)
  {
    var result = await Mediator.Send(new GetMonthPublicationsQuery(id), ct);
    return Ok(result);
  }

  [HttpPut("{id}/month-publications")]
  public async Task<ActionResult> SetMonthPublication(Guid id, [FromBody] MonthPublicationDto request)
  {
    await Mediator.Send(new SetMonthPublicationCommand(id, request.Year, request.Month, request.OpensOn));
    return NoContent();
  }

  [HttpDelete("{id}/month-publications/{year:int}/{month:int}")]
  public async Task<ActionResult> RemoveMonthPublication(Guid id, int year, int month)
  {
    await Mediator.Send(new RemoveMonthPublicationCommand(id, year, month));
    return NoContent();
  }
}

// CustomDuration/Amount/Currency opcjonalne — null = dziedzicz z katalogu usługi (brak override).
public record AssignServiceRequest(Guid ServiceId, int? CustomDuration = null, decimal? Amount = null, string? Currency = null);
public record UpdateEmployeeServiceRequest(int? CustomDuration = null, decimal? Amount = null, string? Currency = null);
public record AddEmployeeLeaveRequest(DateOnly StartDate, DateOnly EndDate, AbsenceType? AbsenceType);
public record SetScheduleActiveRequest(bool IsActive);