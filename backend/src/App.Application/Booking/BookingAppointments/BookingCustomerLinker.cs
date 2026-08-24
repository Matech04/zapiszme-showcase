using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;

namespace App.Application.Booking.BookingAppointments;

/// <summary>
/// Po potwierdzeniu publicznej rezerwacji gość staje się klientem CRM tenanta: wyszukujemy
/// istniejącego po telefonie/emailu, a gdy go nie ma — tworzymy stub-Customera (Source = PublicBooking).
/// Wydzielone z <c>VerifyOtpCommand</c>, by ścieżka „verify OTP” i ścieżka „confirm-with-session”
/// tworzyły/wzbogacały klienta identycznie.
/// </summary>
internal static class BookingCustomerLinker
{
  public static async Task LinkCustomerAsync(
    Appointment appointment,
    ICustomerRepository customers,
    OtpVerificationChannel channel,
    string? email,
    string? phoneE164,
    string? firstName,
    string? lastName,
    string? instagramNick,
    CancellationToken ct)
  {
    if (appointment.CustomerId is not null)
    {
      return;
    }

    Customer? existing = null;
    PhoneNumber? phone = null;

    if (channel == OtpVerificationChannel.Phone && !string.IsNullOrWhiteSpace(phoneE164))
    {
      phone = new PhoneNumber(phoneE164);
      existing = await customers.GetByPhoneNumber(appointment.TenantId, phone, ct);
    }
    else if (channel == OtpVerificationChannel.Email && !string.IsNullOrWhiteSpace(email))
    {
      existing = await customers.GetByEmail(appointment.TenantId, email!, ct);
    }

    var emailForCustomer = channel == OtpVerificationChannel.Email ? email : null;

    if (existing is null)
    {
      var created = Customer.CreateFromPublicBooking(
        appointment.TenantId,
        emailForCustomer,
        phone,
        firstName,
        lastName,
        instagramNick);
      await customers.AddAsync(created);
      appointment.AssignCustomer(created.Id);
    }
    else
    {
      existing.EnrichContactFromPublicBooking(
        emailForCustomer,
        phone,
        firstName,
        lastName,
        instagramNick);
      appointment.AssignCustomer(existing.Id);
    }
  }
}
