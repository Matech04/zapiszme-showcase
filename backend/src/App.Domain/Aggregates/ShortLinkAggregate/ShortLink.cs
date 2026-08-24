using App.Domain.Common;

namespace App.Domain.Aggregates.ShortLinkAggregate;

/// <summary>
/// Krótki link przekierowujący (np. <c>zapisz.me/p/{code}</c>) na docelowy URL — obecnie link
/// Stripe Checkout dla zadatku. Świadomie NIE jest <see cref="ITenantEntity"/>: redirect rozwiązuje
/// się anonimowo, bez kontekstu tenanta (jak webhook/status), więc nie zakładamy globalnego filtra
/// po TenantId. <see cref="TenantId"/> trzymamy jako zwykłą kolumnę (kontekst/analityka). Kod jest
/// losowy (base62) i unikalny.
/// </summary>
public class ShortLink : Entity, IAggregateRoot
{
  public string Code { get; private set; } = string.Empty;
  public string TargetUrl { get; private set; } = string.Empty;
  public string Purpose { get; private set; } = string.Empty;
  public Guid TenantId { get; private set; }
  public Guid? AppointmentId { get; private set; }
  public DateTime CreatedAtUtc { get; private set; }
  public DateTime? ExpiresAtUtc { get; private set; }

  private ShortLink() { }

  public ShortLink(
    string code,
    string targetUrl,
    string purpose,
    Guid tenantId,
    Guid? appointmentId,
    DateTime createdAtUtc,
    DateTime? expiresAtUtc)
  {
    Id = Guid.NewGuid();
    Code = Guard.NormalizeRequiredText(code, nameof(code));
    TargetUrl = Guard.NormalizeRequiredText(targetUrl, nameof(targetUrl));
    Purpose = Guard.NormalizeOptionalText(purpose);
    TenantId = tenantId;
    AppointmentId = appointmentId;
    CreatedAtUtc = createdAtUtc;
    ExpiresAtUtc = expiresAtUtc;
  }

  public bool IsExpired(DateTime nowUtc) => ExpiresAtUtc.HasValue && ExpiresAtUtc.Value < nowUtc;
}
