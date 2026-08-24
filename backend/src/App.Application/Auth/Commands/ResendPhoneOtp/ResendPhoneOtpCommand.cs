using App.Application.Auth.Commands.SendPhoneOtp;
using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace App.Application.Auth.Commands.ResendPhoneOtp;

public sealed record ResendPhoneOtpCommand(Guid UserId) : IRequest;

public sealed class ResendPhoneOtpCommandValidator : AbstractValidator<ResendPhoneOtpCommand>
{
  public ResendPhoneOtpCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

internal sealed class ResendPhoneOtpCommandHandler : IRequestHandler<ResendPhoneOtpCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IMediator _mediator;
  private readonly TimeProvider _timeProvider;
  private readonly TimeSpan _cooldown;

  public ResendPhoneOtpCommandHandler(
    IApplicationDbContext context,
    IMediator mediator,
    TimeProvider timeProvider,
    IConfiguration configuration)
  {
    _context = context;
    _mediator = mediator;
    _timeProvider = timeProvider;
    var seconds = configuration.GetValue<double?>("Auth:PhoneOtpResendCooldownSeconds") ?? 60d;
    _cooldown = TimeSpan.FromSeconds(seconds);
  }

  public async Task Handle(ResendPhoneOtpCommand request, CancellationToken ct)
  {
    var user = await _context.Users
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(u => u.Id == request.UserId, ct)
      ?? throw new NotFoundException("User", request.UserId);

    if (!user.EmailConfirmed)
    {
      // Anti-spam: blokujemy resend SMS dopóki email nieptwierdzony — patrz plan (wariant C).
      throw new PhoneOtpEmailNotConfirmedException();
    }

    if (user.PhoneNumberConfirmed)
    {
      throw new PhoneAlreadyConfirmedException();
    }

    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
    var lastOtp = await _context.PhoneConfirmationOtps
      .Where(o => o.UserId == request.UserId)
      .OrderByDescending(o => o.CreatedAt)
      .FirstOrDefaultAsync(ct);

    if (lastOtp is not null)
    {
      var elapsed = nowUtc - lastOtp.CreatedAt;
      if (elapsed < _cooldown)
      {
        var retryAfter = (int)Math.Ceiling((_cooldown - elapsed).TotalSeconds);
        throw new PhoneOtpCooldownException(retryAfter);
      }
    }

    await _mediator.Send(new SendPhoneOtpCommand(request.UserId), ct);
  }
}
