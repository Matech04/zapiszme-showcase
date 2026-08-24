using System.Text.Json;
using System.Text.Json.Serialization;
using App.Domain.Aggregates.PromoCodeAggregate;

namespace App.Application.Subscription.PromoPricing;

/// <summary>
/// Zaserializowany snapshot warunków rabatu na moment redempcji. Trzymany jako jsonb
/// w <c>promo_code_redemptions.discount_snapshot</c>. Późniejsze zmiany na <c>PromoCode</c>
/// (np. zmiana procentu zniżki) NIE wpływają na istniejące redemptions.
/// </summary>
public sealed record DiscountSnapshot(
  [property: JsonPropertyName("type")] PromoDiscountType Type,
  [property: JsonPropertyName("value")] decimal Value,
  [property: JsonPropertyName("durationMonths")] int? DurationMonths)
{
  private static readonly JsonSerializerOptions Options = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() },
  };

  public string Serialize() => JsonSerializer.Serialize(this, Options);

  public static DiscountSnapshot FromCode(PromoCode code) =>
    new(code.DiscountType, code.DiscountValue, code.DurationMonths);

  public static DiscountSnapshot? TryParse(string? json)
  {
    if (string.IsNullOrWhiteSpace(json)) return null;
    try { return JsonSerializer.Deserialize<DiscountSnapshot>(json, Options); }
    catch (JsonException) { return null; }
  }
}
