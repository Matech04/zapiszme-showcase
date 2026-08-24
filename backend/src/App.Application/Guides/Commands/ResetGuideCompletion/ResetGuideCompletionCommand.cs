using App.Application.Common.Interfaces;
using App.Application.Common.Validation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Guides.Commands.ResetGuideCompletion;

/// <summary>
/// Cofa oznaczenie „ukończono" (akcja „zresetuj postęp" w katalogu przewodników).
/// Idempotentna — brak wiersza nie jest błędem.
///
/// To hard-delete, nie soft-delete: <c>UserGuideCompletion</c> nie implementuje
/// <c>ISoftDelete</c>, bo znacznik postępu nie jest historią, do której cokolwiek się odwołuje.
/// </summary>
public sealed record ResetGuideCompletionCommand(Guid UserId, string GuideId) : IRequest;

public sealed class ResetGuideCompletionCommandValidator : AppValidator<ResetGuideCompletionCommand>
{
  public ResetGuideCompletionCommandValidator()
  {
    RuleFor(x => x.GuideId)
      .NotEmpty()
      .MaximumLength(GuideIdRules.MaxLength)
      .Matches(GuideIdRules.Pattern)
      .WithMessage("Identyfikator przewodnika ma nieprawidłowy format.");
  }
}

internal sealed class ResetGuideCompletionCommandHandler : IRequestHandler<ResetGuideCompletionCommand>
{
  private readonly IApplicationDbContext _context;

  public ResetGuideCompletionCommandHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task Handle(ResetGuideCompletionCommand request, CancellationToken ct)
  {
    var completion = await _context.UserGuideCompletions
      .FirstOrDefaultAsync(c => c.UserId == request.UserId && c.GuideId == request.GuideId, ct);

    if (completion == null)
    {
      return;
    }

    _context.UserGuideCompletions.Remove(completion);
    await _context.SaveChangesAsync(ct);
  }
}
