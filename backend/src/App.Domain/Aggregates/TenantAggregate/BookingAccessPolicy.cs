namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>
/// Polityka dostępu do publicznej rezerwacji online.
/// </summary>
public enum BookingAccessPolicy
{
  /// <summary>Każdy może zarezerwować wizytę.</summary>
  Open = 0,
  /// <summary>Tylko klienci oznaczeni `IsWhitelisted=true` mogą zarezerwować.</summary>
  InviteOnly = 1,
}
