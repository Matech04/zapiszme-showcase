using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Guides.Queries.GetGuideCompletions;

/// <summary>
/// Identyfikatory przewodników ukończonych przez danego użytkownika — katalog
/// <c>/admin/guides</c> odhacza na ich podstawie kafelki.
///
/// Zwykły <c>IRequestHandler</c>, nie <c>TenantHandler</c>: postęp jest własnością osoby,
/// nie salonu, i musi działać także dla konta bez tenanta (admin systemowy, właściciel przed
/// utworzeniem salonu). Izolację daje filtr po <paramref name="UserId"/>, który pochodzi
/// z claima — kontroler nie przyjmuje go z żądania.
/// </summary>
public sealed record GetGuideCompletionsQuery(Guid UserId) : IRequest<IReadOnlyList<string>>;

internal sealed class GetGuideCompletionsQueryHandler
  : IRequestHandler<GetGuideCompletionsQuery, IReadOnlyList<string>>
{
  private readonly IApplicationDbContext _context;

  public GetGuideCompletionsQueryHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<IReadOnlyList<string>> Handle(
    GetGuideCompletionsQuery request, CancellationToken ct)
  {
    return await _context.UserGuideCompletions
      .Where(c => c.UserId == request.UserId)
      .OrderBy(c => c.CompletedAtUtc)
      .Select(c => c.GuideId)
      .ToListAsync(ct);
  }
}
