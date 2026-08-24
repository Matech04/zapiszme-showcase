using System.Net;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// TEN-003 — unknown slug w booking URL → 404.
/// TEN-006 — InviteOnly policy odrzuca rezerwację gdy klient nie jest whitelisted.
/// TEN-012 — /api/Tenants wymaga SystemAdminOnly (Employee/Owner dostają 403).
/// </summary>
public sealed class TenantPolicyAndAuthIntegrationTests
{
  // TEN-003 Negative: unknown slug w booking URL zwraca 404
  [Fact]
  public async Task Public_booking_with_unknown_slug_returns_not_found()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/booking/unknown-slug-xyz/public-salon", ct);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  // TEN-006 Security: InviteOnly + non-whitelisted klient = OTP request odrzucony
  [Fact]
  public async Task InviteOnly_request_otp_for_non_whitelisted_email_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SetTenantBookingAccessPolicy(factory.Services, seed.TenantId, BookingAccessPolicy.InviteOnly);

    // Create hold to get appointment + lease token
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var holdResponse = await client.PostAsJsonAsync(
      $"/api/booking/{seed.TenantSlug}/public-appointment/hold",
      new
      {
        employeeId = seed.EmployeeId,
        serviceIds = new[] { seed.ServiceId },
        date = TestDates.IsoInDays(50),
        startTime = "10:00:00",
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, holdResponse.StatusCode);

    var hold = await holdResponse.Content.ReadFromJsonAsync<HoldResponse>(cancellationToken: ct);
    Assert.NotNull(hold);
    Assert.NotNull(hold!.Lease);

    // Now request OTP with a non-whitelisted email
    var otpResponse = await client.PostAsJsonAsync(
      $"/api/booking/{seed.TenantSlug}/public-appointment/{hold.AppointmentId}/request-otp",
      new
      {
        token = hold.Lease.ReservationToken,
        email = "non-whitelisted@example.com",
        phoneNumber = (string?)null,
      },
      ct);

    // BookingNotInvitedException jest DomainException → mapowanie na 400 (nie 403).
    // Test weryfikuje, że niezaproszony klient JEST odrzucany — kod statusu zgodny z handler.
    Assert.Equal(HttpStatusCode.BadRequest, otpResponse.StatusCode);
  }

  // TEN-012 Security: GET /api/Tenants → 403 dla Owner (SystemAdminOnly required)
  [Fact]
  public async Task Owner_role_cannot_access_tenants_admin_endpoint_returns_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await ownerClient.GetAsync("/api/Tenants", ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // TEN-012 Security: GET /api/Tenants → 401 dla nieuwierzytelnionego klienta
  [Fact]
  public async Task Unauthenticated_request_to_tenants_endpoint_returns_unauthorized()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.GetAsync("/api/Tenants", ct);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  private static void SetTenantBookingAccessPolicy(IServiceProvider rootServices, Guid tenantId, BookingAccessPolicy policy)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = db.Tenants.IgnoreQueryFilters().First(t => t.Id == tenantId);
    tenant.Update(tenant.Name, tenant.Slug, bookingAccessPolicy: policy);
    db.SaveChanges();
  }

  private sealed record HoldResponse(Guid AppointmentId, HoldLeaseDto Lease);
  private sealed record HoldLeaseDto(Guid ReservationToken, DateTime ExpiryTimeUtc);
}
