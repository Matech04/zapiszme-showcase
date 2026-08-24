using App.Application.Common;
using App.Application.Common.Interfaces;
using MediatR;

namespace App.Application.SalonSettings.Commands.PurgeAppointmentHistory;

/// <summary>
/// Na żądanie ownera trwale usuwa ISTNIEJĄCĄ historię wizyt bieżącego salonu (terminalne + przeszłość).
/// Scenariusz: salon zapisywał historię, a potem zdecydował się jej nie trzymać — to kasuje to, co już
/// się nazbierało (job w tle czyści dopiero kolejne). Nie wymaga włączonej flagi — owner inicjuje świadomie.
/// </summary>
public record PurgeAppointmentHistoryCommand : IRequest<PurgeAppointmentHistoryResult>;

public record PurgeAppointmentHistoryResult(int DeletedCount);

internal class PurgeAppointmentHistoryCommandHandler
    : TenantHandler<PurgeAppointmentHistoryCommand, PurgeAppointmentHistoryResult>
{
  private readonly IAppointmentHistoryPurger _purger;

  public PurgeAppointmentHistoryCommandHandler(
    ICurrentTenantService currentTenantService,
    IAppointmentHistoryPurger purger)
    : base(currentTenantService)
  {
    _purger = purger;
  }

  public override async Task<PurgeAppointmentHistoryResult> Handle(
    PurgeAppointmentHistoryCommand request,
    CancellationToken ct)
  {
    var deleted = await _purger.PurgePastHistoryAsync(TenantId, ct);
    return new PurgeAppointmentHistoryResult(deleted);
  }
}
