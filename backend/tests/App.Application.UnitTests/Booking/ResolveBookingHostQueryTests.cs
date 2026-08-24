using App.Application.Booking.BookingSalon.Queries;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Application.UnitTests.Booking;

public class ResolveBookingHostQueryTests
{
  [Fact]
  public async Task Resolve_KnownHost_ReturnsSlugAndSalonInfo()
  {
    using var db = CreateContext();
    var tenant = new Tenant("Magdalena Nowak", "magdalena-nowak");
    tenant.SetCustomDomain("salon-przyklad.pl");
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync();

    var handler = new ResolveBookingHostQueryHandler(db, new FakeCustomDomainRegistry(managed: true));

    var result = await handler.Handle(
      new ResolveBookingHostQuery("rezerwacja.salon-przyklad.pl"), default);

    Assert.Equal("magdalena-nowak", result.Slug);
    Assert.Equal("Magdalena Nowak", result.Name);
    Assert.True(result.IsBookingAvailable); // świeży tenant = aktywny trial
  }

  [Fact]
  public async Task Resolve_UnknownHost_RejectedByRegistry_ThrowsNotFound_WithoutDbHit()
  {
    using var db = CreateContext();
    // Brak tenantów w bazie; rejestr odrzuca → handler nie powinien w ogóle pytać bazy.
    var handler = new ResolveBookingHostQueryHandler(db, new FakeCustomDomainRegistry(managed: false));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new ResolveBookingHostQuery("rezerwacja.cudza-domena.pl"), default));
  }

  [Fact]
  public async Task Resolve_ManagedHostButNoTenantRow_ThrowsNotFound()
  {
    using var db = CreateContext();
    // Rejestr mówi "zarządzany" (stale snapshot), ale w bazie brak rekordu → 404.
    var handler = new ResolveBookingHostQueryHandler(db, new FakeCustomDomainRegistry(managed: true));

    await Assert.ThrowsAsync<NotFoundException>(() =>
      handler.Handle(new ResolveBookingHostQuery("rezerwacja.salon-przyklad.pl"), default));
  }

  private static ApplicationDbContext CreateContext()
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseInMemoryDatabase(Guid.NewGuid().ToString())
      .Options;
    return new ApplicationDbContext(options, new FakeCurrentTenantService());
  }

  private sealed class FakeCurrentTenantService : ICurrentTenantService
  {
    public Guid? TenantId => null;
  }

  private sealed class FakeCustomDomainRegistry : ICustomDomainRegistry
  {
    private readonly bool _managed;
    public FakeCustomDomainRegistry(bool managed) => _managed = managed;

    public bool IsManagedHost(string? host) => _managed;
    public bool IsAllowedBookingOrigin(string? origin) => _managed;
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  }
}
