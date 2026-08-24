namespace App.Domain.Aggregates.TenantAggregate;

/// <summary>
/// Tryb potwierdzania wizyt umówionych przez publiczną rezerwację online.
/// </summary>
public enum AppointmentConfirmationMode
{
  /// <summary>Wizyta jest potwierdzona automatycznie po weryfikacji OTP.</summary>
  Automatic = 0,
  /// <summary>Wizyta wymaga ręcznego zatwierdzenia przez pracownika lub managera.</summary>
  Manual = 1,
}
