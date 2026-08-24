using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Tenants.Commands.DeleteTenant;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Tenants;

/// <summary>
/// DELETE-TENANT-001..003 — twardy purge salonu z panelu admina (DeleteTenantCommand + TenantPurgeService).
///
/// Bez wizyt (owned-type Money łamie EF InMemory) — pełny graf z wizytami pokrywa test integracyjny.
/// </summary>
public sealed class DeleteTenantHandlerTests
{
  [Fact]
  public async Task Deletes_tenant_with_employee_vat_rates_and_owner_account()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var (userId, tenantId) = SeedTenant(db, "owner@salon.local", "Salon A", "salon-a");

    var handler = new DeleteTenantCommandHandler(db, new TenantPurgeService(db));
    await handler.Handle(new DeleteTenantCommand(tenantId), ct);

    Assert.False(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct));
    Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenantId, ct));
    Assert.False(await db.VatRates.IgnoreQueryFilters().AnyAsync(v => v.TenantId == tenantId, ct));
    Assert.False(await db.Users.AnyAsync(u => u.Id == userId, ct));
  }

  [Fact]
  public async Task Throws_NotFound_for_unknown_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();
    var handler = new DeleteTenantCommandHandler(db, new TenantPurgeService(db));

    await Assert.ThrowsAsync<NotFoundException>(
      () => handler.Handle(new DeleteTenantCommand(Guid.NewGuid()), ct));
  }

  [Fact]
  public async Task Keeps_user_account_that_is_employee_of_another_tenant()
  {
    var ct = TestContext.Current.CancellationToken;
    var db = SetupAnonymousDb();

    // Jedno konto Identity przypisane do dwóch salonów (pracownik w A i w B).
    var user = NewUser("shared@salon.local");
    var tenantA = new Tenant("Salon A", "salon-a");
    var tenantB = new Tenant("Salon B", "salon-b");
    db.Users.Add(user);
    db.Tenants.AddRange(tenantA, tenantB);
    db.Employees.Add(new Employee(tenantA.Id, user.Id, "Ala", "A", user.Email!));
    db.Employees.Add(new Employee(tenantB.Id, user.Id, "Ala", "B", user.Email!));
    db.SaveChanges();

    var handler = new DeleteTenantCommandHandler(db, new TenantPurgeService(db));
    await handler.Handle(new DeleteTenantCommand(tenantA.Id), ct);

    // Salon A znika, ale konto użytkownika i salon B zostają — konto jest współdzielone.
    Assert.False(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantA.Id, ct));
    Assert.False(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenantA.Id, ct));
    Assert.True(await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantB.Id, ct));
    Assert.True(await db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenantB.Id, ct));
    Assert.True(await db.Users.AnyAsync(u => u.Id == user.Id, ct));
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static (Guid userId, Guid tenantId) SeedTenant(
    ApplicationDbContext db, string email, string name, string slug)
  {
    var user = NewUser(email);
    var tenant = new Tenant(name, slug);
    var employee = new Employee(tenant.Id, user.Id, "Jan", "Kowalski", email);

    db.Users.Add(user);
    db.Tenants.Add(tenant);
    db.Employees.Add(employee);
    new TenantVatRateSeeder(db, new VatRateCatalog()).SeedDefaults(tenant.Id, "PL");
    db.SaveChanges();
    return (user.Id, tenant.Id);
  }

  private static User NewUser(string email) => new(email, email)
  {
    EmailConfirmed = true,
    PhoneNumberConfirmed = true,
    NormalizedEmail = email.ToUpperInvariant(),
    NormalizedUserName = email.ToUpperInvariant(),
    SecurityStamp = Guid.NewGuid().ToString(),
    ConcurrencyStamp = Guid.NewGuid().ToString(),
  };

  private static ApplicationDbContext SetupAnonymousDb()
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new AnonymousCurrentTenantService());
  }

  private sealed class AnonymousCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId => null;
  }
}
