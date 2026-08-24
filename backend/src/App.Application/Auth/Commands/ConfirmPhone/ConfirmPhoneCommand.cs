using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Auth.Commands.ConfirmPhone;

public sealed record ConfirmPhoneCommand(Guid UserId, string Code) : IRequest;

public sealed class ConfirmPhoneCommandValidator : AbstractValidator<ConfirmPhoneCommand>
{
  public ConfirmPhoneCommandValidator()
  {
    RuleFor(x => x.UserId).NotEmpty();
    RuleFor(x => x.Code).NotEmpty().Matches(@"^\d{6}$")
      .WithMessage("Kod musi składać się z 6 cyfr.");
  }
}

internal sealed class ConfirmPhoneCommandHandler : IRequestHandler<ConfirmPhoneCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly TimeProvider _timeProvider;
  private readonly ILogger<ConfirmPhoneCommandHandler> _logger;

  public ConfirmPhoneCommandHandler(
    IApplicationDbContext context,
    TimeProvider timeProvider,
    ILogger<ConfirmPhoneCommandHandler> logger)
  {
    _context = context;
    _timeProvider = timeProvider;
    _logger = logger;
  }

  public async Task Handle(ConfirmPhoneCommand request, CancellationToken ct)
  {
    var user = await _context.Users
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
      ?? throw new NotFoundException("User", request.UserId);

    if (!user.EmailConfirmed)
    {
      throw new PhoneOtpEmailNotConfirmedException();
    }

    if (user.PhoneNumberConfirmed)
    {
      // Idempotentny no-op — UI nie musi się troszczyć.
      return;
    }

    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

    var otp = await _context.PhoneConfirmationOtps
      .Where(o => o.UserId == request.UserId && o.ConsumedAt == null)
      .OrderByDescending(o => o.CreatedAt)
      .FirstOrDefaultAsync(ct)
      ?? throw new PhoneOtpExpiredException();

    if (otp.IsExpired(nowUtc))
    {
      throw new PhoneOtpExpiredException();
    }

    if (otp.IsLocked)
    {
      throw new PhoneOtpLockedException();
    }

    var providedHash = OtpCodeHasher.Hash(request.Code);
    if (providedHash != otp.CodeHash)
    {
      otp.RegisterFailedAttempt();
      await _context.SaveChangesAsync(ct);
      var remaining = Math.Max(0, PhoneConfirmationOtp.MaxAttempts - otp.AttemptsCount);
      _logger.LogWarning(
        "PhoneOtpInvalid UserId={UserId} Remaining={Remaining} Category={Category}",
        request.UserId, remaining, "audit");
      throw new PhoneOtpInvalidException(remaining);
    }

    otp.Consume(nowUtc);
    user.PhoneNumberConfirmed = true;
    await _context.SaveChangesAsync(ct);

    _logger.LogInformation(
      "PhoneConfirmed UserId={UserId} Category={Category}", request.UserId, "audit");
  }
}

