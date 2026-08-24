using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Repositories;

public class AppointmentRepository : IAppointmentRepository
{
  private readonly IApplicationDbContext _context;

  public AppointmentRepository(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task AddAsync(Appointment appointment) => await _context.Appointments.AddAsync(appointment);
  // FirstOrDefaultAsync (nie FindAsync) — globalny query filter tenanta działa jako automatyczny
  // backstop izolacji. FindAsync omija filtry i wymagał ręcznego checku TenantId w każdym handlerze.
  // WARNING: izolacja tenanta dla publicznych handlerów OTP (VerifyOtp/RequestOtp) opiera się na tym
  // query-filtrze. NIE dodawaj tu IgnoreQueryFilters() ani nie zmieniaj na FindAsync — cichy cross-tenant.
  public async Task<Appointment?> GetByIdAsync(Guid id) =>
    await _context.Appointments.FirstOrDefaultAsync(a => a.Id == id);
  public void Update(Appointment appointment) => _context.Appointments.Update(appointment);
  public void Remove(Appointment appointment) => _context.Appointments.Remove(appointment);
  public async Task<bool> HasCollisionAsync(Guid employeeId, TimeRange timeRange, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null)
  {
    // Wygasły anonimowy hold OTP (AwaitingOtp) nie powoduje kolizji — slot jest de facto wolny
    // (czeka tylko na sprzątanie przez job). Pozwala to nowej rezerwacji zająć porzucony slot
    // bez czekania na cykl joba. Booked/Pending blokują zawsze (nawet ze stale lease).
    var nowUtc = DateTime.UtcNow;
    bool collision = await _context.Appointments.AnyAsync(a =>
      a.EmployeeId == employeeId
      && a.TenantId == tenantId
      && a.Date == date
      && a.Id != ignoreAppointmentId
      && a.Status != AppointmentStatus.Canceled
      && !(a.Status == AppointmentStatus.AwaitingOtp && a.Lease!.ExpiryTimeUtc < nowUtc)
      && a.StartTime < timeRange.EndTime
      && a.EndTime > timeRange.StartTime
      );

    return collision;
  }

  public Task<bool> HasCollisionAsync(Guid employeeId, TimeOnly startTime, TimeOnly endTime, DateOnly date, Guid tenantId, Guid? ignoreAppointmentId = null)
  {
    return HasCollisionAsync(employeeId, new TimeRange(startTime, endTime), date, tenantId, ignoreAppointmentId);
  }

  public async Task<bool> HasCollisionAsync(Guid employeeId, TimeRange timeRange, DateOnly date, Guid tenantId, IReadOnlyCollection<Guid> ignoreAppointmentIds)
  {
    var nowUtc = DateTime.UtcNow;
    bool collision = await _context.Appointments.AnyAsync(a =>
      a.EmployeeId == employeeId
      && a.TenantId == tenantId
      && a.Date == date
      && !ignoreAppointmentIds.Contains(a.Id)
      && a.Status != AppointmentStatus.Canceled
      && !(a.Status == AppointmentStatus.AwaitingOtp && a.Lease!.ExpiryTimeUtc < nowUtc)
      && a.StartTime < timeRange.EndTime
      && a.EndTime > timeRange.StartTime
      );

    return collision;
  }

  public async Task<bool> HasCollisionInDateRangeAsync(Guid employeeId, DateOnly startDate, DateOnly endDate, Guid tenantId)
  {
    bool collision = await _context.Appointments.AnyAsync(a =>
      a.EmployeeId == employeeId
      && a.TenantId == tenantId
      && a.Date >= startDate
      && a.Date <= endDate
      && a.Status != AppointmentStatus.Canceled
      );

    return collision;
  }

  public async Task<List<Appointment>> GetAppointmentsByDateAsync(Guid employeeId, DateOnly date, Guid tenantId)
  {
    return await _context.Appointments
      .Where(a =>
        a.EmployeeId == employeeId
        && a.TenantId == tenantId
        && a.Date == date
        && a.Status != AppointmentStatus.Canceled)
      .ToListAsync();
  }

  public async Task<List<Appointment>> GetAppointmentsInDateRangeAsync(Guid employeeId, DateOnly startDate, DateOnly endDate, Guid tenantId)
  {
    return await _context.Appointments
      .Where(a =>
        a.EmployeeId == employeeId
        && a.TenantId == tenantId
        && a.Date >= startDate
        && a.Date <= endDate
        && a.Status != AppointmentStatus.Canceled)
      .ToListAsync();
  }

}
