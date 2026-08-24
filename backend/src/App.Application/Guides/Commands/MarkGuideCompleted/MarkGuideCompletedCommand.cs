using App.Application.Common.Interfaces;
using App.Application.Common.Validation;
using App.Domain.Aggregates.UserAggregate;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Guides.Commands.MarkGuideCompleted;

/// <summary>
/// Odnotowuje ukończenie przewodnika przez użytkownika. Idempotentna: powtórne wywołanie
/// (odtworzenie przewodnika z katalogu) nie tworzy duplikatu i NIE przesuwa daty pierwszego
/// ukończenia. Dzięki temu front może wołać ją bez sprawdzania stanu.
/// </summary>
public sealed record MarkGuideCompletedCommand(Guid UserId, string GuideId) : IRequest;

public sealed class MarkGuideCompletedCommandValidator : AppValidator<MarkGuideCompletedCommand>
{
  public MarkGuideCompletedCommandValidator()
  {
    RuleFor(x => x.GuideId)
      .NotEmpty()
      .MaximumLength(GuideIdRules.MaxLength)
      .Matches(GuideIdRules.Pattern)
      .WithMessage("Identyfikator przewodnika ma nieprawidłowy format.");
  }
}

internal sealed class MarkGuideCompletedCommandHandler : IRequestHandler<MarkGuideCompletedCommand>
{
  private readonly IApplicationDbContext _context;

  public MarkGuideCompletedCommandHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task Handle(MarkGuideCompletedCommand request, CancellationToken ct)
  {
    var alreadyCompleted = await _context.UserGuideCompletions
      .AnyAsync(c => c.UserId == request.UserId && c.GuideId == request.GuideId, ct);

    if (alreadyCompleted)
    {
      return;
    }

    // Sufit na użytkownika. GuideId nie jest weryfikowany względem rejestru front-endu (celowo —
    // patrz GuideIdRules), więc to jedyna bariera przed zapełnieniem tabeli przez zalogowanego
    // klienta wołającego endpoint w pętli z generowanymi identyfikatorami.
    var completedCount = await _context.UserGuideCompletions
      .CountAsync(c => c.UserId == request.UserId, ct);

    if (completedCount >= GuideIdRules.MaxPerUser)
    {
      return;
    }

    _context.UserGuideCompletions.Add(
      UserGuideCompletion.Create(request.UserId, request.GuideId, DateTime.UtcNow));

    await _context.SaveChangesAsync(ct);
  }
}
