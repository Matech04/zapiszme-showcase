using MediatR;

namespace App.Application.Notifications.Events;

/// <summary>
/// Pracownik ręcznie wystawił klientowi wizytę w panelu (rezerwacja zrobiona przez salon,
/// a nie przez klienta online) i wizyta od razu trafiła w stan <c>Booked</c>. Publikowane po
/// zapisie z <c>CreateAppointmentHandler</c> — handler powiadomień wysyła klientowi potwierdzenie.
/// </summary>
public record StaffBookedAppointmentEvent(Guid TenantId, Guid AppointmentId) : INotification;
