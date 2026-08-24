using App.Application.Common.Interfaces;
using App.Application.Notifications;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Notifications.Handlers;

/// <summary>
/// Snapshot danych potrzebnych do zbudowania powiadomień o wizycie. Ładowany przez
/// <see cref="NotificationContextLoader"/> z pominięciem filtrów tenanta (handlery zdarzeń
/// mogą działać poza kontekstem bieżącego tenanta — filtrujemy jawnie po <c>TenantId</c> ze zdarzenia).
/// </summary>
internal sealed record AppointmentNotificationContext(
  Tenant Tenant,
  Appointment Appointment,
  Customer? Customer,
  Employee Employee,
  Service Service)
{
  public string SalonName => string.IsNullOrWhiteSpace(Tenant.Name) ? "Salon" : Tenant.Name;

  public string StaffName
  {
    get
    {
      var name = $"{Employee.FirstName} {Employee.LastName}".Trim();
      return string.IsNullOrWhiteSpace(name) ? "Pracownik" : name;
    }
  }

  /// <summary>
  /// Imię i nazwisko klienta lub pusty string, gdy klient ich nie podał (bez fallbacku „Klient").
  /// Powiadomienia DO salonu pokazują wiersz „Klient" tylko wtedy, gdy ta wartość jest niepusta.
  /// </summary>
  public string CustomerFullName =>
    Customer is null ? string.Empty : $"{Customer.FirstName} {Customer.LastName}".Trim();

  /// <summary>Wyświetlana nazwa klienta z fallbackiem „Klient" — do powitań w powiadomieniach DO klienta.</summary>
  public string CustomerName =>
    string.IsNullOrWhiteSpace(CustomerFullName) ? "Klient" : CustomerFullName;

  /// <summary>Telefon klienta (E.164) lub <c>null</c>, gdy nieznany.</summary>
  public string? CustomerPhone => Customer?.PhoneNumber?.Value;

  /// <summary>E-mail klienta lub <c>null</c>, gdy nieznany.</summary>
  public string? CustomerEmail =>
    string.IsNullOrWhiteSpace(Customer?.Email) ? null : Customer!.Email;

  public string ServiceName => string.IsNullOrWhiteSpace(Service.Name) ? "Usługa" : Service.Name;

  /// <summary>
  /// Buduje adresata powiadomienia DO KLIENTA. Kanał komunikacji z klientem = kanał weryfikacji
  /// (<see cref="Tenant.CustomerVerificationChannel"/>) — przy rezerwacji zbieramy tylko jeden
  /// kontakt, więc tym samym kanałem ślemy powiadomienia: <c>Phone</c> → SMS, <c>Email</c> → e-mail.
  /// Zwraca <c>null</c>, gdy brak klienta albo kontaktu w wybranym kanale — wtedy powiadomienie pomijamy.
  /// </summary>
  public NotificationRecipient? CustomerRecipient()
  {
    if (Customer is null)
    {
      return null;
    }

    if (Tenant.CustomerVerificationChannel == CustomerVerificationChannel.Phone)
    {
      var phone = Customer.PhoneNumber?.Value;
      return string.IsNullOrWhiteSpace(phone)
        ? null
        : new NotificationRecipient(null, phone, null, CustomerName);
    }

    return string.IsNullOrWhiteSpace(Customer.Email)
      ? null
      : new NotificationRecipient(Customer.Email, null, null, CustomerName);
  }
}

internal static class NotificationContextLoader
{
  public static async Task<AppointmentNotificationContext?> LoadAsync(
    IApplicationDbContext context,
    Guid tenantId,
    Guid appointmentId,
    CancellationToken ct)
  {
    var appointment = await context.Appointments
      .IgnoreQueryFilters().AsNoTracking()
      .FirstOrDefaultAsync(a => a.Id == appointmentId && a.TenantId == tenantId, ct);
    if (appointment is null)
    {
      return null;
    }

    var tenant = await context.Tenants
      .IgnoreQueryFilters().AsNoTracking()
      .FirstOrDefaultAsync(t => t.Id == tenantId, ct);
    if (tenant is null)
    {
      return null;
    }

    // Backstop tenant na FK z appointmentu — appointment jest już zweryfikowany tenantowo (a.TenantId == tenantId),
    // ale jawny `&& X.TenantId == tenantId` chroni treść powiadomień przed wyciekiem danych innego salonu,
    // gdyby kiedyś doszło do naruszenia integralności FK.
    var employee = await context.Employees
      .IgnoreQueryFilters().AsNoTracking()
      .FirstOrDefaultAsync(e => e.Id == appointment.EmployeeId && e.TenantId == tenantId, ct);
    if (employee is null)
    {
      return null;
    }

    var service = await context.Services
      .IgnoreQueryFilters().AsNoTracking()
      .FirstOrDefaultAsync(s => s.Id == appointment.ServiceId && s.TenantId == tenantId, ct);
    if (service is null)
    {
      return null;
    }

    Customer? customer = null;
    if (appointment.CustomerId is { } customerId)
    {
      customer = await context.Customers
        .IgnoreQueryFilters().AsNoTracking()
        .FirstOrDefaultAsync(c => c.Id == customerId && c.TenantId == tenantId, ct);
    }

    return new AppointmentNotificationContext(tenant, appointment, customer, employee, service);
  }
}
