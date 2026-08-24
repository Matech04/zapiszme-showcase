using App.Domain.Common;

namespace App.Domain.Aggregates.ImpersonationAggregate;

/// <summary>
/// Sesja wsparcia ("support impersonation") — platformowy admin uzyskuje czasowy,
/// audytowany dostęp do tenanta klienta. NIE implementuje <c>ITenantEntity</c> i NIE
/// ma query filtra (świadomy wyjątek od reguły izolacji tenantów, jak <c>PromoCode</c>): to dane
/// rozliczalności dostępu supportu, nie dane salonu.
///
/// Encja jest źródłem prawdy dla całego mechanizmu — middleware sprawdza ją przy każdym
/// żądaniu, dzięki czemu zakończenie/wygaśnięcie sesji działa natychmiast (cookie to tylko
/// podpisany wskaźnik na to id). Sesje nigdy nie są kasowane — stanowią dziennik audytu.
/// </summary>
public class ImpersonationSession : Entity, IAggregateRoot
{
  /// <summary>Identity user id platformowego admina, który uruchomił sesję.</summary>
  public Guid AdminUserId { get; private set; }

  /// <summary>Tenant, do którego admin uzyskuje dostęp w trybie support.</summary>
  public Guid TargetTenantId { get; private set; }

  /// <summary>Powód wejścia — wymagany, ląduje w audycie.</summary>
  public string Reason { get; private set; } = string.Empty;

  /// <summary>Gdy true, mutacje (nie-GET) są blokowane na czas sesji.</summary>
  public bool IsReadOnly { get; private set; }

  public DateTime StartedAtUtc { get; private set; }
  public DateTime ExpiresAtUtc { get; private set; }

  /// <summary>Ustawiane przy jawnym zakończeniu sesji. null = sesja nie została zakończona ręcznie.</summary>
  public DateTime? EndedAtUtc { get; private set; }

  /// <summary>IP, z którego uruchomiono sesję (na potrzeby audytu).</summary>
  public string? IpAddress { get; private set; }

  private ImpersonationSession() { }

  public static ImpersonationSession Start(
    Guid adminUserId,
    Guid targetTenantId,
    string reason,
    bool isReadOnly,
    TimeSpan ttl,
    DateTime nowUtc,
    string? ipAddress = null)
  {
    if (adminUserId == Guid.Empty)
    {
      throw new ArgumentException("AdminUserId jest wymagane.", nameof(adminUserId));
    }
    if (targetTenantId == Guid.Empty)
    {
      throw new ArgumentException("TargetTenantId jest wymagane.", nameof(targetTenantId));
    }

    var trimmed = (reason ?? string.Empty).Trim();
    if (trimmed.Length is < 5 or > 500)
    {
      throw new ArgumentOutOfRangeException(nameof(reason), "Powód musi mieć 5–500 znaków.");
    }
    if (ttl <= TimeSpan.Zero)
    {
      throw new ArgumentOutOfRangeException(nameof(ttl), "TTL musi być dodatnie.");
    }

    var startedAt = nowUtc.ToUniversalTime();
    return new ImpersonationSession
    {
      Id = Guid.NewGuid(),
      AdminUserId = adminUserId,
      TargetTenantId = targetTenantId,
      Reason = trimmed,
      IsReadOnly = isReadOnly,
      StartedAtUtc = startedAt,
      ExpiresAtUtc = startedAt.Add(ttl),
      EndedAtUtc = null,
      IpAddress = ipAddress,
    };
  }

  /// <summary>Sesja jest aktywna, gdy nie została zakończona ręcznie i nie wygasła.</summary>
  public bool IsActive(DateTime nowUtc) =>
    EndedAtUtc is null && nowUtc.ToUniversalTime() < ExpiresAtUtc;

  /// <summary>Idempotentne zakończenie sesji.</summary>
  public void End(DateTime nowUtc)
  {
    EndedAtUtc ??= nowUtc.ToUniversalTime();
  }

  /// <summary>Pozostały czas sesji (≥ 0) — dla banera w dashboardzie.</summary>
  public TimeSpan RemainingTime(DateTime nowUtc)
  {
    var remaining = ExpiresAtUtc - nowUtc.ToUniversalTime();
    return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
  }
}
