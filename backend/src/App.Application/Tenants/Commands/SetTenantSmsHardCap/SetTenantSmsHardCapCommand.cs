using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Tenants.Commands.SetTenantSmsHardCap;

/// <summary>
/// Admin-only: ustaw/zdejmij twardy miesięczny limit SMS salonu (anti-abuse kill-switch).
/// <c>HardCap == null</c> → wróć do limitu z planu (<c>MonthlySmsAllowance</c>).
/// </summary>
public record SetTenantSmsHardCapCommand(Guid TenantId, int? HardCap) : IRequest;

public class SetTenantSmsHardCapCommandHandler : IRequestHandler<SetTenantSmsHardCapCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IUnitOfWork _uow;

  public SetTenantSmsHardCapCommandHandler(IApplicationDbContext context, IUnitOfWork uow)
  {
    _context = context;
    _uow = uow;
  }

  public async Task Handle(SetTenantSmsHardCapCommand request, CancellationToken ct)
  {
    var tenant = await _context.Tenants
        .FirstOrDefaultAsync(t => t.Id == request.TenantId, ct)
        ?? throw new NotFoundException(nameof(Tenant), request.TenantId);

    tenant.Subscription.SetMonthlySmsHardCap(request.HardCap);

    await _uow.SaveChangesAsync(ct);
  }
}

public class SetTenantSmsHardCapCommandValidator : AbstractValidator<SetTenantSmsHardCapCommand>
{
  public SetTenantSmsHardCapCommandValidator()
  {
    When(c => c.HardCap is not null, () =>
      RuleFor(c => c.HardCap!.Value)
        .GreaterThanOrEqualTo(0).WithMessage("Limit SMS nie może być ujemny.")
        .LessThanOrEqualTo(1_000_000).WithMessage("Limit SMS jest nierealnie wysoki."));
  }
}
