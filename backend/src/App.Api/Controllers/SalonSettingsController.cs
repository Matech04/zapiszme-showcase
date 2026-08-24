using App.Application.Payments.Commands.ConnectMerchantAccount;
using App.Application.Payments.Queries.GetMerchantAccountStatus;
using App.Application.SalonSettings.Commands.PurgeAppointmentHistory;
using App.Application.SalonSettings.Commands.SetBookingPause;
using App.Application.SalonSettings.Commands.UpdateCurrentSalonSettings;
using App.Application.SalonSettings.Queries.GetCurrentSalonSettings;
using App.Application.Tenants.Dtos;
using App.Application.Tenants.Queries.GetRegisterOwnerSlugAvailability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

/// <summary>
/// Ustawienia bieżącego salonu (tenant z JWT). Mutacje wymagają roli Owner (BusinessManagement);
/// GET jest dostępny dla wszystkich zalogowanych pracowników — kalendarz w panelu czyta
/// politykę widoczności kalendarza (`StaffCalendarVisibilityPolicy`), żeby UI Employee'a
/// dostosowywał się do uprawnień ustawionych przez ownera.
/// </summary>
public class SalonSettingsController : ApiControllerBase
{
  /// <summary>
  /// Sprawdza dostępność sluga przy edycji ustawień — bieżący tenant jest wykluczany z kolizji (własny slug jest OK).
  /// </summary>
  [Authorize(Policy = "BusinessManagement")]
  [HttpGet("slug-availability")]
  public async Task<ActionResult<SlugAvailabilityDto>> GetSlugAvailability(
    [FromQuery] string slug,
    CancellationToken ct)
  {
    var result = await Mediator.Send(
      new GetRegisterOwnerSlugAvailabilityQuery(slug ?? string.Empty, ExcludeCurrentTenant: true),
      ct);

    return Ok(new SlugAvailabilityDto(result.Available));
  }

  [Authorize(Policy = "GeneralAccess")]
  [HttpGet]
  public async Task<ActionResult<TenantDto>> Get()
  {
    var result = await Mediator.Send(new GetCurrentSalonSettingsQuery());

    // GET jest dostępny dla całego personelu (kalendarz czyta StaffCalendarVisibilityPolicy), ale
    // konfiguracja płatności/zadatków należy do właściciela. Pracownik (Employee) nie powinien
    // widzieć trybu/wartości zadatku ani stanu konta merchanta — odcinamy te pola dla nie-managerów.
    var isManager = User.IsInRole("Owner") || User.IsInRole("Manager") || User.IsInRole("Admin");
    if (!isManager)
    {
      result = result with { DepositSettings = null, MerchantAccount = null };
    }

    return Ok(result);
  }

  // Ustawienia biznesowe (slug = publiczny URL, strefa, waluta, StaffCalendarVisibilityPolicy =
  // zakres widoczności cudzych kalendarzy) należą do właściciela. BusinessManagement = Owner/Admin.
  [Authorize(Policy = "BusinessManagement")]
  [HttpPut]
  public async Task<ActionResult> Put([FromBody] UpdateCurrentSalonSettingsRequest request)
  {
    await Mediator.Send(
        new UpdateCurrentSalonSettingsCommand(
            request.Name,
            request.Slug,
            request.CustomerVerificationChannel,
            request.AppointmentSlotStepMinutes,
            request.TimeZoneId,
            request.Currency,
            request.BookingAccessPolicy,
            request.AppointmentConfirmationMode,
            request.GapFillingSettings,
            request.NotificationSettings,
            request.StaffCalendarVisibilityPolicy,
            request.RequireCustomerName,
            request.CollectInstagramHandle,
            request.CollectInspirationImages,
            request.DepositSettings,
            request.BookingCalendarColorHex,
            request.BookingCalendarBackgroundHex,
            request.BookingCalendarSurfaceHex,
            request.BookingCalendarPriceHex,
            request.TermsOfService,
            request.DoNotRetainAppointmentHistory,
            request.BookingHorizonDays));
    return NoContent();
  }

  /// <summary>
  /// Trwale usuwa ISTNIEJĄCĄ historię wizyt salonu (terminalne + przeszłość). Dla salonu, który
  /// zapisywał historię, a potem zdecydował się jej nie trzymać — kasuje to, co już się nazbierało.
  /// Owner only. Zwraca liczbę usuniętych wizyt.
  /// </summary>
  [Authorize(Policy = "BusinessManagement")]
  [HttpPost("purge-appointment-history")]
  public async Task<ActionResult<PurgeAppointmentHistoryResult>> PurgeAppointmentHistory(CancellationToken ct)
  {
    var result = await Mediator.Send(new PurgeAppointmentHistoryCommand(), ct);
    return Ok(result);
  }

  /// <summary>
  /// Przełącza wstrzymanie rezerwacji salonu (instant toggle). Gdy włączone — publiczne rezerwacje online są
  /// zablokowane, a panel pokazuje baner informujący personel, że rezerwacje są wstrzymane. Owner/Admin.
  /// </summary>
  [Authorize(Policy = "BusinessManagement")]
  [HttpPut("booking-pause")]
  public async Task<ActionResult> SetBookingPause([FromBody] SetBookingPauseRequest request, CancellationToken ct)
  {
    await Mediator.Send(new SetBookingPauseCommand(request.Paused, request.Message), ct);
    return NoContent();
  }

  // --- Konto płatności (zadatki) ---

  /// <summary>Rozpoczyna/ponawia onboarding konta płatności salonu (Stripe Connect) — zwraca link do przekierowania.</summary>
  [Authorize(Policy = "BusinessManagement")]
  [HttpPost("connect-merchant-account")]
  public async Task<ActionResult<ConnectMerchantAccountResult>> ConnectMerchantAccount(CancellationToken ct)
  {
    var result = await Mediator.Send(new ConnectMerchantAccountCommand(), ct);
    return Ok(result);
  }

  /// <summary>Odświeża i zwraca stan konta płatności salonu (czy gotowe do przyjmowania zadatków).</summary>
  [Authorize(Policy = "BusinessManagement")]
  [HttpGet("merchant-account-status")]
  public async Task<ActionResult<MerchantAccountStatusDto>> GetMerchantAccountStatus(CancellationToken ct)
  {
    var result = await Mediator.Send(new GetMerchantAccountStatusQuery(), ct);
    return Ok(result);
  }
}
