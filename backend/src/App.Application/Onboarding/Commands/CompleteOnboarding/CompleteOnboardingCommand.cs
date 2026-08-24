using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Onboarding.Commands.CompleteOnboarding;

/// <summary>
/// Ostatni krok kreatora („Jak przyjmujesz zapisy? / Twój link jest gotowy"). Opcjonalnie ustawia
/// tryb potwierdzania wizyt i oznacza onboarding jako ukończony (idempotentnie). Zwraca slug salonu,
/// żeby front mógł od razu pokazać publiczny link rezerwacji.
/// </summary>
public sealed record CompleteOnboardingCommand(
  AppointmentConfirmationMode? ConfirmationMode) : IRequest<CompleteOnboardingResult>;

public sealed record CompleteOnboardingResult(Guid TenantId, string Slug);

internal sealed class CompleteOnboardingCommandHandler
  : TenantHandler<CompleteOnboardingCommand, CompleteOnboardingResult>
{
  private readonly IApplicationDbContext _context;

  public CompleteOnboardingCommandHandler(
    IApplicationDbContext context,
    ICurrentTenantService currentTenantService)
    : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<CompleteOnboardingResult> Handle(
    CompleteOnboardingCommand request, CancellationToken ct)
  {
    var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == TenantId, ct)
      ?? throw new NotFoundException(nameof(Tenant), TenantId);

    if (request.ConfirmationMode.HasValue)
    {
      tenant.Update(
        tenant.Name,
        tenant.Slug,
        appointmentConfirmationMode: request.ConfirmationMode.Value);
    }

    tenant.MarkOnboardingCompleted(DateTime.UtcNow);
    await _context.SaveChangesAsync(ct);

    return new CompleteOnboardingResult(tenant.Id, tenant.Slug);
  }
}
