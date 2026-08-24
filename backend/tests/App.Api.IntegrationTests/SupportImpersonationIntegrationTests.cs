using System.Net;
using System.Net.Http.Json;
using App.Api.Authentication;
using App.Api.E2eSupport;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Support impersonation: admin platformy uzyskuje czasowy, audytowany dostęp do tenanta.
/// Bramką jest rozwiązanie tenanta (cookie + aktywna sesja w bazie), nie sama rola Admin.
/// </summary>
public sealed class SupportImpersonationIntegrationTests
{
  private sealed record EmployeeListItem(Guid Id);

  private sealed record NotificationItem(Guid Id, int Type, string Subject);

  private sealed record StartResult(Guid SessionId, Guid TenantId, DateTime ExpiresAtUtc, bool IsReadOnly);

  // Diagnostyka: admin w trybie wsparcia widzi dzwonek CAŁEGO salonu (jak Recepcja), mimo że żadne
  // powiadomienie nie jest jego. Bez tego GET /api/notifications filtrowałby po id admina → pusto.
  [Fact]
  public async Task Impersonation_notification_bell_shows_whole_salon_like_desk()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SeedSalonNotification(factory.Services, seed.TenantId, "Powiadomienie właścicielki", IntegrationTestUserIds.SalonOwner);
    SeedSalonNotification(factory.Services, seed.TenantId, "Powiadomienie pracownicy", Guid.NewGuid());
    var adminClient = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    var start = await adminClient.PostAsJsonAsync(
      "/api/admin/impersonation",
      new { tenantId = seed.TenantId, reason = "Diagnostyka dzwonka", readOnly = true },
      ct);
    Assert.Equal(HttpStatusCode.OK, start.StatusCode);

    var list = await adminClient.GetFromJsonAsync<List<NotificationItem>>("/api/notifications", ct);

    Assert.NotNull(list);
    Assert.Equal(2, list!.Count);
  }

  private static void SeedSalonNotification(
    IServiceProvider rootServices, Guid tenantId, string subject, Guid recipientUserId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Notifications.Add(Notification.Create(
      tenantId, recipientUserId, NotificationType.NewBookingToSalon, subject, "...", null, DateTime.UtcNow));
    db.SaveChanges();
  }

  [Fact]
  public async Task Admin_without_session_cannot_read_tenant_data()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var adminClient = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await adminClient.GetAsync("/api/Employees", ct);

    // Admin przechodzi autoryzację (rola w politykach), ale bez sesji nie ma TenantId → NoTenantHeader.
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Admin_with_active_session_reads_target_tenant_data()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var adminClient = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    var start = await adminClient.PostAsJsonAsync(
      "/api/admin/impersonation",
      new { tenantId = seed.TenantId, reason = "Pomoc z konfiguracją", readOnly = false },
      ct);
    Assert.Equal(HttpStatusCode.OK, start.StatusCode);

    var response = await adminClient.GetAsync("/api/Employees", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var list = await response.Content.ReadFromJsonAsync<List<EmployeeListItem>>(cancellationToken: ct);
    Assert.NotNull(list);
    Assert.Contains(list!, e => e.Id == seed.EmployeeId);
  }

  [Fact]
  public async Task Impersonation_preserves_tenant_isolation()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var first = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var adminClient = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    await adminClient.PostAsJsonAsync(
      "/api/admin/impersonation",
      new { tenantId = first.TenantId, reason = "Wsparcie pierwszego salonu", readOnly = false },
      ct);

    var list = await (await adminClient.GetAsync("/api/Employees", ct))
      .Content.ReadFromJsonAsync<List<EmployeeListItem>>(cancellationToken: ct);

    Assert.NotNull(list);
    Assert.Contains(list!, e => e.Id == first.EmployeeId);
    Assert.DoesNotContain(list!, e => e.Id == second.EmployeeId);
  }

  [Fact]
  public async Task ReadOnly_session_blocks_mutations_but_allows_reads()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var adminClient = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    await adminClient.PostAsJsonAsync(
      "/api/admin/impersonation",
      new { tenantId = seed.TenantId, reason = "Tylko podgląd ustawień", readOnly = true },
      ct);

    var read = await adminClient.GetAsync("/api/Employees", ct);
    Assert.Equal(HttpStatusCode.OK, read.StatusCode);

    var mutate = await adminClient.PostAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/leaves",
      new { startDate = TestDates.IsoInDays(30), endDate = TestDates.IsoInDays(31) },
      ct);
    Assert.Equal(HttpStatusCode.Forbidden, mutate.StatusCode);
  }

  [Fact]
  public async Task Ended_session_no_longer_grants_access()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var adminClient = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    // Uwaga: w Testing globalny rate limit = 3 żądania/okno per partycja — utrzymujemy ≤ 3.
    await adminClient.PostAsJsonAsync(
      "/api/admin/impersonation",
      new { tenantId = seed.TenantId, reason = "Krótka pomoc", readOnly = false },
      ct);

    var end = await adminClient.DeleteAsync("/api/admin/impersonation", ct);
    Assert.Equal(HttpStatusCode.NoContent, end.StatusCode);

    // Cookie skasowany + sesja zakończona → brak kontekstu tenanta.
    var afterEnd = await adminClient.GetAsync("/api/Employees", ct);
    Assert.Equal(HttpStatusCode.BadRequest, afterEnd.StatusCode);
  }

  [Fact]
  public async Task Non_admin_cannot_start_impersonation()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ownerClient = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var start = await ownerClient.PostAsJsonAsync(
      "/api/admin/impersonation",
      new { tenantId = seed.TenantId, reason = "Próba bez uprawnień", readOnly = false },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
  }

  [Fact]
  public async Task Impersonation_cookie_is_inert_for_non_admin()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var first = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var adminClient = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    // Admin startuje sesję dla DRUGIEGO salonu i zdobywa cookie.
    var start = await adminClient.PostAsJsonAsync(
      "/api/admin/impersonation",
      new { tenantId = second.TenantId, reason = "Wsparcie drugiego salonu", readOnly = false },
      ct);
    Assert.Equal(HttpStatusCode.OK, start.StatusCode);
    var impersonationCookie = ExtractImpersonationCookie(start);
    Assert.NotNull(impersonationCookie);

    // Owner pierwszego salonu podstawia cudze cookie — middleware ignoruje (brak roli Admin).
    var ownerClient = factory.CreateClient();
    ownerClient.DefaultRequestHeaders.TryAddWithoutValidation(
      IntegrationTestAuthHeaders.UserId, IntegrationTestUserIds.SalonOwner.ToString());
    ownerClient.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, "Owner");
    ownerClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", impersonationCookie!);

    var list = await (await ownerClient.GetAsync("/api/Employees", ct))
      .Content.ReadFromJsonAsync<List<EmployeeListItem>>(cancellationToken: ct);

    Assert.NotNull(list);
    // Owner widzi WYŁĄCZNIE swój (pierwszy) salon — cookie nie podmieniło tenanta.
    Assert.Contains(list!, e => e.Id == first.EmployeeId);
    Assert.DoesNotContain(list!, e => e.Id == second.EmployeeId);
  }

  private sealed record ServiceItem(Guid Id);

  [Fact]
  public async Task Impersonation_does_not_override_tenant_on_public_booking()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var first = RestApiIntegrationSeed.Seed(factory.Services);
    var second = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    var adminClient = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    // Admin startuje sesję wsparcia DRUGIEGO salonu (cookie ląduje w kliencie).
    var start = await adminClient.PostAsJsonAsync(
      "/api/admin/impersonation",
      new { tenantId = second.TenantId, reason = "Wsparcie drugiego salonu", readOnly = false },
      ct);
    Assert.Equal(HttpStatusCode.OK, start.StatusCode);

    // Publiczny kalendarz PIERWSZEGO salonu (slug w URL) — mimo aktywnej impersonacji DRUGIEGO,
    // usługi MUSZĄ być pierwszego salonu (tenant wyłącznie ze slugu, nie z sesji wsparcia).
    // Regresja: ImpersonationMiddleware nadpisywał tenant też na /api/booking/* → wyciek usług
    // impersonowanego salonu na cudzą publiczną stronę rezerwacji.
    var response = await adminClient.GetAsync($"/api/booking/{first.TenantSlug}/services", ct);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var services = await response.Content.ReadFromJsonAsync<List<ServiceItem>>(cancellationToken: ct);
    Assert.NotNull(services);
    Assert.Contains(services!, s => s.Id == first.ServiceId);
    Assert.DoesNotContain(services!, s => s.Id == second.ServiceId);
  }

  private static string? ExtractImpersonationCookie(HttpResponseMessage response)
  {
    if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
    {
      return null;
    }

    var raw = cookies.FirstOrDefault(c => c.StartsWith("impersonation=", StringComparison.Ordinal));
    // Tylko para nazwa=wartość (bez atrybutów Path/Expires...).
    return raw?.Split(';', 2)[0];
  }
}
