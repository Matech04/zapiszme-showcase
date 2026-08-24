using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Common;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Appointments.Commands.UpdateAppointmentStatus;

public record UpdateAppointmentStatusCommand(
  Guid AppointmentId,
  int StatusId) : IRequest<Guid>, IAppointmentWriteRequest;

public class UpdateAppointmentStatusHandler : TenantHandler<UpdateAppointmentStatusCommand, Guid>
{
  private readonly IAppointmentRepository _repository;
  private readonly IStaffAccessPolicy _access;
  private readonly IUnitOfWork _uow;
  private readonly IPublisher _publisher;
  private readonly ILogger<UpdateAppointmentStatusHandler> _logger;
  private readonly IAppointmentInspirationCleanup _inspirationCleanup;

  public UpdateAppointmentStatusHandler(
    IAppointmentRepository repository,
    IUnitOfWork uow,
    IStaffAccessPolicy access,
    ICurrentTenantService currentTenantService,
    IPublisher publisher,
    ILogger<UpdateAppointmentStatusHandler> logger,
    IAppointmentInspirationCleanup inspirationCleanup
    )
      : base(currentTenantService)
  {
    _access = access;
    _repository = repository;
    _uow = uow;
    _publisher = publisher;
    _logger = logger;
    _inspirationCleanup = inspirationCleanup;
  }

  public override async Task<Guid> Handle(UpdateAppointmentStatusCommand request, CancellationToken ct)
  {
    var appointment = await _repository.GetByIdAsync(request.AppointmentId)
        ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    // Tenant Safety Check
    if (appointment.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    // Autoryzacja właściciela kalendarza (rola + StaffCalendarVisibilityPolicy salonu). Wizytę mamy
    // już w ręku, więc pytamy o pracownika, zamiast dociągać ją drugi raz przez policy.
    await _access.EnsureCanMutateEmployeeCalendarAsync(appointment.EmployeeId, ct);

    var newStatus = AppointmentStatus.FromId(request.StatusId);

    if (newStatus == AppointmentStatus.Canceled && appointment.Status == AppointmentStatus.Completed)
    {
      throw new AppointmentBookingRuleException(
        "Nie można anulować wizyty, która została już zakończona.");
    }

    var previousStatus = appointment.Status;
    appointment.ChangeStatus(newStatus);

    // Wizyta terminalna (Completed/Canceled) — podgląd inspiracji zbędny: zdejmij rekordy (skasują się
    // w tym samym zapisie) i po commit skasuj obiekty z R2 (best-effort).
    var inspirationKeys = newStatus.IsTerminal
        ? _inspirationCleanup.DetachImages(appointment)
        : Array.Empty<string>();

    await _uow.SaveChangesAsync(ct);

    await _inspirationCleanup.PurgeStorageAsync(inspirationKeys, ct);

    await PublishStatusNotificationAsync(appointment, previousStatus, newStatus, ct);

    return appointment.Id;
  }

  /// <summary>
  /// Best-effort: salon potwierdził (Pending → Booked) albo odwołał wizytę (→ Canceled).
  /// Błąd publikacji nie może wywrócić zmiany statusu.
  /// </summary>
  private async Task PublishStatusNotificationAsync(
    Appointment appointment,
    AppointmentStatus previousStatus,
    AppointmentStatus newStatus,
    CancellationToken ct)
  {
    INotification? notification = null;

    if (previousStatus == AppointmentStatus.Pending && newStatus == AppointmentStatus.Booked)
    {
      notification = new AppointmentConfirmedBySalonEvent(appointment.TenantId, appointment.Id);
    }
    else if (newStatus == AppointmentStatus.Canceled && previousStatus != AppointmentStatus.Canceled)
    {
      var wasPending = previousStatus == AppointmentStatus.Pending
        || previousStatus == AppointmentStatus.AwaitingOtp;
      notification = new AppointmentCancelledBySalonEvent(appointment.TenantId, appointment.Id, wasPending);
    }

    if (notification is null)
    {
      return;
    }

    try
    {
      await _publisher.Publish(notification, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Publikacja zdarzenia powiadomień dla wizyty {Id} nie powiodła się", appointment.Id);
    }
  }
}
