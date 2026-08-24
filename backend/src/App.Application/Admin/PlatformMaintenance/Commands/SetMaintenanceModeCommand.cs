using App.Application.Common.Interfaces;
using App.Domain.Aggregates.GlobalSettingsAggregate;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.PlatformMaintenance.Commands;

/// <summary>
/// Admin-only: włącza/wyłącza globalny tryb serwisowy platformy (kill-switch). Gdy włączony,
/// <c>PlatformMaintenanceMiddleware</c> blokuje wszystkie operacje zapisu poza adminem platformy.
/// NIE dziedziczy z TenantHandler — GlobalSettings jest globalny. Authorization na controllerze
/// (SystemAdminOnly). Po zapisie inwaliduje cache stanu, żeby przełącznik działał natychmiast.
/// </summary>
public record SetMaintenanceModeRequest(bool Enabled, string? Message = null);

public record SetMaintenanceModeCommand(bool Enabled, string? Message = null) : IRequest;

public class SetMaintenanceModeCommandValidator : AbstractValidator<SetMaintenanceModeCommand>
{
  public SetMaintenanceModeCommandValidator()
  {
    RuleFor(x => x.Message)
      .MaximumLength(GlobalSettings.MaintenanceMessageMaxLength)
      .WithMessage($"Komunikat trybu serwisowego może mieć maksymalnie {GlobalSettings.MaintenanceMessageMaxLength} znaków.");
  }
}

public sealed class SetMaintenanceModeCommandHandler : IRequestHandler<SetMaintenanceModeCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IPlatformMaintenanceState _state;

  public SetMaintenanceModeCommandHandler(IApplicationDbContext context, IPlatformMaintenanceState state)
  {
    _context = context;
    _state = state;
  }

  public async Task Handle(SetMaintenanceModeCommand request, CancellationToken ct)
  {
    var settings = await _context.GlobalSettings.FirstOrDefaultAsync(ct);
    if (settings is null)
    {
      settings = GlobalSettings.CreateDefault();
      _context.GlobalSettings.Add(settings);
    }

    settings.SetMaintenance(request.Enabled, request.Message, DateTime.UtcNow);
    await _context.SaveChangesAsync(ct);

    _state.Invalidate();
  }
}
