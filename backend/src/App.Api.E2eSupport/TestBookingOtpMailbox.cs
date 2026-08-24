using App.Application.Booking;
using App.Application.Common.Email;

namespace App.Api.E2eSupport;

/// <summary>Przechwytuje wysłane kody OTP w testach integracyjnych (bez Azure).</summary>
public sealed class TestBookingOtpMailbox : IBookingOtpEmailSender
{
  private readonly List<(string ToEmail, string Code, string SalonName)> _allSent = new();
  private readonly object _gate = new();

  public string? LastToEmail { get; private set; }
  public string? LastCode { get; private set; }
  public string? LastSalonName { get; private set; }

  /// <summary>Wszystkie wysłane OTP-y — używane przez testy anti-email-bomb.</summary>
  public IReadOnlyList<(string ToEmail, string Code, string SalonName)> AllSent
  {
    get { lock (_gate) { return _allSent.ToArray(); } }
  }

  public Task SendOtpCodeAsync(
      string toEmail,
      string sixDigitCode,
      EmailBrand brand,
      CancellationToken cancellationToken = default)
  {
    lock (_gate) { _allSent.Add((toEmail, sixDigitCode, brand.Name)); }
    LastToEmail = toEmail;
    LastCode = sixDigitCode;
    LastSalonName = brand.Name;
    return Task.CompletedTask;
  }
}
