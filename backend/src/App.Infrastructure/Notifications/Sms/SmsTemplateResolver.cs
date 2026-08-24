using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Application.Notifications.Sms;
using App.Domain.Aggregates.SmsTemplateAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Notifications.Sms;

/// <summary>
/// Rozwiązuje ZATWIERDZONY custom szablon SMS dla (tenant, typ), renderuje placeholdery i stosuje
/// sanityzację kosztową <see cref="SmsTextBuilder.SanitizeUnicode"/> — custom treść ZACHOWUJE polskie
/// znaki i emoji (świadomy wybór salonu), z limitem wg kodowania (UCS-2 → 70, łacina → 140).
///
/// Lookup jest po JAWNYM tenantId (z <c>NotificationMessage.TenantId</c>), więc działa też w tle
/// (przypomnienia lecą cross-tenant, bez kontekstu <c>ICurrentTenantService</c>). Z tego powodu
/// świadomie używamy <c>IgnoreQueryFilters</c> z ręcznym <c>Where(TenantId == tenantId)</c> —
/// izolacja tenantów jest tu zachowana przez jawny filtr, a nie ambient context.
/// </summary>
public sealed class SmsTemplateResolver : ISmsTemplateResolver
{
  private readonly IApplicationDbContext _context;

  public SmsTemplateResolver(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<string?> TryResolveAsync(
    Guid tenantId,
    NotificationType type,
    NotificationPayload payload,
    CancellationToken ct)
  {
    // Tylko typy klienckie są personalizowalne — dla *ToSalon / OTP nigdy nie czytamy szablonu.
    if (!SmsTemplatePlaceholders.IsCustomizable(type))
    {
      return null;
    }

    // Gate na Body != null (a NIE na Status): Body jest ustawiane wyłącznie przez Approve i nie jest
    // czyszczone przy ponownym zgłoszeniu/odrzuceniu, więc ostatnia ZATWIERDZONA treść pozostaje
    // aktywna nawet gdy nowa edycja czeka na akceptację albo została odrzucona.
    var body = await _context.Set<SmsTemplate>()
      .AsNoTracking()
      .IgnoreQueryFilters()
      .Where(t => t.TenantId == tenantId
                  && t.NotificationType == type
                  && t.Body != null)
      .Select(t => t.Body)
      .FirstOrDefaultAsync(ct);

    if (string.IsNullOrWhiteSpace(body))
    {
      return null;
    }

    var rendered = SmsTemplateRenderer.Render(body!, payload);
    var sanitized = SmsTextBuilder.SanitizeUnicode(rendered);

    // Po renderze/sanityzacji treść mogłaby zostać pusta (np. szablon złożony tylko z pustych
    // placeholderów) — wtedy fallback do hardcode zamiast wysłać pusty SMS.
    return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
  }
}
