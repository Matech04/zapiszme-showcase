using MediatR;

namespace App.Application.Notifications.Events;

/// <summary>
/// Rezerwacja publiczna przeszła OTP, ale salon ma tryb potwierdzania Manual — wizyta jest
/// w stanie Pending i czeka na ręczne potwierdzenie. Publikowane z <c>VerifyOtpCommandHandler</c>.
/// </summary>
public record BookingAwaitingConfirmationEvent(Guid TenantId, Guid AppointmentId) : INotification;
