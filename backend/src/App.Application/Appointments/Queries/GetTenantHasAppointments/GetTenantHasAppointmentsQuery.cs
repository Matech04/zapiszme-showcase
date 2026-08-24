using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Appointments.Queries.GetTenantHasAppointments;

/// <summary>
/// Czy salon ma już choć jedną realną wizytę? Używane przez onboarding-checklist
/// na dashboardzie ("Odbierz pierwszą rezerwację"). Lekki EXISTS — nie materializuje
/// joinu wizyty×pracownicy×usługi×klienci i nie podlega limitowi zakresu dat z
/// GetAppointmentsByRange (366 dni), więc wykrywa wizytę w dowolnej dacie.
/// </summary>
public record GetTenantHasAppointmentsQuery() : IRequest<bool>;

internal class GetTenantHasAppointmentsHandler : TenantHandler<GetTenantHasAppointmentsQuery, bool>
{
  private readonly IApplicationDbContext _context;

  public GetTenantHasAppointmentsHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override Task<bool> Handle(GetTenantHasAppointmentsQuery request, CancellationToken ct)
  {
    // Pomijamy AwaitingOtp — to ulotny hold publicznej rezerwacji (klient nie potwierdził
    // jeszcze OTP), który po TTL znika; nie chcemy, by checklist „migał".
    return _context.Appointments
        .AsNoTracking()
        .AnyAsync(a => a.TenantId == TenantId && a.Status != AppointmentStatus.AwaitingOtp, ct);
  }
}
