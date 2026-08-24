using App.Application.Common.Interfaces;
using App.Application.VatRates.Commands.CreateVatRate;
using App.Application.VatRates.Commands.DeleteVatRate;
using App.Application.VatRates.Commands.UpdateVatRate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.VatRates;

/// <summary>
/// APP-VAT — handlerowe testy VatRate: Create/Update z IsDefault=true czyści inne; Delete soft-delete; NotFound + TenantViolation.
/// </summary>
public sealed class VatRateHandlerTests
{
  // VAT-005: tworzenie z IsDefault=true czyści IsDefault z innych w tenancie
  [Fact]
  public async Task Create_with_isDefault_true_clears_default_from_other_tenant_vat_rates()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();

    var existing = new VatRate(tenantId, "Old default", 0.23m, isDefault: true);
    db.VatRates.Add(existing);
    db.SaveChanges();

    var handler = new CreateVatRateHandler(new VatRateRepository(db), db, new FakeCurrentTenantService(tenantId));

    var newId = await handler.Handle(new CreateVatRateCommand("New default", 0.08m, IsDefault: true), ct);

    var existingReloaded = await db.VatRates.FindAsync(new object[] { existing.Id }, ct);
    Assert.False(existingReloaded!.IsDefault);

    var newRate = await db.VatRates.FindAsync(new object[] { newId }, ct);
    Assert.True(newRate!.IsDefault);
  }

  // VAT-005: aktualizacja z IsDefault=true czyści default z innych
  [Fact]
  public async Task Update_with_isDefault_true_clears_default_from_other_tenant_vat_rates()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();

    var existing = new VatRate(tenantId, "Old default", 0.23m, isDefault: true);
    var another = new VatRate(tenantId, "Another", 0.05m, isDefault: false);
    db.VatRates.AddRange(existing, another);
    db.SaveChanges();

    var handler = new UpdateVatRateHandler(new VatRateRepository(db), db, new FakeCurrentTenantService(tenantId));

    await handler.Handle(new UpdateVatRateCommand(another.Id, "Another", 0.05m, IsDefault: true), ct);

    var existingReloaded = await db.VatRates.FindAsync(new object[] { existing.Id }, ct);
    var anotherReloaded = await db.VatRates.FindAsync(new object[] { another.Id }, ct);
    Assert.False(existingReloaded!.IsDefault);
    Assert.True(anotherReloaded!.IsDefault);
  }

  // VAT-008: Update cross-tenant — encja innego tenanta niewidoczna przez query filter → NotFound
  // (silniejsza izolacja niż TenantViolation; ręczny check pozostaje jako defense-in-depth).
  [Fact]
  public async Task Update_throws_NotFound_for_cross_tenant_vat_rate()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();
    var other = new VatRate(Guid.NewGuid(), "Other tenant", 0.23m);
    db.VatRates.Add(other);
    db.SaveChanges();

    var handler = new UpdateVatRateHandler(new VatRateRepository(db), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new UpdateVatRateCommand(other.Id, "Hacked", 0.10m, false), ct));
  }

  // VAT-008: Delete cross-tenant → NotFound (jak wyżej).
  [Fact]
  public async Task Delete_throws_NotFound_for_cross_tenant_vat_rate()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();
    var other = new VatRate(Guid.NewGuid(), "Other tenant", 0.23m);
    db.VatRates.Add(other);
    db.SaveChanges();

    var handler = new DeleteVatRateHandler(new VatRateRepository(db), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new DeleteVatRateCommand(other.Id), ct));
  }

  // VAT-003: Delete = soft-delete (IsActive=false, rekord zostaje)
  [Fact]
  public async Task Delete_soft_deletes_vat_rate()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();
    var vat = new VatRate(tenantId, "ToRemove", 0.23m);
    db.VatRates.Add(vat);
    db.SaveChanges();

    var handler = new DeleteVatRateHandler(new VatRateRepository(db), db, new FakeCurrentTenantService(tenantId));
    await handler.Handle(new DeleteVatRateCommand(vat.Id), ct);

    var reloaded = await db.VatRates.IgnoreQueryFilters().AsNoTracking().FirstAsync(v => v.Id == vat.Id, ct);
    Assert.False(reloaded.IsActive);
  }

  // VAT-007: Update unknown ID → NotFound
  [Fact]
  public async Task Update_throws_NotFound_for_unknown_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();
    var handler = new UpdateVatRateHandler(new VatRateRepository(db), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new UpdateVatRateCommand(Guid.NewGuid(), "X", 0.10m, false), ct));
  }

  [Fact]
  public async Task Delete_throws_NotFound_for_unknown_id()
  {
    var ct = TestContext.Current.CancellationToken;
    var (db, tenantId) = SetupDb();
    var handler = new DeleteVatRateHandler(new VatRateRepository(db), db, new FakeCurrentTenantService(tenantId));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new DeleteVatRateCommand(Guid.NewGuid()), ct));
  }

  // ── helpers ─────────────────────────────────────────────────────────────────────────────

  private static (ApplicationDbContext db, Guid tenantId) SetupDb()
  {
    var tenantId = Guid.NewGuid();
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    var db = new ApplicationDbContext(options, new FakeCurrentTenantService(tenantId));
    return (db, tenantId);
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; }
    public FakeCurrentTenantService(Guid tenantId) => TenantId = tenantId;
  }
}
