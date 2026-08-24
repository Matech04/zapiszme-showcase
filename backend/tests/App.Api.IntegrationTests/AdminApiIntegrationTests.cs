using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.TenantAggregate;

namespace App.Api.IntegrationTests;

/// <summary>Endpointy wymagające roli Admin (np. <c>Tenants</c>).</summary>
public sealed class AdminApiIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  [Fact]
  public async Task Get_tenants_returns_ok_when_seeded()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Tenants", ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var list = await response.Content.ReadFromJsonAsync<List<TenantListItem>>(JsonRead, ct);
    Assert.NotNull(list);
    Assert.Contains(list, t => t.Slug == RestApiIntegrationSeed.TenantSlug);
  }

  private sealed record TenantListItem(Guid Id, string Name, string Slug, CustomerVerificationChannel CustomerVerificationChannel);
}
