using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Common;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Appointments.Commands.SetFinalPrice;

/// <summary>
/// Ustawia cenę końcową (faktycznie pobraną) wizyty. Wejście płaskie (Amount + Currency),
/// nie value-object — zgodnie z konwencją kontraktów (NSwag) w pozostałych komendach.
/// </summary>
public record SetFinalPriceCommand(
  Guid AppointmentId,
  decimal Amount,
  string Currency) : IRequest<Guid>, IAppointmentWriteRequest;

public class SetFinalPriceHandler : TenantHandler<SetFinalPriceCommand, Guid>
{
  private readonly IAppointmentRepository _repository;
  private readonly IStaffAccessPolicy _access;
  private readonly IUnitOfWork _uow;

  public SetFinalPriceHandler(
    IAppointmentRepository repository,
    IUnitOfWork uow,
    IStaffAccessPolicy access,
    ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _access = access;
    _repository = repository;
    _uow = uow;
  }

  public override async Task<Guid> Handle(SetFinalPriceCommand request, CancellationToken ct)
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

    appointment.SetFinalPrice(new Money(request.Amount, request.Currency));

    await _uow.SaveChangesAsync(ct);

    return appointment.Id;
  }
}
