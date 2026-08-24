using App.Application.Appointments.Dtos;
using App.Application.Appointments.Queries.GetAvailableTimeSlots;
using MediatR;

namespace App.Application.Booking.BookingAppointments.Queries;

public record GetBookingAvailableTimeSlotsQuery(DateOnly Date, Guid EmployeeId, IReadOnlyList<Guid> ServiceIds)
    : IRequest<List<AppointmentSlotDto>>;

internal sealed class GetBookingAvailableTimeSlotsQueryHandler
    : IRequestHandler<GetBookingAvailableTimeSlotsQuery, List<AppointmentSlotDto>>
{
  private readonly ISender _mediator;

  public GetBookingAvailableTimeSlotsQueryHandler(ISender mediator)
  {
    _mediator = mediator;
  }

  public Task<List<AppointmentSlotDto>> Handle(GetBookingAvailableTimeSlotsQuery request, CancellationToken ct)
  {
    return _mediator.Send(
        new GetAvailableTimeSlotsQuery(
            request.Date,
            request.EmployeeId,
            request.ServiceIds,
            EnforceStrictGapFilter: true,
            EnforcePublicVisibility: true),
        ct);
  }
}
