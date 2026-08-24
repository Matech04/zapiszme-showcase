using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Common;
using App.Application.Notifications.Events;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace App.Application.Appointments.Commands.DeleteAppointment;

public record DeleteAppointmentCommand(Guid Id) : IRequest, IAppointmentWriteRequest;

internal class DeleteAppointmentHandler : TenantHandler<DeleteAppointmentCommand>
{
  private readonly IAppointmentRepository _repository;
  private readonly IStaffAccessPolicy _access;
  private readonly IUnitOfWork _uow;
  private readonly IPublisher _publisher;
  private readonly ILogger<DeleteAppointmentHandler> _logger;

  public DeleteAppointmentHandler(
    IAppointmentRepository repository,
    IUnitOfWork uow,
    IStaffAccessPolicy access,
    ICurrentTenantService currentTenantService,
    IPublisher publisher,
    ILogger<DeleteAppointmentHandler> logger) : base(currentTenantService)
  {
    _repository = repository;
    _access = access;
    _uow = uow;
    _publisher = publisher;
    _logger = logger;
  }

  public override async Task Handle(DeleteAppointmentCommand request, CancellationToken ct)
  {
    var appointment = await _repository.GetByIdAsync(request.Id) ?? throw new NotFoundException(nameof(Appointment), request.Id);

    // Rolę odsiewa `[Authorize(Policy = "StaffManagement")]` na endpoincie; tu pilnujemy własności
    // kalendarza, żeby handler nie był bezbronny przy wywołaniu inną ścieżką niż kontroler.
    await _access.EnsureCanMutateEmployeeCalendarAsync(appointment.EmployeeId, ct);

    if (appointment.TenantId != TenantId)
    {
      throw new TenantViolation();
    }

    // Publikujemy PRZED usunięciem — handler powiadomień ładuje wizytę przez AsNoTracking,
    // więc wiersz musi jeszcze istnieć w bazie. Best-effort: błąd nie blokuje usunięcia.
    var wasPending = appointment.Status == AppointmentStatus.Pending
      || appointment.Status == AppointmentStatus.AwaitingOtp;
    try
    {
      await _publisher.Publish(
        new AppointmentCancelledBySalonEvent(appointment.TenantId, appointment.Id, wasPending),
        ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      _logger.LogWarning(ex, "Publikacja AppointmentCancelledBySalonEvent dla usuwanej wizyty {Id} nie powiodła się", appointment.Id);
    }

    _repository.Remove(appointment);
    await _uow.SaveChangesAsync(ct);
  }
}