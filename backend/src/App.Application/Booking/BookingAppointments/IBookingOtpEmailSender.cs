using App.Application.Common.Email;

namespace App.Application.Booking;

public interface IBookingOtpEmailSender
{
  /// <param name="brand">Marka salonu (nazwa + kolor akcentu) — mail idzie do jego klienta.</param>
  Task SendOtpCodeAsync(
    string toEmail,
    string sixDigitCode,
    EmailBrand brand,
    CancellationToken cancellationToken = default);
}
