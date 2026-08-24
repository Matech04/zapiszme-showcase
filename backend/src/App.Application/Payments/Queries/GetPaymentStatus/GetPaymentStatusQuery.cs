using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Payments.Queries.GetPaymentStatus;

/// <summary>
/// Publiczny (anonimowy) odczyt stanu płatności wizyty po jej GUID — używany przez stronę powrotu
/// w aplikacji web do odpytywania po redirectcie z Checkout (nie ufamy samemu redirectowi).
/// Zwraca wyłącznie status (bez PII). GUID jest niezgadywalny — brak kontekstu tenanta jest OK.
/// </summary>
public record GetPaymentStatusQuery(Guid AppointmentId) : IRequest<PaymentStatusDto>;

public record PaymentStatusDto(string PaymentStatus, string AppointmentStatus, bool Expired);

internal class GetPaymentStatusQueryHandler : IRequestHandler<GetPaymentStatusQuery, PaymentStatusDto>
{
  private readonly IApplicationDbContext _context;

  public GetPaymentStatusQueryHandler(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task<PaymentStatusDto> Handle(GetPaymentStatusQuery request, CancellationToken ct)
  {
    var appointment = await _context.Appointments
      .IgnoreQueryFilters()
      .AsNoTracking()
      .FirstOrDefaultAsync(a => a.Id == request.AppointmentId, ct)
      ?? throw new NotFoundException(nameof(Appointment), request.AppointmentId);

    return new PaymentStatusDto(
      appointment.PaymentStatus.ToString(),
      appointment.Status.Name,
      appointment.IsDepositLinkExpired(DateTime.UtcNow));
  }
}
