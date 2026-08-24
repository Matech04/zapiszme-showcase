using System.Security.Claims;
using App.Application.Guides.Commands.MarkGuideCompleted;
using App.Application.Guides.Commands.ResetGuideCompletion;
using App.Application.Guides.Queries.GetGuideCompletions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

/// <summary>
/// Postęp interaktywnych przewodników po panelu (katalog <c>/admin/guides</c>).
///
/// Stan jest per UŻYTKOWNIK, nie per salon — dlatego <c>UserId</c> bierzemy z claima, a nie
/// z rozwiązanego tenanta, i endpointy działają także dla konta bez salonu (admin systemowy,
/// właściciel w trakcie kreatora). Tak samo robi <c>OnboardingController</c>.
///
/// Samo <c>[Authorize]</c> bez polityki ról: przewodniki ma każda zalogowana rola, a to, KTÓRE
/// z nich widzi, rozstrzyga rejestr po stronie dashboardu (<c>roles[]</c> w definicji).
/// Backend przechowuje wyłącznie znaczniki „ukończono" i nie jest bramką uprawnień.
/// </summary>
[Authorize]
[Route("api/guides")]
public sealed class GuidesController : ApiControllerBase
{
  /// <summary>Identyfikatory przewodników ukończonych przez zalogowanego użytkownika.</summary>
  [HttpGet("completions")]
  public async Task<ActionResult<IReadOnlyList<string>>> GetCompletions(CancellationToken ct)
  {
    var userId = GetUserId();
    if (userId == null)
    {
      return Unauthorized();
    }

    return Ok(await Mediator.Send(new GetGuideCompletionsQuery(userId.Value), ct));
  }

  /// <summary>Oznacza przewodnik jako ukończony. Idempotentne.</summary>
  [HttpPost("{guideId}/complete")]
  public async Task<IActionResult> MarkCompleted(string guideId, CancellationToken ct)
  {
    var userId = GetUserId();
    if (userId == null)
    {
      return Unauthorized();
    }

    await Mediator.Send(new MarkGuideCompletedCommand(userId.Value, guideId), ct);
    return NoContent();
  }

  /// <summary>Cofa oznaczenie „ukończono" („zresetuj postęp" w katalogu). Idempotentne.</summary>
  [HttpDelete("{guideId}/complete")]
  public async Task<IActionResult> ResetCompletion(string guideId, CancellationToken ct)
  {
    var userId = GetUserId();
    if (userId == null)
    {
      return Unauthorized();
    }

    await Mediator.Send(new ResetGuideCompletionCommand(userId.Value, guideId), ct);
    return NoContent();
  }

  private Guid? GetUserId()
  {
    var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    return Guid.TryParse(value, out var id) ? id : null;
  }
}
