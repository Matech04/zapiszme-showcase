using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.Hubs;
using App.Api.E2eSupport;
using App.Api.Authentication;
using App.Application.Notifications;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.NotificationAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// NOTIF-RT-* — kanał in-app: zdarzenie → trwały wiersz Notification + push realtime;
/// API odczytu (lista / mark-read); hub SignalR (auth + dostarczenie do grupy tenanta).
/// Rate-limit globalny podniesiony — w Testing domyślnie 3/min, a te testy robią więcej żądań.
/// </summary>
public sealed class NotificationsRealtimeIntegrationTests
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  // NOTIF-RT-001: verify-OTP → trwały wiersz Notification + wywołany IRealtimeNotifier.
  [Fact]
  public async Task Verify_otp_persists_notification_row_and_invokes_realtime()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = WithRaisedRateLimit(baseFactory);
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, "notif-rt-salon",
      out var tenantId, out var appointmentId, out var leaseToken);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    await client.PostAsJsonAsync(
      $"/api/booking/notif-rt-salon/public-appointment/{appointmentId}/request-otp",
      new { token = leaseToken, phoneNumber = (string?)null, email = "booker@example.com" }, ct);
    var otpMailbox = factory.Services.GetRequiredService<TestBookingOtpMailbox>();
    var verify = await client.PostAsJsonAsync(
      $"/api/booking/notif-rt-salon/public-appointment/{appointmentId}/verify-otp",
      new { token = leaseToken, otp = otpMailbox.LastCode }, ct);
    Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var row = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
      .SingleAsync(n => n.TenantId == tenantId, ct);
    Assert.Equal(NotificationType.NewBookingToSalon, row.Type);
    Assert.Equal(appointmentId, row.ReferenceId);
    Assert.Null(row.ReadAtUtc);

    var realtime = factory.Services.GetRequiredService<TestRealtimeNotifier>();
    var call = Assert.Single(realtime.Calls);
    Assert.Equal(tenantId, call.TenantId);
    Assert.Equal(row.Id, call.Dto.Id);
  }

  // NOTIF-RT-005: tryb Manual → verify-OTP publikuje "rezerwacja czeka na potwierdzenie".
  [Fact]
  public async Task Verify_otp_in_manual_mode_persists_awaiting_confirmation_notification()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = WithRaisedRateLimit(baseFactory);
    _ = factory.Services;
    SeedPendingAppointmentWithLease(factory.Services, "notif-rt-manual",
      out var tenantId, out var appointmentId, out var leaseToken,
      AppointmentConfirmationMode.Manual);
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    await client.PostAsJsonAsync(
      $"/api/booking/notif-rt-manual/public-appointment/{appointmentId}/request-otp",
      new { token = leaseToken, phoneNumber = (string?)null, email = "booker@example.com" }, ct);
    var otpMailbox = factory.Services.GetRequiredService<TestBookingOtpMailbox>();
    var verify = await client.PostAsJsonAsync(
      $"/api/booking/notif-rt-manual/public-appointment/{appointmentId}/verify-otp",
      new { token = leaseToken, otp = otpMailbox.LastCode }, ct);
    Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = await db.Appointments.IgnoreQueryFilters().AsNoTracking()
      .SingleAsync(a => a.Id == appointmentId, ct);
    Assert.Equal(AppointmentStatus.Pending, appt.Status);

    var row = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
      .SingleAsync(n => n.TenantId == tenantId, ct);
    Assert.Equal(NotificationType.AwaitingConfirmationToSalon, row.Type);
  }

  // NOTIF-RT-002: API odczytu — lista, mark-read, read-all + izolacja tenanta ORAZ użytkownika.
  [Fact]
  public async Task Read_api_lists_and_marks_notifications_scoped_to_tenant_and_user()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = WithRaisedRateLimit(baseFactory);
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var other = RestApiIntegrationSeed.SeedSecondTenant(factory.Services);
    SeedNotification(factory.Services, seed.TenantId, "Powiadomienie A", IntegrationTestUserIds.SalonOwner);
    SeedNotification(factory.Services, seed.TenantId, "Powiadomienie koleżanki");
    SeedNotification(factory.Services, other.TenantId, "Obce powiadomienie");

    var client = OwnerClient(factory, IntegrationTestUserIds.SalonOwner);
    var ct = TestContext.Current.CancellationToken;

    var list = await client.GetFromJsonAsync<List<NotificationApiDto>>("/api/notifications", JsonOptions, ct);
    Assert.NotNull(list);
    // Dzwonek osobisty: ani powiadomienie koleżanki, ani obcego tenanta.
    var mine = Assert.Single(list!);
    Assert.Equal("Powiadomienie A", mine.Subject);
    Assert.False(mine.Read);

    var markRead = await client.PostAsync($"/api/notifications/{mine.Id}/read", null, ct);
    Assert.Equal(HttpStatusCode.NoContent, markRead.StatusCode);

    var afterRead = await client.GetFromJsonAsync<List<NotificationApiDto>>("/api/notifications", JsonOptions, ct);
    Assert.True(afterRead!.Single().Read);

    var readAll = await client.PostAsync("/api/notifications/read-all", null, ct);
    Assert.Equal(HttpStatusCode.NoContent, readAll.StatusCode);

    // Izolacja: wiersz obcego tenanta nietknięty; read-all nie gasi dzwonka koleżance.
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var foreign = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
      .SingleAsync(n => n.TenantId == other.TenantId, ct);
    Assert.Null(foreign.ReadAtUtc);

    var colleagues = await db.Notifications.IgnoreQueryFilters().AsNoTracking()
      .SingleAsync(n => n.Subject == "Powiadomienie koleżanki", ct);
    Assert.Null(colleagues.ReadAtUtc);
  }

  // NOTIF-RT-002b: konto „Recepcja" (Kiosk) widzi powiadomienia całego salonu — nie ma własnego
  // kalendarza, więc osobisty dzwonek byłby dla niego pusty.
  [Fact]
  public async Task Read_api_desk_account_sees_whole_salon()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = WithRaisedRateLimit(baseFactory);
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    SeedNotification(factory.Services, seed.TenantId, "Powiadomienie A", IntegrationTestUserIds.SalonOwner);
    SeedNotification(factory.Services, seed.TenantId, "Powiadomienie koleżanki");

    var kiosk = KioskClient(factory);
    var ct = TestContext.Current.CancellationToken;

    var list = await kiosk.GetFromJsonAsync<List<NotificationApiDto>>("/api/notifications", JsonOptions, ct);

    Assert.NotNull(list);
    Assert.Equal(2, list!.Count);
  }

  // NOTIF-RT-002c: nie-właściciel (rola Employee, WŁASNE konto powiązane z osobnym pracownikiem)
  // dostaje SWOJE powiadomienie przez /api/notifications — pełna ścieżka „RecipientUserId → odczyt"
  // dla nie-właściciela. Izolacja obustronna: pracownica nie widzi powiadomienia właścicielki,
  // a właścicielka pracownicy.
  [Fact]
  public async Task Read_api_non_owner_employee_sees_only_their_own_notifications()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = WithRaisedRateLimit(baseFactory);
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    // Osobne konto pracownicy (nie właścicielka) powiązane z pracownikiem w tym samym tenancie.
    var staffUserId = Guid.NewGuid();
    SeedEmployeeWithAccount(factory.Services, seed.TenantId, staffUserId, "staff@rest-seed.local");
    SeedNotification(factory.Services, seed.TenantId, "Dla pracownicy", staffUserId);
    SeedNotification(factory.Services, seed.TenantId, "Dla właścicielki", IntegrationTestUserIds.SalonOwner);

    var ct = TestContext.Current.CancellationToken;

    // Pracownica (rola Employee) widzi WYŁĄCZNIE swoje — dowód, że powiadomienia trafiają do
    // nie-właściciela i że dzwonek jest osobisty także dla niego.
    var staffClient = StaffClient(factory, staffUserId, "Employee");
    var staffList = await staffClient.GetFromJsonAsync<List<NotificationApiDto>>("/api/notifications", JsonOptions, ct);
    Assert.NotNull(staffList);
    var staffMine = Assert.Single(staffList!);
    Assert.Equal("Dla pracownicy", staffMine.Subject);

    // Właścicielka nie widzi powiadomienia pracownicy (i odwrotnie powyżej).
    var owner = OwnerClient(factory, IntegrationTestUserIds.SalonOwner);
    var ownerList = await owner.GetFromJsonAsync<List<NotificationApiDto>>("/api/notifications", JsonOptions, ct);
    Assert.NotNull(ownerList);
    var ownerMine = Assert.Single(ownerList!);
    Assert.Equal("Dla właścicielki", ownerMine.Subject);
  }

  // NOTIF-RT-003: hub wymaga uwierzytelnienia — bez nagłówków negotiate zwraca 401.
  [Fact]
  public async Task Hub_rejects_unauthenticated_connection()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = WithRaisedRateLimit(baseFactory);
    _ = factory.Services;
    var ct = TestContext.Current.CancellationToken;

    await using var connection = BuildHubConnection(factory, userId: null);

    await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync(ct));
  }

  // NOTIF-RT-004: uwierzytelniony pracownik łączy się i odbiera push wysłany do JEGO grupy
  // osobistej (`user-{userId}`) — dzwonek nie jest już broadcastem po tenantcie.
  [Fact]
  public async Task Hub_authenticated_connection_receives_own_user_group_push()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = WithRaisedRateLimit(baseFactory);
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var ct = TestContext.Current.CancellationToken;

    await using var connection = BuildHubConnection(factory, IntegrationTestUserIds.SalonOwner);

    var received = new TaskCompletionSource<RealtimeNotificationDto>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    connection.On<RealtimeNotificationDto>("ReceiveNotification", dto => received.TrySetResult(dto));

    await connection.StartAsync(ct);
    Assert.Equal(HubConnectionState.Connected, connection.State);

    var dto = new RealtimeNotificationDto(
      Guid.NewGuid(), seed.TenantId, (int)NotificationType.NewBookingToSalon,
      "Push", "Treść", null, DateTime.UtcNow, Read: false);
    var hubContext = factory.Services.GetRequiredService<IHubContext<NotificationHub>>();
    await hubContext.Clients.Group(NotificationHub.UserGroupName(IntegrationTestUserIds.SalonOwner))
      .SendAsync("ReceiveNotification", dto, ct);

    var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
    Assert.Equal("Push", got.Subject);
    Assert.Equal(seed.TenantId, got.TenantId);
  }

  // ── helpers ────────────────────────────────────────────────────────────────────────────

  private static WebApplicationFactory<Program> WithRaisedRateLimit(BookingApiApplicationFactory baseFactory)
    => baseFactory.WithWebHostBuilder(builder =>
      builder.UseSetting("RateLimiting:Global:PermitLimit", "10000"));

  private static HttpClient OwnerClient(WebApplicationFactory<Program> factory, Guid userId)
  {
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.UserId, userId.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, "Owner");
    return client;
  }

  /// <summary>Dowolna rola panelu (Owner/Manager/Employee) na wskazanym koncie użytkownika.</summary>
  private static HttpClient StaffClient(WebApplicationFactory<Program> factory, Guid userId, string role)
  {
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.UserId, userId.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, role);
    return client;
  }

  /// <summary>Konto „Recepcja" — ten sam user id co owner (tenant rozwiązywany przez pracownika), rola Kiosk.</summary>
  private static HttpClient KioskClient(WebApplicationFactory<Program> factory)
  {
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(
      IntegrationTestAuthHeaders.UserId, IntegrationTestUserIds.SalonOwner.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, "Kiosk");
    return client;
  }

  private static HubConnection BuildHubConnection(WebApplicationFactory<Program> factory, Guid? userId)
  {
    var server = factory.Server;
    return new HubConnectionBuilder()
      .WithUrl(new Uri(server.BaseAddress, "hubs/notifications"), o =>
      {
        o.HttpMessageHandlerFactory = _ => server.CreateHandler();
        o.Transports = HttpTransportType.LongPolling; // TestServer nie obsługuje WebSockets
        if (userId is { } id)
        {
          o.Headers.Add(IntegrationTestAuthHeaders.UserId, id.ToString());
          o.Headers.Add(IntegrationTestAuthHeaders.Roles, "Owner");
        }
      })
      .Build();
  }

  /// <summary>
  /// Dzwonek jest osobisty — odbiorca decyduje, kto powiadomienie zobaczy. Domyślnie losowy
  /// (powiadomienie „czyjeś"); podaj `recipientUserId`, żeby przypisać je konkretnemu kontu.
  /// </summary>
  private static void SeedNotification(
    IServiceProvider rootServices,
    Guid tenantId,
    string subject,
    Guid? recipientUserId = null)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Notifications.Add(Notification.Create(
      tenantId, recipientUserId ?? Guid.NewGuid(), NotificationType.NewBookingToSalon, subject, "...", null, DateTime.UtcNow));
    db.SaveChanges();
  }

  /// <summary>Dodaje do tenanta pracownika z WŁASNYM kontem (UserId) — nie-właściciela z loginem.</summary>
  private static Guid SeedEmployeeWithAccount(
    IServiceProvider rootServices, Guid tenantId, Guid userId, string email)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var employee = new Employee(tenantId, TestIdentity.Ensure(db, userId, email), "Nowa", "Pracownica", email);
    db.Employees.Add(employee);
    db.SaveChanges();
    return employee.Id;
  }

  private static void SeedPendingAppointmentWithLease(
    IServiceProvider rootServices,
    string slug,
    out Guid tenantId,
    out Guid appointmentId,
    out Guid leaseToken,
    AppointmentConfirmationMode confirmationMode = AppointmentConfirmationMode.Automatic)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Notif RT Salon", slug);
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email,
      appointmentConfirmationMode: confirmationMode);

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    // Pracownik z UserId — inaczej kanał in-app pominąłby powiadomienie NewBookingToSalon.
    var employee = new Employee(tenant.Id, TestIdentity.Ensure(db, Guid.NewGuid(), "eva@test.local"), "Eva", "Test", "eva@test.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Service", new Money(50m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes,
      new Money(service.Price.Amount, service.Price.Currency));

    leaseToken = Guid.NewGuid();
    var lease = new HoldLease(leaseToken, DateTime.UtcNow.AddHours(4));
    var appointment = new Appointment(
      tenant.Id, employee.Id, service.Id, customerId: null,
      TestDates.InDays(15), new TimeOnly(9, 0), new TimeOnly(10, 0),
      AppointmentStatus.Pending, new Money(50m, "PLN"), string.Empty, lease);

    tenantId = tenant.Id;
    appointmentId = appointment.Id;

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Appointments.Add(appointment);
    db.SaveChanges();
  }

  private sealed record NotificationApiDto(
    Guid Id, int Type, string Subject, string ShortText, Guid? ReferenceId, DateTime CreatedAtUtc, bool Read);
}
