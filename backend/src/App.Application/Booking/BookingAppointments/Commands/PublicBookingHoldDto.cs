namespace App.Application.Booking.BookingAppointments.Commands;

/// <summary>Odpowiedź tworzenia publicznego holdu — id wizyty (Pending) + dzierżawa slotu.</summary>
public record PublicBookingHoldDto(Guid AppointmentId, HoldLease Lease);
