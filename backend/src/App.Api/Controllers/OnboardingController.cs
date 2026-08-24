using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using App.Application.Onboarding.Commands.ApplyIndustryTemplate;
using App.Application.Onboarding.Commands.CompleteOnboarding;
using App.Application.Onboarding.Commands.CompleteProfile;
using App.Application.Onboarding.Commands.SetOnboardingSchedule;
using App.Application.Onboarding.Queries.GetIndustryTemplates;
using App.Application.Onboarding.Queries.GetOnboardingState;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace App.Api.Controllers;

/// <summary>
/// Kreator onboardingu właściciela (Droga B). Wszystkie akcje wymagają zalogowanego usera (JWT).
/// <c>state</c> i <c>profile</c> działają PRZED utworzeniem tenanta — user id bierzemy z claima,
/// nie z rozwiązanego tenanta. Kroki po utworzeniu tenanta (industry/schedule/complete) rozwiązują
/// tenanta przez middleware z rekordu Employee.
///
/// PODZIAŁ BRAMEK (preflight 2026-07-31): odczyty zostają na gołym <c>[Authorize]</c>, bo
/// <c>state</c> woła bootstrap KAŻDEJ roli — pracownik musi dostać odpowiedź „nie jesteś w
/// kreatorze", żeby guard nie odbił go w pętli na <c>/setup</c>. Wszystkie cztery MUTACJE mają
/// <c>BusinessManagement</c>: wcześniej klasowe <c>[Authorize]</c> wystawiało je każdej zalogowanej
/// roli, więc pracownik i konto „Recepcja" mogły zmienić nazwę salonu i jego PUBLICZNY slug
/// rezerwacji — wbrew obietnicy przy definicji polityk w Program.cs, że Kiosk nie zmienia ustawień.
///
/// Rola Owner jest nadawana już przy rejestracji (RegisterOwner), a nie dopiero z tenantem, więc
/// polityka nie zamyka drogi właścicielowi, który jeszcze nie ma salonu.
/// </summary>
[Authorize]
[Route("api/onboarding")]
public sealed class OnboardingController : ApiControllerBase
{
  /// <summary>Punkt wznowienia kreatora dla bieżącego usera (steruje przekierowaniem w /setup).</summary>
  [HttpGet("state")]
  public async Task<ActionResult<OnboardingStateDto>> GetState(CancellationToken ct)
  {
    var userId = GetUserId();
    if (userId == null)
    {
      return Unauthorized();
    }

    return Ok(await Mediator.Send(new GetOnboardingStateQuery(userId.Value), ct));
  }

  /// <summary>Statyczny katalog branż + propozycji usług (krok „Czym się zajmujesz?" / „Twoje usługi").</summary>
  [HttpGet("industry-templates")]
  public async Task<ActionResult<GetIndustryTemplatesResult>> GetIndustryTemplates(CancellationToken ct)
    => Ok(await Mediator.Send(new GetIndustryTemplatesQuery(), ct));

  /// <summary>Krok „Jak się nazywasz? / Nazwa salonu" — TU powstaje Tenant + Employee.</summary>
  [EnableRateLimiting("AuthSensitive")]
  [Authorize(Policy = "BusinessManagement")]
  [HttpPost("profile")]
  public async Task<ActionResult<CompleteProfileResult>> CompleteProfile(
    CompleteProfileRequest request, CancellationToken ct)
  {
    var userId = GetUserId();
    if (userId == null)
    {
      return Unauthorized();
    }

    var result = await Mediator.Send(
      new CompleteProfileCommand(
        userId.Value, request.FirstName, request.LastName, request.SalonName, request.SalonSlug),
      ct);
    return Ok(result);
  }

  /// <summary>Krok „Twoje usługi" — zapisuje branżę i tworzy wybrane usługi.</summary>
  [EnableRateLimiting("AuthSensitive")]
  [Authorize(Policy = "BusinessManagement")]
  [HttpPost("industry")]
  public async Task<ActionResult<ApplyIndustryTemplateResult>> ApplyIndustry(
    ApplyIndustryRequest request, CancellationToken ct)
  {
    var services = (request.Services ?? [])
      .Select(s => new TemplateServiceSelection(s.Name, s.Price, s.DurationMinutes))
      .ToList();

    var result = await Mediator.Send(
      new ApplyIndustryTemplateCommand(request.IndustryKey, services), ct);
    return Ok(result);
  }

  /// <summary>
  /// Krok „Jaki grafik? / Kiedy pracujesz?" — domyka krok na dwa sposoby: grafikiem powtarzalnym
  /// pracownika-właściciela albo deklaracją „ustawiam dni na bieżąco" (<c>UseAdHoc</c>).
  /// </summary>
  [EnableRateLimiting("AuthSensitive")]
  [Authorize(Policy = "BusinessManagement")]
  [HttpPost("schedule")]
  public async Task<IActionResult> SetSchedule(SetScheduleRequest request, CancellationToken ct)
  {
    await Mediator.Send(
      new SetOnboardingScheduleCommand(
        request.SlotMode,
        (request.Days ?? [])
          .Select(d => new OnboardingScheduleDayInput(d.Day, d.StartTime, d.EndTime))
          .ToList(),
        request.FixedStartTimes,
        request.UseAdHoc),
      ct);
    return NoContent();
  }

  /// <summary>Ostatni krok — tryb potwierdzania wizyt + oznaczenie onboardingu jako ukończonego.</summary>
  [EnableRateLimiting("AuthSensitive")]
  [Authorize(Policy = "BusinessManagement")]
  [HttpPost("complete")]
  public async Task<ActionResult<CompleteOnboardingResult>> Complete(
    CompleteOnboardingRequest request, CancellationToken ct)
    => Ok(await Mediator.Send(new CompleteOnboardingCommand(request.ConfirmationMode), ct));

  private Guid? GetUserId()
  {
    var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    return Guid.TryParse(value, out var id) ? id : null;
  }
}

public sealed record CompleteProfileRequest(
  [Required, MaxLength(100)] string FirstName,
  [Required, MaxLength(100)] string LastName,
  [Required, MaxLength(100)] string SalonName,
  [Required, MaxLength(100), RegularExpression("^[a-zA-Z0-9-]+$")] string SalonSlug);

public sealed record OnboardingServiceInput(
  [Required, MaxLength(100)] string Name,
  decimal Price,
  int DurationMinutes);

public sealed record ApplyIndustryRequest(
  [Required, MaxLength(64)] string IndustryKey,
  IReadOnlyList<OnboardingServiceInput>? Services);

/// <summary>Jeden dzień roboczy. Godziny są per dzień; w trybie stałym oba pola są <c>null</c>.</summary>
public sealed record SetScheduleDayInput(DayOfWeek Day, TimeOnly? StartTime, TimeOnly? EndTime);

/// <param name="Days">
/// Dni pracy wraz z godzinami — każdy dzień ma WŁASNY zakres (Pn 9–17, Wt 10–18). W trybie stałych
/// godzin startu <c>StartTime</c>/<c>EndTime</c> są <c>null</c>: dzień nie ma wtedy okna pracy.
/// </param>
/// <param name="UseAdHoc">
/// „Papierowy kalendarz" — właściciel nie prowadzi grafiku powtarzalnego i wpisuje dostępność
/// dzień po dniu. Pozostałe pola są wtedy ignorowane.
/// </param>
public sealed record SetScheduleRequest(
  SlotGenerationMode SlotMode,
  IReadOnlyList<SetScheduleDayInput>? Days,
  IReadOnlyList<TimeOnly>? FixedStartTimes,
  bool UseAdHoc = false);

public sealed record CompleteOnboardingRequest(AppointmentConfirmationMode? ConfirmationMode);
