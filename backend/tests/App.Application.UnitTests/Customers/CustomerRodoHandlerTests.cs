using App.Application.Common.Interfaces;
using App.Application.Customers.Commands.AnonymizeCustomer;
using App.Application.Customers.Queries.ExportCustomerData;
using App.Application.UnitTests.TestSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Customers;

/// <summary>
/// RODO: anonimizacja (art. 17) czyści notatki wizyt; eksport (art. 15) zawiera InstagramNick.
/// Baza InMemory EF Core (jak pozostałe testy handlerów Customer).
/// </summary>
public sealed class CustomerRodoHandlerTests
{
  [Fact]
  public async Task Anonymize_clears_appointment_notes()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, tenantId, "Anna", "Kowalska", instagramNick: "anna.k");
    var appointment = SeedAppointment(db, tenantId, customer.Id, notes: "Uczulenie na lateks, cukrzyca");

    var handler = new AnonymizeCustomerHandler(db, db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new AnonymizeCustomerCommand(customer.Id), ct);

    var reloaded = await db.Appointments.IgnoreQueryFilters().AsNoTracking()
      .FirstAsync(a => a.Id == appointment.Id, ct);
    Assert.Equal(string.Empty, reloaded.AppointmentNotes);

    var reloadedCustomer = await db.Customers.IgnoreQueryFilters().AsNoTracking()
      .FirstAsync(c => c.Id == customer.Id, ct);
    Assert.Equal("Klient", reloadedCustomer.FirstName);
  }

  [Fact]
  public async Task Export_includes_instagram_nick()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, tenantId, "Anna", "Kowalska", instagramNick: "anna.k_01");

    var handler = new ExportCustomerDataHandler(db, new TestTimeProvider(), new FakeCurrentTenantService(tenantId));
    var export = await handler.Handle(new ExportCustomerDataQuery(customer.Id), ct);

    Assert.Equal("anna.k_01", export.InstagramNick);
  }

  // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

  private static (ApplicationDbContext db, Guid tenantId) CreateDb()
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    return (db, tenantId);
  }

  private static Customer SeedCustomer(
    ApplicationDbContext db,
    Guid tenantId,
    string firstName,
    string lastName,
    string? instagramNick = null)
  {
    var customer = Customer.CreateFromPublicBooking(
      tenantId,
      $"{firstName.ToLower()}@example.com",
      new PhoneNumber("+48501000001"),
      firstName,
      lastName,
      instagramNick);
    db.Customers.Add(customer);
    db.SaveChanges();
    return customer;
  }

  private static Appointment SeedAppointment(ApplicationDbContext db, Guid tenantId, Guid customerId, string notes)
  {
    var appointment = new Appointment(
      tenantId, Guid.NewGuid(), Guid.NewGuid(), customerId,
      DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), new TimeOnly(10, 0), new TimeOnly(11, 0),
      AppointmentStatus.Completed, new Money(100m, "PLN"), notes, null);
    db.Appointments.Add(appointment);
    db.SaveChanges();
    return appointment;
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
