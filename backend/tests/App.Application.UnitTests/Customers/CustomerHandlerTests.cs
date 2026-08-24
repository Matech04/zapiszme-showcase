using App.Application.Common.Interfaces;
using App.Application.Customers.Commands.CreateCustomer;
using App.Application.Customers.Commands.DeleteCustomer;
using App.Application.Customers.Commands.SetCustomerWhitelist;
using App.Application.Customers.Commands.UpdateCustomer;
using App.Application.Customers.Commands.UpdateCustomerNotes;
using App.Application.Customers.Queries.GetCustomerById;
using App.Application.Customers.Queries.GetCustomers;
using App.Application.Customers.Queries.SearchCustomersQuery;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using App.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Customers;

/// <summary>
/// APP-CUST-001..010 — testy handlerów Customer z bazą InMemory EF Core.
/// </summary>
public sealed class CustomerHandlerTests
{
  // ── CUST-001: Create ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Create_customer_persists_and_returns_guid()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var handler = new CreateCustomerHandler(new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    var id = await handler.Handle(
      new CreateCustomerCommand("Anna", "Kowalska", "anna@example.com", "+48501000001", "VIP"),
      ct);

    Assert.NotEqual(Guid.Empty, id);
    var saved = await db.Customers.FindAsync(new object[] { id }, ct);
    Assert.NotNull(saved);
    Assert.Equal("Anna", saved.FirstName);
    Assert.Equal(tenantId, saved.TenantId);
  }

  [Fact]
  public async Task Create_customer_without_email_or_phone_persists()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var handler = new CreateCustomerHandler(new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    var id = await handler.Handle(
      new CreateCustomerCommand("Anna", "Kowalska", "", "", ""),
      ct);

    var saved = await db.Customers.FindAsync(new object[] { id }, ct);
    Assert.NotNull(saved);
    Assert.Equal(string.Empty, saved.Email);
    Assert.Null(saved.PhoneNumber);
  }

  [Fact]
  public async Task Create_customer_persists_normalized_instagram_nick()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var handler = new CreateCustomerHandler(new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    // Wiodące @ i spacje są strzyżone przez domenę (NormalizeInstagramNick).
    var id = await handler.Handle(
      new CreateCustomerCommand("Anna", "Kowalska", "", "", "", "  @anna.nails  "),
      ct);

    var saved = await db.Customers.FindAsync(new object[] { id }, ct);
    Assert.Equal("anna.nails", saved!.InstagramNick);
  }

  // ── CUST-002: Update ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Update_customer_persists_changes()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, tenantId, "Old", "Name");

    var handler = new UpdateCustomerHandler(new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(
      new UpdateCustomerCommand(customer.Id, "New", "Name", "new@example.com", "+48502000002", ""),
      ct);

    var updated = await db.Customers.FindAsync(new object[] { customer.Id }, ct);
    Assert.Equal("New", updated!.FirstName);
    Assert.Equal("new@example.com", updated.Email);
  }

  [Fact]
  public async Task Update_customer_sets_and_clears_instagram_nick()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, tenantId, "Ola", "Insta");
    var handler = new UpdateCustomerHandler(new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    await handler.Handle(
      new UpdateCustomerCommand(customer.Id, "Ola", "Insta", "", "", "", "@ola_ig"),
      ct);
    Assert.Equal("ola_ig", (await db.Customers.FindAsync(new object[] { customer.Id }, ct))!.InstagramNick);

    // Panel jest autorytatywny — puste pole czyści nick (w odróżnieniu od enrich z rezerwacji).
    await handler.Handle(
      new UpdateCustomerCommand(customer.Id, "Ola", "Insta", "", "", "", ""),
      ct);
    Assert.Null((await db.Customers.FindAsync(new object[] { customer.Id }, ct))!.InstagramNick);
  }

  [Fact]
  public async Task Get_customer_by_id_returns_instagram_nick()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var create = new CreateCustomerHandler(new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));
    var id = await create.Handle(
      new CreateCustomerCommand("Zofia", "IG", "", "", "", "zofia.brows"), ct);

    var handler = new GetCustomerByIdHandler(db, new FakeCurrentTenantService(tenantId));
    var result = await handler.Handle(new GetCustomerByIdQuery(id), ct);

    Assert.Equal("zofia.brows", result.InstagramNick);
  }

  [Fact]
  public async Task Update_customer_unknown_id_throws_not_found_exception()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var handler = new UpdateCustomerHandler(new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(
        new UpdateCustomerCommand(Guid.NewGuid(), "X", "Y", "x@y.com", "+48503000003", ""),
        ct));
  }

  [Fact]
  public async Task Update_customer_from_other_tenant_throws_not_found()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, Guid.NewGuid(), "Other", "Tenant");

    var handler = new UpdateCustomerHandler(new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    // Cross-tenant: encja innego tenanta jest niewidoczna przez query filter (GetByIdAsync→null),
    // więc handler rzuca NotFound — silniejsza izolacja niż TenantViolation (brak leaku istnienia).
    // Ręczny check TenantViolation w handlerze pozostaje jako defense-in-depth dla ścieżki zapisu.
    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(
        new UpdateCustomerCommand(customer.Id, "X", "Y", "x@y.com", "+48503000003", ""),
        ct));
  }

  // ── CUST-002b: Update notes (lekki endpoint notatki) ──────────────────────────────────────────

  [Fact]
  public async Task Update_customer_notes_persists_changes()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, tenantId, "Ola", "Notes");

    var handler = new UpdateCustomerNotesHandler(new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));
    var returnedId = await handler.Handle(
      new UpdateCustomerNotesCommand(customer.Id, "  Alergia na lateks  "), ct);

    Assert.Equal(customer.Id, returnedId);
    var updated = await db.Customers.FindAsync(new object[] { customer.Id }, ct);
    Assert.Equal("Alergia na lateks", updated!.GeneralNotes);
  }

  [Fact]
  public async Task Update_customer_notes_unknown_id_throws_not_found_exception()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var handler = new UpdateCustomerNotesHandler(new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new UpdateCustomerNotesCommand(Guid.NewGuid(), "x"), ct));
  }

  [Fact]
  public async Task Update_customer_notes_from_other_tenant_throws_not_found()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, Guid.NewGuid(), "Cross", "Tenant");

    var handler = new UpdateCustomerNotesHandler(new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    // Cross-tenant: niewidoczne przez query filter → NotFound (brak leaku istnienia).
    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new UpdateCustomerNotesCommand(customer.Id, "x"), ct));
  }

  // ── CUST-004: Delete ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Delete_customer_erases_pii_and_deactivates()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, tenantId, "Jan", "Nowak");

    var handler = BuildDeleteHandler(db, tenantId);
    await handler.Handle(new DeleteCustomerCommand(customer.Id), ct);

    var deleted = await db.Customers.IgnoreQueryFilters()
      .AsNoTracking()
      .FirstOrDefaultAsync(c => c.Id == customer.Id, ct);
    Assert.NotNull(deleted);
    Assert.False(deleted!.IsActive);

    // „Usuń” to erasure, nie soft-delete: PII nie może zostać w bazie. Sam `IsActive == false`
    // przeszedłby też dla starej semantyki — dlatego asercje na konkretnych polach.
    Assert.Equal("Klient", deleted.FirstName);
    Assert.Equal("usunięty", deleted.LastName);
    Assert.Equal(string.Empty, deleted.Email);
    Assert.Null(deleted.PhoneNumber);
    Assert.Null(deleted.InstagramNick);
    Assert.Equal(string.Empty, deleted.GeneralNotes);
  }

  [Fact]
  public async Task Delete_customer_scrubs_appointment_notes()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, tenantId, "Jan", "Nowak");
    var appointment = SeedAppointmentForCustomer(db, tenantId, customer.Id, "Uczulenie na lateks");

    var handler = BuildDeleteHandler(db, tenantId);
    await handler.Handle(new DeleteCustomerCommand(customer.Id), ct);

    // Notatka wizyty to free-text mogący nieść dane art. 9 — znika razem z klientem,
    // ale sama wizyta (kwota, status) zostaje jako zapis księgowy.
    var reloaded = await db.Appointments.IgnoreQueryFilters()
      .AsNoTracking()
      .FirstAsync(a => a.Id == appointment.Id, ct);
    Assert.Equal(string.Empty, reloaded.AppointmentNotes);
    Assert.Equal(customer.Id, reloaded.CustomerId);
  }

  [Fact]
  public async Task Delete_customer_unknown_id_throws_not_found_exception()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var handler = BuildDeleteHandler(db, tenantId);

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new DeleteCustomerCommand(Guid.NewGuid()), ct));
  }

  [Fact]
  public async Task Delete_customer_from_other_tenant_throws_not_found()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, Guid.NewGuid(), "Cross", "Tenant");

    var handler = BuildDeleteHandler(db, tenantId);

    // Cross-tenant: niewidoczne przez query filter → NotFound (brak leaku istnienia).
    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new DeleteCustomerCommand(customer.Id), ct));
  }

  // ── CUST-008: Whitelist single ────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Set_whitelist_true_persists()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, tenantId, "Ewa", "Biała");

    var handler = new SetCustomerWhitelistCommandHandler(
      new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    await handler.Handle(new SetCustomerWhitelistCommand(customer.Id, true), ct);

    var updated = await db.Customers.FindAsync(new object[] { customer.Id }, ct);
    Assert.True(updated!.IsWhitelisted);
  }

  [Fact]
  public async Task Set_whitelist_false_persists()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, tenantId, "Ewa", "Biała");
    customer.Whitelist();
    await db.SaveChangesAsync(ct);

    var handler = new SetCustomerWhitelistCommandHandler(
      new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    await handler.Handle(new SetCustomerWhitelistCommand(customer.Id, false), ct);

    var updated = await db.Customers.FindAsync(new object[] { customer.Id }, ct);
    Assert.False(updated!.IsWhitelisted);
  }

  [Fact]
  public async Task Set_whitelist_unknown_id_throws_not_found_exception()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var handler = new SetCustomerWhitelistCommandHandler(
      new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new SetCustomerWhitelistCommand(Guid.NewGuid(), true), ct));
  }

  [Fact]
  public async Task Set_whitelist_cross_tenant_throws_not_found()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, Guid.NewGuid(), "Cross", "Tenant");

    var handler = new SetCustomerWhitelistCommandHandler(
      new CustomerRepository(db), db, new FakeCurrentTenantService(tenantId));

    // Cross-tenant: niewidoczne przez query filter → NotFound (brak leaku istnienia).
    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new SetCustomerWhitelistCommand(customer.Id, true), ct));
  }

  // ── CUST-008: Whitelist bulk ───────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Bulk_whitelist_updates_own_customers_and_returns_count()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var c1 = SeedCustomer(db, tenantId, "A", "One");
    var c2 = SeedCustomer(db, tenantId, "B", "Two");

    var handler = new BulkSetCustomerWhitelistCommandHandler(db, db, new FakeCurrentTenantService(tenantId));
    var count = await handler.Handle(
      new BulkSetCustomerWhitelistCommand(new[] { c1.Id, c2.Id }, true), ct);

    Assert.Equal(2, count);
    Assert.True((await db.Customers.FindAsync(new object[] { c1.Id }, ct))!.IsWhitelisted);
    Assert.True((await db.Customers.FindAsync(new object[] { c2.Id }, ct))!.IsWhitelisted);
  }

  [Fact]
  public async Task Bulk_whitelist_empty_list_returns_zero()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var handler = new BulkSetCustomerWhitelistCommandHandler(db, db, new FakeCurrentTenantService(tenantId));

    var count = await handler.Handle(
      new BulkSetCustomerWhitelistCommand(Array.Empty<Guid>(), true), ct);

    Assert.Equal(0, count);
  }

  [Fact]
  public async Task Bulk_whitelist_skips_cross_tenant_customers()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var own = SeedCustomer(db, tenantId, "Own", "Customer");
    var other = SeedCustomer(db, Guid.NewGuid(), "Other", "Tenant");

    var handler = new BulkSetCustomerWhitelistCommandHandler(db, db, new FakeCurrentTenantService(tenantId));
    var count = await handler.Handle(
      new BulkSetCustomerWhitelistCommand(new[] { own.Id, other.Id }, true), ct);

    Assert.Equal(1, count);
    var otherReloaded = await db.Customers.IgnoreQueryFilters()
      .AsNoTracking()
      .FirstOrDefaultAsync(c => c.Id == other.Id, ct);
    Assert.False(otherReloaded!.IsWhitelisted);
  }

  // ── CUST-009: Search ──────────────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Search_by_first_name_returns_matching_customer()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    SeedCustomer(db, tenantId, "Magdalena", "Nowak");
    var handler = new SearchCustomersHandler(db, new FakeCurrentTenantService(tenantId));

    var results = await handler.Handle(new SearchCustomersQuery("Magdalena"), ct);

    Assert.Contains(results, r => r.FirstName == "Magdalena");
  }

  [Fact]
  public async Task Search_by_last_name_returns_matching_customer()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    SeedCustomer(db, tenantId, "Magdalena", "Wiśniewska");
    var handler = new SearchCustomersHandler(db, new FakeCurrentTenantService(tenantId));

    var results = await handler.Handle(new SearchCustomersQuery("Wiśniew"), ct);

    Assert.Contains(results, r => r.LastName == "Wiśniewska");
  }

  [Fact]
  public async Task Search_by_phone_returns_matching_customer()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    SeedCustomer(db, tenantId, "Bartek", "Zielony", "+48509123456");
    var handler = new SearchCustomersHandler(db, new FakeCurrentTenantService(tenantId));

    var results = await handler.Handle(new SearchCustomersQuery("509123456"), ct);

    Assert.Contains(results, r => r.FirstName == "Bartek");
  }

  [Fact]
  public async Task Search_by_phone_ignores_formatting()
  {
    // Fraza i zapisany numer są normalizowane do samych cyfr — spacje i prefiks +48 nie psują dopasowania.
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    SeedCustomer(db, tenantId, "Bartek", "Zielony", "+48509123456");
    var handler = new SearchCustomersHandler(db, new FakeCurrentTenantService(tenantId));

    var results = await handler.Handle(new SearchCustomersQuery("509 123 456"), ct);

    Assert.Contains(results, r => r.FirstName == "Bartek");
  }

  [Fact]
  public async Task Search_with_empty_term_returns_empty_list()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    SeedCustomer(db, tenantId, "Test", "User");
    var handler = new SearchCustomersHandler(db, new FakeCurrentTenantService(tenantId));

    var results = await handler.Handle(new SearchCustomersQuery(""), ct);

    Assert.Empty(results);
  }

  [Fact]
  public async Task Search_returns_at_most_10_results()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    for (var i = 0; i < 15; i++)
    {
      SeedCustomer(db, tenantId, "SearchTest", $"User{i}");
    }

    var handler = new SearchCustomersHandler(db, new FakeCurrentTenantService(tenantId));

    var results = await handler.Handle(new SearchCustomersQuery("SearchTest"), ct);

    Assert.True(results.Count <= 10);
  }

  [Fact]
  public async Task Search_is_isolated_per_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    SeedCustomer(db, tenantId, "OwnTenant", "Customer");
    SeedCustomer(db, Guid.NewGuid(), "OtherTenant", "Customer");

    var handler = new SearchCustomersHandler(db, new FakeCurrentTenantService(tenantId));
    var results = await handler.Handle(new SearchCustomersQuery("Customer"), ct);

    Assert.Contains(results, r => r.FirstName == "OwnTenant");
    Assert.DoesNotContain(results, r => r.FirstName == "OtherTenant");
  }

  // ── CUST-010: Get list & by ID ────────────────────────────────────────────────────────────────

  [Fact]
  public async Task Get_customers_returns_only_own_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var own = SeedCustomer(db, tenantId, "Own", "Customer");
    var other = SeedCustomer(db, Guid.NewGuid(), "Other", "Tenant");

    var handler = new GetCustomersHandler(db, new FakeCurrentTenantService(tenantId));
    var results = await handler.Handle(new GetCustomersQuery(), ct);

    Assert.Contains(results, c => c.Id == own.Id);
    Assert.DoesNotContain(results, c => c.Id == other.Id);
  }

  [Fact]
  public async Task Get_customer_by_id_returns_correct_dto()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var customer = SeedCustomer(db, tenantId, "Hanna", "Brown");

    var handler = new GetCustomerByIdHandler(db, new FakeCurrentTenantService(tenantId));
    var result = await handler.Handle(new GetCustomerByIdQuery(customer.Id), ct);

    Assert.Equal(customer.Id, result.Id);
    Assert.Equal("Hanna", result.FirstName);
  }

  [Fact]
  public async Task Get_customer_by_id_unknown_id_throws_not_found_exception()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var handler = new GetCustomerByIdHandler(db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new GetCustomerByIdQuery(Guid.NewGuid()), ct));
  }

  [Fact]
  public async Task Get_customer_by_id_cross_tenant_throws_not_found_exception()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = CreateDb();
    var other = SeedCustomer(db, Guid.NewGuid(), "Cross", "Tenant");

    var handler = new GetCustomerByIdHandler(db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new GetCustomerByIdQuery(other.Id), ct));
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
    string phone = "+48501000001")
  {
    var customer = new Customer(
      tenantId, firstName, lastName,
      $"{firstName.ToLower()}@example.com",
      new PhoneNumber(phone),
      string.Empty);
    db.Customers.Add(customer);
    db.SaveChanges();
    return customer;
  }

  private static DeleteCustomerHandler BuildDeleteHandler(ApplicationDbContext db, Guid tenantId)
    => new(db, db, new FakeCurrentTenantService(tenantId));

  private static Appointment SeedAppointmentForCustomer(
    ApplicationDbContext db, Guid tenantId, Guid customerId, string notes)
  {
    var appointment = new Appointment(
      tenantId, Guid.NewGuid(), Guid.NewGuid(), customerId,
      DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), new TimeOnly(10, 0), new TimeOnly(11, 0),
      AppointmentStatus.Completed, new Money(100m, "PLN"), notes, null);
    db.Appointments.Add(appointment);
    db.SaveChanges();
    return appointment;
  }

  // ── Fakes ─────────────────────────────────────────────────────────────────────────────────────

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }

}
