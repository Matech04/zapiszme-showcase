using App.Application.Common.Interfaces;
using App.Domain.Aggregates.SelfServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.SelfService.Commands;

public record VerifySelfServiceOtpCommand(string Slug, string? Email, string? PhoneNumber, string Otp)
    : IRequest<VerifySelfServiceOtpResult>;

public record VerifySelfServiceOtpResult(Guid SessionToken, DateTime ExpiresAtUtc);

internal sealed class VerifySelfServiceOtpCommandHandler
    : IRequestHandler<VerifySelfServiceOtpCommand, VerifySelfServiceOtpResult>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;
  private readonly TimeProvider _timeProvider;

  private static readonly TimeSpan SessionLifetime = SelfServiceSessionPolicy.SessionLifetime;

  public VerifySelfServiceOtpCommandHandler(IApplicationDbContext context, IUnitOfWork uow, TimeProvider timeProvider)
  {
    _context = context;
    _uow = uow;
    _timeProvider = timeProvider;
  }

  public async Task<VerifySelfServiceOtpResult> Handle(VerifySelfServiceOtpCommand request, CancellationToken ct)
  {
    var tenant = await _context.Tenants
      .AsNoTracking()
      .FirstOrDefaultAsync(t => t.Slug == request.Slug, ct)
      ?? throw new NotFoundException(nameof(Tenant), request.Slug);

    var hasEmail = !string.IsNullOrWhiteSpace(request.Email);
    var hasPhone = !string.IsNullOrWhiteSpace(request.PhoneNumber);
    if (hasEmail == hasPhone)
    {
      throw new AppointmentBookingRuleException(
        "Podaj kontakt użyty przy żądaniu kodu.",
        ErrorCodes.AppointmentOtpMissingContact);
    }

    string? email = hasEmail ? request.Email!.Trim().ToLowerInvariant() : null;
    string? phone = hasPhone ? new PhoneNumber(request.PhoneNumber!).Value : null;

    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

    var otp = await _context.SelfServiceOtps
      .Where(o => o.TenantId == tenant.Id
        && !o.Consumed
        && o.ExpiresAtUtc > nowUtc
        && (email != null ? o.Email == email : o.PhoneE164 == phone))
      .OrderByDescending(o => o.CreatedAtUtc)
      .FirstOrDefaultAsync(ct)
      ?? throw new AppointmentBookingRuleException(
        "Nie znaleziono aktywnego kodu — poproś o nowy.",
        ErrorCodes.AppointmentOtpVerificationRequired);

    if (otp.IsBlocked)
    {
      throw new AppointmentBookingRuleException(
        "Wykorzystano 3 nieudane próby — poproś o nowy kod.",
        ErrorCodes.AppointmentOtpTooManyFailures);
    }

    var hash = SelfServiceCodeHasher.Hash(request.Otp.Trim());
    if (!otp.TryConsume(hash, nowUtc, SessionLifetime))
    {
      await _uow.SaveChangesAsync(ct);
      var fails = otp.FailedAttempts;
      if (fails >= 3)
      {
        throw new AppointmentBookingRuleException(
          "3 razy podano błędny lub wygasły kod. Poproś o nowy kod OTP.",
          ErrorCodes.AppointmentOtpTooManyFailures);
      }

      throw new AppointmentBookingRuleException(
        $"Nieprawidłowy lub wygasły kod OTP. Pozostało prób: {3 - fails}.",
        ErrorCodes.AppointmentOtpInvalidCode);
    }

    await _uow.SaveChangesAsync(ct);
    return new VerifySelfServiceOtpResult(otp.SessionToken!.Value, otp.SessionExpiresAtUtc!.Value);
  }
}
