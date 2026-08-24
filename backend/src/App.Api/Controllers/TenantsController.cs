using Microsoft.AspNetCore.Mvc;
using App.Application.Tenants.Dtos;
using App.Application.Tenants.Queries.GetTenants;
using App.Application.Tenants.Queries.GetTenantById;
using App.Application.Tenants.Queries.GetTenantsAdmin;
using App.Application.Tenants.Commands.CreateTenant;
using App.Application.Tenants.Commands.DeleteTenant;
using App.Application.Tenants.Commands.MarkFoundingMember;
using App.Application.Tenants.Commands.SetTenantSubscription;
using App.Application.Tenants.Commands.SetTenantSmsHardCap;
using App.Application.Tenants.Commands.SetTenantCustomDomain;
using App.Application.Tenants.Commands.AddTenantEmployee;
using App.Application.Tenants.Commands.AnonymizeTenantEmployee;
using App.Application.Tenants.Queries.GetTenantEmployees;
using App.Domain.Aggregates.TenantAggregate;
using App.Application.Tenants.Commands.UpdateTenant;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;

namespace App.Api.Controllers;

[Authorize(Policy = "SystemAdminOnly")]
public class TenantsController : ApiControllerBase
{
  [HttpGet(Name = "GetTenants")]
  public async Task<ActionResult<List<TenantDto>>> GetTenants()
  {
    var result = await Mediator.Send(new GetTenantsQuery());
    return Ok(result);
  }

  [HttpGet("admin", Name = "GetTenantsAdmin")]
  public async Task<ActionResult<List<TenantAdminDto>>> GetTenantsAdmin()
  {
    var result = await Mediator.Send(new GetTenantsAdminQuery());
    return Ok(result);
  }

  [HttpGet("{id}", Name = "GetTenantById")]
  public async Task<ActionResult<TenantDto>> GetTenant(Guid id)
  {
    var result = await Mediator.Send(new GetTenantByIdQuery(id));
    return Ok(result);
  }

  [HttpPost]
  public async Task<ActionResult<Guid>> CreateTenant(CreateTenantCommand command)
  {
    var tenantId = await Mediator.Send(command);
    return Ok(tenantId);
  }

  [HttpPut("{id}")]
  public async Task<ActionResult> UpdateTenant(Guid id, [FromBody] UpdateTenantRequest request)
  {
    await Mediator.Send(new UpdateTenantCommand(id, request.Name, request.Slug, request.TimeZoneId, request.Currency));
    return NoContent();
  }

  [HttpPut("{id}/subscription", Name = "SetTenantSubscription")]
  public async Task<ActionResult> SetSubscription(Guid id, [FromBody] SetSubscriptionRequest request)
  {
    await Mediator.Send(new SetTenantSubscriptionCommand(
      id,
      request.Status,
      request.Seats,
      request.IsFoundingMember,
      request.TrialEndsAt,
      request.CurrentPeriodEndsAt));
    return NoContent();
  }

  /// <summary>
  /// Admin: twardy miesięczny limit SMS salonu (anti-abuse). <c>hardCap == null</c> → limit z planu.
  /// </summary>
  [HttpPut("{id}/sms-hard-cap", Name = "SetTenantSmsHardCap")]
  public async Task<ActionResult> SetSmsHardCap(Guid id, [FromBody] SetSmsHardCapRequest request)
  {
    await Mediator.Send(new SetTenantSmsHardCapCommand(id, request.HardCap));
    return NoContent();
  }

  [HttpPost("{id}/founding-member", Name = "MarkTenantFoundingMember")]
  public async Task<ActionResult> MarkFoundingMember(Guid id)
  {
    await Mediator.Send(new MarkFoundingMemberCommand(id));
    return NoContent();
  }

  /// <summary>
  /// Admin: ustawia/czyści white-label domenę salonu (np. <c>salon-przyklad.pl</c>).
  /// Pusta wartość wyłącza white-label. Odświeża rejestr (CORS / resolve-host / On-Demand TLS).
  /// </summary>
  [HttpPut("{id}/custom-domain", Name = "SetTenantCustomDomain")]
  public async Task<ActionResult> SetCustomDomain(Guid id, [FromBody] SetTenantCustomDomainRequest request)
  {
    await Mediator.Send(new SetTenantCustomDomainCommand(id, request.CustomDomain));
    return NoContent();
  }

  /// <summary>Admin: lista aktywnych pracowników wskazanego salonu.</summary>
  [HttpGet("{id}/employees", Name = "GetTenantEmployees")]
  public async Task<ActionResult<List<TenantEmployeeDto>>> GetTenantEmployees(Guid id)
  {
    var result = await Mediator.Send(new GetTenantEmployeesQuery(id));
    return Ok(result);
  }

  /// <summary>
  /// Admin: dodaje pracownika do wskazanego salonu jako zasób kalendarza (bez logowania).
  /// Konto logowania można dopisać później.
  /// </summary>
  [HttpPost("{id}/employees", Name = "AddTenantEmployee")]
  public async Task<ActionResult<Guid>> AddTenantEmployee(Guid id, [FromBody] AddTenantEmployeeRequest request)
  {
    var employeeId = await Mediator.Send(
      new AddTenantEmployeeCommand(id, request.FirstName, request.LastName, request.Email));
    return Ok(employeeId);
  }

  /// <summary>
  /// Admin (RODO art. 17): trwale anonimizuje dane osobowe pracownicy salonu (imię/nazwisko/e-mail).
  /// Rekord zostaje (integralność historii wizyt), ale osoby nie da się już zidentyfikować.
  /// </summary>
  [HttpPost("{id}/employees/{employeeId}/anonymize", Name = "AnonymizeTenantEmployee")]
  public async Task<ActionResult> AnonymizeTenantEmployee(Guid id, Guid employeeId)
  {
    await Mediator.Send(new AnonymizeTenantEmployeeCommand(id, employeeId));
    return NoContent();
  }

  [HttpDelete("{id}")]
  public async Task<ActionResult> DeleteTenant(Guid id)
  {
    await Mediator.Send(new DeleteTenantCommand(id));
    return NoContent();
  }
}

public sealed record AddTenantEmployeeRequest(
    [Required, MaxLength(100)] string FirstName,
    [Required, MaxLength(100)] string LastName,
    [Required, EmailAddress, MaxLength(255)] string Email
);

public sealed record SetTenantCustomDomainRequest(
    [MaxLength(253)] string? CustomDomain
);

public sealed record SetSubscriptionRequest(
    [Required] SubscriptionStatus Status,
    [Range(1, 1000)] int Seats,
    bool IsFoundingMember,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? CurrentPeriodEndsAt
);

public sealed record SetSmsHardCapRequest(
    [Range(0, 1_000_000)] int? HardCap
);
