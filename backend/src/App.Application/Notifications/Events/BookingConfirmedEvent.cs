using MediatR;

namespace App.Application.Notifications.Events;

/// <summary>
/// Rezerwacja publiczna została potwierdzona (klient zweryfikował OTP, wizyta przeszła w Booked/Pending).
/// Publikowane po zapisie z <c>VerifyOtpCommandHandler</c>.
/// </summary>
public record BookingConfirmedEvent(Guid TenantId, Guid AppointmentId) : INotification;
