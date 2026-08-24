using App.Api.Security;
using App.Application.Admin.Impersonation;
using App.Application.Admin.Impersonation.Commands.EndImpersonation;
using App.Application.Admin.Impersonation.Commands.StartImpersonation;
using App.Application.Admin.Impersonation.Dtos;
using App.Application.Admin.Impersonation.Queries.GetCurrentImpersonation;
using App.Application.Admin.Impersonation.Queries.GetImpersonationHistory;
using App.Application.Common;
using App.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace App.Api.Controllers;

/// <summary>
/// Support impersonation — admin platformy uzyskuje czasowy, audytowany dostęp do tenanta.
/// Start ustawia podpisany cookie; middleware na jego podstawie nadpisuje tenanta przy
/// każdym żądaniu (źródło prawdy: rekord sesji w bazie).
/// </summary>
[Authorize(Policy = "SystemAdminOnly")]
[Route("api/admin/impersonation")]
public class AdminImpersonationController : ApiControllerBase
{
  private readonly IImpersonationTicketService _ticket;
  private readonly IHostEnvironment _env;
  private readonly ImpersonationOptions _options;

  public AdminImpersonationController(
    IImpersonationTicketService ticket,
    IHostEnvironment env,
    IOptions<ImpersonationOptions> options)
  {
    _ticket = ticket;
    _env = env;
    _options = options.Value;
  }

  /// <summary>Rozpoczyna sesję wsparcia i ustawia cookie.</summary>
  [HttpPost]
  public async Task<ActionResult<StartImpersonationResult>> Start([FromBody] StartImpersonationBody body)
  {
    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
    var readOnly = body.ReadOnly ?? _options.DefaultReadOnly;

    var result = await Mediator.Send(new StartImpersonationCommand(body.TenantId, body.Reason, readOnly, ip));

    var token = _ticket.Protect(result.SessionId, result.ExpiresAtUtc);
    Response.Cookies.Append(ImpersonationConstants.CookieName, token, BuildCookieOptions(result.ExpiresAtUtc));

    return Ok(result);
  }

  /// <summary>Kończy bieżącą sesję wsparcia (id z cookie) i czyści cookie.</summary>
  [HttpDelete]
  public async Task<ActionResult> End()
  {
    if (Request.Cookies.TryGetValue(ImpersonationConstants.CookieName, out var token)
        && _ticket.TryUnprotect(token, out var sessionId))
    {
      await Mediator.Send(new EndImpersonationCommand(sessionId));
    }

    Response.Cookies.Delete(ImpersonationConstants.CookieName, BuildCookieOptions(null));
    return NoContent();
  }

  /// <summary>Stan aktywnej sesji wsparcia (dla banera). Zwraca null, gdy brak/wygasła.</summary>
  [HttpGet("current")]
  public async Task<ActionResult<ImpersonationStatusDto?>> Current()
  {
    if (!Request.Cookies.TryGetValue(ImpersonationConstants.CookieName, out var token)
        || !_ticket.TryUnprotect(token, out var sessionId))
    {
      return Ok((ImpersonationStatusDto?)null);
    }

    var dto = await Mediator.Send(new GetCurrentImpersonationQuery(sessionId));
    return Ok(dto);
  }

  /// <summary>Dziennik audytu sesji wsparcia.</summary>
  [HttpGet("history")]
  public async Task<ActionResult<List<ImpersonationHistoryItemDto>>> History(
    [FromQuery] Guid? tenantId,
    [FromQuery] int take = 100)
  {
    var result = await Mediator.Send(new GetImpersonationHistoryQuery(tenantId, take));
    return Ok(result);
  }

  private CookieOptions BuildCookieOptions(DateTime? expiresAtUtc)
  {
    var options = new CookieOptions
    {
      HttpOnly = true,
      Path = "/",
      IsEssential = true,
      Expires = expiresAtUtc,
    };
    CrossOriginCookiePolicy.Apply(options, _env, Request.IsHttps);
    return options;
  }
}

public sealed record StartImpersonationBody(Guid TenantId, string Reason, bool? ReadOnly);
