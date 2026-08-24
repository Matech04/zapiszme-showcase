using App.Application.Common.Interfaces;
using App.Domain.Aggregates.PromoCodeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.PromoCodes.Queries.ValidatePromoCode;

/// <summary>
/// Anonimowa walidacja kodu promocyjnego. Wywoływana z public booking flow przy rejestracji
/// (real-time feedback w UI). NIE inkrementuje <c>CurrentUses</c>. Zwraca generic message
/// dla klienta — szczegółowy powód idzie tylko do logów / admina.
/// </summary>
public record ValidatePromoCodeQuery(string Code) : IRequest<ValidatePromoCodeResult>;

public record ValidatePromoCodeResult(
  bool IsValid,
  /// <summary>Generic user-facing message. Dla invalid: zawsze "Kod nieprawidłowy".</summary>
  string? Message,
  /// <summary>Krótki opis rabatu dla klienta — pokazany przy success. Np. "Cena bazy: 49 zł zamiast 79 zł".</summary>
  string? DiscountPreview);

public class ValidatePromoCodeQueryHandler : IRequestHandler<ValidatePromoCodeQuery, ValidatePromoCodeResult>
{
  private readonly IApplicationDbContext _context;

  public ValidatePromoCodeQueryHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<ValidatePromoCodeResult> Handle(ValidatePromoCodeQuery request, CancellationToken ct)
  {
    var normalized = PromoCode.NormalizeCode(request.Code ?? string.Empty);
    if (string.IsNullOrEmpty(normalized))
    {
      return new ValidatePromoCodeResult(false, "Kod nieprawidłowy.", null);
    }

    var code = await _context.PromoCodes
      .AsNoTracking()
      .FirstOrDefaultAsync(p => p.Code == normalized, ct);

    if (code is null)
    {
      return new ValidatePromoCodeResult(false, "Kod nieprawidłowy.", null);
    }

    // Walidacja jak przy rejestracji nowego tenant'a (najczęstszy przypadek z public flow).
    var verdict = code.CanBeRedeemed(
      now: DateTime.UtcNow,
      redemptionsByThisTenant: 0,
      tenantStatus: SubscriptionStatus.Trial,
      isNewTenantRegistration: true);

    if (!verdict.IsAllowed)
    {
      return new ValidatePromoCodeResult(false, "Kod nieprawidłowy.", null);
    }

    return new ValidatePromoCodeResult(true, null, BuildPreview(code));
  }

  private static string BuildPreview(PromoCode code) => code.DiscountType switch
  {
    PromoDiscountType.PriceOverride =>
      $"Cena bazowa: {code.DiscountValue:0.##} zł / mies. zamiast 79 zł.",
    PromoDiscountType.PercentOff when code.DurationMonths is { } months =>
      $"Rabat {code.DiscountValue:0.##}% na cały rachunek przez {months} {MonthLabel(months)}.",
    PromoDiscountType.PercentOff =>
      $"Rabat {code.DiscountValue:0.##}% na cały rachunek.",
    PromoDiscountType.FreeMonths =>
      $"{(int)code.DiscountValue} {MonthLabel((int)code.DiscountValue)} gratis.",
    PromoDiscountType.TrialExtension =>
      $"Dodatkowe {(int)code.DiscountValue} dni trialu.",
    _ => "Rabat aktywny.",
  };

  private static string MonthLabel(int n)
  {
    if (n == 1) return "miesiąc";
    var lastTwo = n % 100;
    var last = n % 10;
    if (lastTwo is >= 12 and <= 14) return "miesięcy";
    if (last is >= 2 and <= 4) return "miesiące";
    return "miesięcy";
  }
}
