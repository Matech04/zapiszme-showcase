using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.Impersonation.Commands.EndImpersonation;

/// <summary>
/// Admin-only: zakończenie sesji wsparcia wskazanej przez cookie (id przekazuje controller).
/// Idempotentne — brak sesji lub już zakończona to no-op (zwraca false).
/// </summary>
public record EndImpersonationCommand(Guid SessionId) : IRequest<bool>;

public class EndImpersonationCommandHandler : IRequestHandler<EndImpersonationCommand, bool>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;
  private readonly TimeProvider _timeProvider;

  public EndImpersonationCommandHandler(
    IApplicationDbContext context,
    IUnitOfWork uow,
    TimeProvider timeProvider)
  {
    _context = context;
    _uow = uow;
    _timeProvider = timeProvider;
  }

  public async Task<bool> Handle(EndImpersonationCommand r, CancellationToken ct)
  {
    var session = await _context.ImpersonationSessions
      .FirstOrDefaultAsync(s => s.Id == r.SessionId, ct);

    if (session is null || session.EndedAtUtc is not null)
    {
      return false;
    }

    session.End(_timeProvider.GetUtcNow().UtcDateTime);
    await _uow.SaveChangesAsync(ct);
    return true;
  }
}
