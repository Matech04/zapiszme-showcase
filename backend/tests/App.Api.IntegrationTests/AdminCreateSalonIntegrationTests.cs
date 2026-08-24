using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// White-label onboarding z panelu admina: tworzenie salonu „za klienta" (tenant + właścicielka +
/// pracownice-zasoby + domena) oraz ustawianie/edycja custom domeny istniejącego tenanta.
/// </summary>
public sealed class AdminCreateSalonIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  [Fact]
  public async Task Admin_create_salon_creates_owner_staff_domain_and_is_resolvable()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync("/api/auth/admin/create-salon", new
    {
      salonName = "Salon Magdalena Nowak",
      salonSlug = "magdalena-nowak",
      timeZoneId = "Europe/Warsaw",
      currency = "PLN",
      ownerEmail = "salon@salon-przyklad.pl",
      ownerPassword = "Password123!",
      ownerFirstName = "Magdalena",
      ownerLastName = "Nowak",
      customDomain = "salon-przyklad.pl",
      staff = new[]
      {
        new { firstName = "Ania", lastName = "Kowalska", email = "ania@salon-przyklad.pl" },
        new { firstName = "Beata", lastName = "Nowak", email = "beata@salon-przyklad.pl" },
      },
    }, ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var created = await response.Content.ReadFromJsonAsync<CreateSalonResponse>(JsonRead, ct);
    Assert.NotNull(created);
    Assert.NotEqual(Guid.Empty, created.TenantId);

    // Pracownice: 1 właścicielka (z kontem) + 2 zasoby (bez logowania).
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var employees = await db.Employees.IgnoreQueryFilters().AsNoTracking()
        .Where(e => e.TenantId == created.TenantId)
        .ToListAsync(ct);
      Assert.Equal(3, employees.Count);
      Assert.Single(employees, e => e.UserId != null);          // właścicielka
      Assert.Equal(2, employees.Count(e => e.UserId == null));  // zasoby

      var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstAsync(t => t.Id == created.TenantId, ct);
      Assert.Equal("salon-przyklad.pl", tenant.CustomDomain);
    }

    // Endpoint odświeża rejestr → host od razu rozwiązywalny (bez czekania na cykliczny refresh).
    var resolve = await client.GetAsync(
      "/api/booking-domains/resolve?host=rezerwacja.salon-przyklad.pl", ct);
    Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
    var salon = await resolve.Content.ReadFromJsonAsync<ResolveResponse>(JsonRead, ct);
    Assert.NotNull(salon);
    Assert.Equal("magdalena-nowak", salon.Slug);
  }

  [Fact]
  public async Task Admin_create_salon_with_duplicate_domain_returns_409()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var client = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    object Body(string slug, string email) => new
    {
      salonName = "Salon " + slug,
      salonSlug = slug,
      timeZoneId = "Europe/Warsaw",
      currency = "PLN",
      ownerEmail = email,
      ownerPassword = "Password123!",
      ownerFirstName = "A",
      ownerLastName = "B",
      customDomain = "wspolna-domena.pl",
    };

    var first = await client.PostAsJsonAsync("/api/auth/admin/create-salon", Body("salon-a", "a@wspolna-domena.pl"), ct);
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);

    var second = await client.PostAsJsonAsync("/api/auth/admin/create-salon", Body("salon-b", "b@wspolna-domena.pl"), ct);
    Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
  }

  [Fact]
  public async Task Set_custom_domain_on_existing_tenant_makes_it_resolvable()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;

    Guid tenantId;
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var tenant = new Tenant("Istniejący Salon", "istniejacy-salon");
      db.Tenants.Add(tenant);
      await db.SaveChangesAsync(CancellationToken.None);
      tenantId = tenant.Id;
    }

    var client = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    var put = await client.PutAsJsonAsync($"/api/Tenants/{tenantId}/custom-domain",
      new { customDomain = "istniejacy-salon.pl" }, ct);
    Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

    var resolve = await client.GetAsync(
      "/api/booking-domains/resolve?host=rezerwacja.istniejacy-salon.pl", ct);
    Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
    var salon = await resolve.Content.ReadFromJsonAsync<ResolveResponse>(JsonRead, ct);
    Assert.NotNull(salon);
    Assert.Equal("istniejacy-salon", salon.Slug);
  }

  [Fact]
  public async Task Admin_can_add_resource_employee_to_existing_tenant_and_list_it()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;

    Guid tenantId;
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var tenant = new Tenant("Salon Z Pracownikami", "salon-z-pracownikami");
      db.Tenants.Add(tenant);
      await db.SaveChangesAsync(CancellationToken.None);
      tenantId = tenant.Id;
    }

    var client = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    var add = await client.PostAsJsonAsync($"/api/Tenants/{tenantId}/employees",
      new { firstName = "Ola", lastName = "Wiśniewska", email = "ola@salon.local" }, ct);
    Assert.Equal(HttpStatusCode.OK, add.StatusCode);

    var list = await client.GetFromJsonAsync<List<EmployeeListItem>>(
      $"/api/Tenants/{tenantId}/employees", JsonRead, ct);
    Assert.NotNull(list);
    // Dodana osoba to ZASÓB (bez konta logowania).
    Assert.Contains(list!, e => e.FirstName == "Ola" && e.LastName == "Wiśniewska" && !e.HasAccount);
  }

  private sealed record CreateSalonResponse(Guid TenantId, Guid OwnerUserId);
  private sealed record ResolveResponse(string Name, string Slug);
  private sealed record EmployeeListItem(Guid Id, string FirstName, string LastName, string Email, bool HasAccount);
}
