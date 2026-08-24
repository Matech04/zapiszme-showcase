using App.Application.Common.Interfaces;
using App.Domain.Aggregates.SelfServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Booking.SelfService;

internal record SelfServiceSession(Guid TenantId, string? Email, string? PhoneE164);

internal static class SelfServiceSessionResolver
{
  public static async Task<SelfServiceSession> ResolveAsync(
    IApplicationDbContext ctx,
    string slug,
    Guid sessionToken,
    TimeProvider timeProvider,
    CancellationToken ct)
  {
    var tenant = await ctx.Tenants
      .AsNoTracking()
      .FirstOrDefaultAsync(t => t.Slug == slug, ct)
      ?? throw new NotFoundException(nameof(Tenant), slug);

    var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
    var otp = await ctx.SelfServiceOtps
      .AsNoTracking()
      .FirstOrDefaultAsync(
        o => o.TenantId == tenant.Id
          && o.SessionToken == sessionToken
          && o.Consumed
          && o.SessionExpiresAtUtc > nowUtc,
        ct)
      ?? throw new ForbiddenAccessException(
        "Sesja klienta wygasła. Zaloguj się ponownie kodem OTP.",
        ErrorCodes.AppointmentOtpInvalidLease);

    return new SelfServiceSession(tenant.Id, otp.Email, otp.PhoneE164);
  }
}
