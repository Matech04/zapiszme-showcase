using App.Application.Appointments.Commands.ApplyReschedule;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Options;

namespace App.Application.Booking.BookingAppointments.Commands;

/// <summary>Zmiana terminu/usługi z panelu klienta — wymaga ważnego tokena dzierżawy; po przeniesieniu zwracana jest nowa dzierżawa (HoldLease).</summary>
public record UpdatePublicAppointmentCommand(
    Guid AppointmentId,
    Guid Token,
    Guid EmployeeId,
    IReadOnlyList<Guid> ServiceIds,
    DateOnly Date,
    TimeOnly StartTime)
    : IRequest<HoldLease>;

internal sealed class UpdatePublicAppointmentCommandHandler : IRequestHandler<UpdatePublicAppointmentCommand, HoldLease>
{
  private readonly ISender _mediator;
  private readonly IAppointmentRepository _repository;
  private readonly IUnitOfWork _uow;
  private readonly TimeProvider _timeProvider;
  private readonly ICurrentTenantService _currentTenant;
  private readonly BookingHoldOptions _holdOptions;

  public UpdatePublicAppointmentCommandHandler(
      ISender mediator,
      IAppointmentRepository repository,
      IUnitOfWork uow,
      TimeProvider timeProvider,
      ICurrentTenantService currentTenant,
      IOptions<BookingHoldOptions> holdOptions)
  {
    _mediator = mediator;
    _repository = repository;
    _uow = uow;
    _timeProvider = timeProvider;
    _currentTenant = currentTenant;
    _holdOptions = holdOptions.Value;
  }

  public async Task<HoldLease> Handle(UpdatePublicAppointmentCommand request, CancellationToken ct)
  {
    var appointment = await _repository.GetByIdAsync(request.AppointmentId)
        ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    // Defense-in-depth: wizyta MUSI należeć do tenanta ze slugu (zob. WARNING w AppointmentRepository).
    if (_currentTenant.TenantId is { } currentTenantId && appointment.TenantId != currentTenantId)
    {
      throw new NotFoundException(nameof(Appointment), request.AppointmentId);
    }

    if (appointment.Lease is null || !appointment.Lease.IsValid(request.Token))
    {
      throw new ForbiddenAccessException(
          "Nieprawidłowy lub wygasły token rezerwacji.",
          ErrorCodes.AppointmentOtpInvalidLease);
    }

    await _mediator.Send(
        new ApplyRescheduleCommand(
            request.AppointmentId,
            request.EmployeeId,
            request.ServiceIds,
            request.Date,
            request.StartTime,
            // Anonimowa ścieżka publicznego bookingu — obowiązuje horyzont i publikacja miesiąca.
            EnforcePublicVisibility: true),
        ct);

    // TTL z konfiguracji, nie sztywne 10 minut. Sztywna wartość pozwalała obejść HoldTtl: squatter
    // wołał PATCH co ~10 min i trzymał (a nawet przesuwał po kalendarzu) slot w oknach dużo dłuższych
    // niż projektowe. Po zamówieniu OTP klient potrzebuje czasu na wpisanie kodu — wtedy OtpLease.
    var ttl = appointment.Status == AppointmentStatus.AwaitingOtp
      ? _holdOptions.OtpLease
      : _holdOptions.HoldTtl;

    var lease = new HoldLease(Guid.NewGuid(), _timeProvider.GetUtcNow().UtcDateTime.Add(ttl));
    appointment.ReplaceHoldLease(lease);

    await _uow.SaveChangesAsync(ct);

    return lease;
  }
}
