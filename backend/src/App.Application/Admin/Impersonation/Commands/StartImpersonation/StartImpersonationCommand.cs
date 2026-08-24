using App.Application.Common.Interfaces;
using App.Domain.Aggregates.ImpersonationAggregate;
using App.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Application.Admin.Impersonation.Commands.StartImpersonation;

/// <summary>
/// Admin-only: rozpoczęcie sesji wsparcia (support impersonation) dla wskazanego tenanta.
/// NIE dziedziczy z TenantHandler — operacja platformowa, ponad kontekstem tenanta.
/// Autoryzacja roli na controllerze (<c>Policy="SystemAdminOnly"</c>); tu dodatkowy guard.
/// </summary>
public record StartImpersonationCommand(
  Guid TenantId,
  string Reason,
  bool ReadOnly,
  string? IpAddress = null) : IRequest<StartImpersonationResult>;

public record StartImpersonationResult(
  Guid SessionId,
  Guid TenantId,
  DateTime ExpiresAtUtc,
  bool IsReadOnly);

public sealed class StartImpersonationCommandValidator : AbstractValidator<StartImpersonationCommand>
{
  public StartImpersonationCommandValidator()
  {
    RuleFor(x => x.TenantId).NotEmpty();
    RuleFor(x => x.Reason)
      .NotEmpty()
      .MinimumLength(5)
      .MaximumLength(500);
  }
}

public class StartImpersonationCommandHandler : IRequestHandler<StartImpersonationCommand, StartImpersonationResult>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;
  private readonly ICurrentUserAccessor _currentUser;
  private readonly TimeProvider _timeProvider;
  private readonly ImpersonationOptions _options;

  public StartImpersonationCommandHandler(
    IApplicationDbContext context,
    IUnitOfWork uow,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    IOptions<ImpersonationOptions> options)
  {
    _context = context;
    _uow = uow;
    _currentUser = currentUser;
    _timeProvider = timeProvider;
    _options = options.Value;
  }

  public async Task<StartImpersonationResult> Handle(StartImpersonationCommand r, CancellationToken ct)
  {
    if (!_currentUser.IsSystemAdmin || _currentUser.UserId is not { } adminUserId)
    {
      throw new ForbiddenAccessException(
        "Tryb wsparcia jest dostępny tylko dla administratora platformy.",
        ErrorCodes.Forbidden);
    }

    // Tenant nie ma query filtra — admin nie ma kontekstu tenanta, więc odczyt jest bezpośredni.
    var tenant = await _context.Tenants
      .AsNoTracking()
      .FirstOrDefaultAsync(t => t.Id == r.TenantId, ct);

    if (tenant is null)
    {
      throw new NotFoundException("Tenant", r.TenantId);
    }

    if (tenant.IsDemo)
    {
      throw new ForbiddenAccessException(
        "Nie można wejść w tryb wsparcia na tenancie demo.",
        ErrorCodes.ImpersonationTenantDemo);
    }

    var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
    var session = ImpersonationSession.Start(
      adminUserId: adminUserId,
      targetTenantId: tenant.Id,
      reason: r.Reason,
      isReadOnly: r.ReadOnly,
      ttl: _options.SessionTtl,
      nowUtc: nowUtc,
      ipAddress: r.IpAddress);

    _context.ImpersonationSessions.Add(session);
    await _uow.SaveChangesAsync(ct);

    return new StartImpersonationResult(session.Id, tenant.Id, session.ExpiresAtUtc, session.IsReadOnly);
  }
}
