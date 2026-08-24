using System.Net;
using System.Net.Http.Json;
using System.Text;
using App.Api.Authentication;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.ShortLinkAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Infrastructure.BackgroundJobs;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Api.IntegrationTests;

/// <summary>
/// Zadatki — testy integracyjne na realnym pipeline HTTP + EF (FakePaymentProvider podmieniony
/// w fabryce). Pokrywają: generowanie krótkiego linku, fallback gdy konto niegotowe, webhook
/// (potwierdzenie + idempotencja + nieznana wizyta) oraz cleanup wygasłych linków.
/// </summary>
public sealed class DepositPaymentIntegrationTests
{
  private const string WebhookPath = "/api/payments/webhook/stripe";

  [Fact]
  public async Task GenerateDepositLink_returns_short_link_and_sets_awaiting_payment()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedSalon(factory.Services, "dep-gen-ok", chargesEnabled: true, out var appointmentId);

    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/Deposits/{appointmentId}/link", new { amountOverride = (decimal?)null }, ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var result = await response.Content.ReadFromJsonAsync<GenerateLinkResult>(cancellationToken: ct);
    Assert.NotNull(result);
    Assert.Contains("/p/", result!.Url);
    Assert.Equal(30m, result.Amount); // 30% z 100 zł

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var appt = await db.Appointments.IgnoreQueryFilters().AsNoTracking()
      .FirstAsync(a => a.Id == appointmentId, ct);
    Assert.Equal(AppointmentPaymentStatus.AwaitingPayment, appt.PaymentStatus);
    Assert.False(string.IsNullOrEmpty(appt.PaymentSessionId));

    var shortLink = await db.ShortLinks.IgnoreQueryFilters().AsNoTracking()
      .FirstOrDefaultAsync(l => l.AppointmentId == appointmentId, ct);
    Assert.NotNull(shortLink);
    Assert.EndsWith(shortLink!.Code, result.Url);
  }

  [Fact]
  public async Task GenerateDepositLink_on_custom_domain_uses_rezerwacja_host()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedSalon(factory.Services, "dep-whitelabel", chargesEnabled: true, out var appointmentId);
    SetCustomDomain(factory.Services, "dep-whitelabel", "salon-nowak.pl");

    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/Deposits/{appointmentId}/link", new { amountOverride = (decimal?)null }, ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var result = await response.Content.ReadFromJsonAsync<GenerateLinkResult>(cancellationToken: ct);
    Assert.NotNull(result);
    // Krótki link (do klientki) na domenie salonu, nie na platformie.
    Assert.StartsWith("https://rezerwacja.salon-nowak.pl/p/", result!.Url);

    // Success/cancel (przekazane do Stripe) też na domenie salonu.
    var provider = factory.Services.GetRequiredService<FakePaymentProvider>();
    Assert.NotNull(provider.LastRequest);
    Assert.StartsWith("https://rezerwacja.salon-nowak.pl/platnosc/sukces", provider.LastRequest!.SuccessUrl);
    Assert.StartsWith("https://rezerwacja.salon-nowak.pl/platnosc/anulowano", provider.LastRequest!.CancelUrl);
  }

  [Fact]
  public async Task GenerateDepositLink_when_account_not_ready_returns_error()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedSalon(factory.Services, "dep-gen-notready", chargesEnabled: false, out var appointmentId);

    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/Deposits/{appointmentId}/link", new { amountOverride = (decimal?)null }, ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken: ct);
    Assert.Equal("merchant_account.not_ready", problem?.ErrorCode);
  }

  [Fact]
  public async Task GenerateDepositLink_employee_on_own_appointment_succeeds()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    // SeedSalon przypisuje wizytę pracownikowi powiązanemu z SalonOwner (= caller w CreateEmployeeClient).
    SeedSalon(factory.Services, "dep-emp-own", chargesEnabled: true, out var appointmentId);

    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/Deposits/{appointmentId}/link", new { amountOverride = (decimal?)null }, ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task GenerateDepositLink_employee_on_other_appointment_forbidden()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var appointmentId = SeedSalonAppointmentOwnedByOtherEmployee(factory.Services, "dep-emp-other");

    var client = factory.CreateEmployeeClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/Deposits/{appointmentId}/link", new { amountOverride = (decimal?)null }, ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  /// <summary>
  /// Recepcja (Kiosk) obsługuje wizyty całego zespołu i nie ma własnego kalendarza — musi móc
  /// wygenerować link do zadatku dla cudzej wizyty. Wcześniej dostawała 403 mimo GeneralAccess,
  /// bo helper w DepositsController nie wymieniał roli Kiosk (w przeciwieństwie do wizyt).
  /// </summary>
  [Fact]
  public async Task GenerateDepositLink_kiosk_on_other_appointment_allowed()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var appointmentId = SeedSalonAppointmentOwnedByOtherEmployee(factory.Services, "dep-kiosk-other");

    var client = factory.CreateKioskClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/Deposits/{appointmentId}/link", new { amountOverride = (decimal?)null }, ct);

    Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task GenerateDepositLink_when_already_paid_returns_error()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedSalon(factory.Services, "dep-gen-paid", chargesEnabled: true, out var appointmentId);
    MarkAppointmentPaid(factory.Services, appointmentId);

    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/Deposits/{appointmentId}/link", new { amountOverride = (decimal?)null }, ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken: ct);
    Assert.Equal("deposit.already_paid", problem?.ErrorCode);
  }

  [Fact]
  public async Task GetAppointmentById_exposes_payment_status_after_paid()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedSalon(factory.Services, "dep-get-paid", chargesEnabled: true, out var appointmentId);
    MarkAppointmentPaid(factory.Services, appointmentId);

    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var dto = await client.GetFromJsonAsync<AppointmentDetailResponse>(
      $"/api/Appointments/{appointmentId}", ct);

    Assert.NotNull(dto);
    Assert.Equal("Paid", dto!.PaymentStatus);
    Assert.NotNull(dto.PaidAtUtc);
    Assert.Equal(30m, dto.DepositAmount?.Amount);
  }

  [Fact]
  public async Task SendDepositLink_via_email_succeeds()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var appointmentId = SeedSalonWithCustomerAndLink(
      factory.Services, "dep-send-ok", CustomerVerificationChannel.Email);

    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/Deposits/{appointmentId}/send", new { channel = (int?)null }, ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var result = await response.Content.ReadFromJsonAsync<SendResult>(cancellationToken: ct);
    Assert.Equal("Email", result?.Channel);
  }

  [Fact]
  public async Task SendDepositLink_when_no_link_returns_error()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedSalon(factory.Services, "dep-send-nolink", chargesEnabled: true, out var appointmentId);

    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      $"/api/Deposits/{appointmentId}/send", new { channel = (int?)null }, ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken: ct);
    Assert.Equal("deposit.link_not_generated", problem?.ErrorCode);
  }

  [Fact]
  public async Task GenerateDepositLink_amount_override_above_total_returns_error()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    SeedSalon(factory.Services, "dep-over-cap", chargesEnabled: true, out var appointmentId);

    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    // Cena wizyty = 100 zł; override 150 zł musi zostać odrzucony.
    var response = await client.PostAsJsonAsync(
      $"/api/Deposits/{appointmentId}/link", new { amountOverride = 150m }, ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(cancellationToken: ct);
    Assert.Equal("deposit.amount_exceeds_total", problem?.ErrorCode);
  }

  [Fact]
  public async Task Webhook_with_mismatched_tenant_is_noop()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var (appointmentId, sessionId) = SeedAwaitingPaymentAppointment(factory.Services, "dep-wh-tenant");

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // tenantId w metadanych ≠ tenant wizyty → handler musi zignorować (defense-in-depth cross-tenant).
    var body = $$"""
      {"type":"PaymentSucceeded","sessionId":"{{sessionId}}","appointmentId":"{{appointmentId}}","tenantId":"{{Guid.NewGuid()}}"}
      """;
    var response = await client.PostAsync(
      WebhookPath, new StringContent(body, Encoding.UTF8, "application/json"), ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = await db.Appointments.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == appointmentId, ct);
    Assert.Equal(AppointmentPaymentStatus.AwaitingPayment, appt.PaymentStatus); // NIE opłacona
  }

  [Fact]
  public async Task Webhook_with_mismatched_connected_account_is_noop()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var (appointmentId, sessionId) = SeedAwaitingPaymentAppointment(factory.Services, "dep-wh-acct");

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // connectedAccountId ≠ konto salonu (acct_test) → druga bramka webhooka odrzuca.
    var body = $$"""
      {"type":"PaymentSucceeded","sessionId":"{{sessionId}}","appointmentId":"{{appointmentId}}","connectedAccountId":"acct_other"}
      """;
    var response = await client.PostAsync(
      WebhookPath, new StringContent(body, Encoding.UTF8, "application/json"), ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = await db.Appointments.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == appointmentId, ct);
    Assert.Equal(AppointmentPaymentStatus.AwaitingPayment, appt.PaymentStatus); // NIE opłacona
  }

  [Fact]
  public async Task Webhook_payment_succeeded_marks_deposit_paid()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var (appointmentId, sessionId) = SeedAwaitingPaymentAppointment(factory.Services, "dep-wh-paid");

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsync(WebhookPath, WebhookBody("PaymentSucceeded", sessionId, appointmentId), ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = await db.Appointments.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == appointmentId, ct);
    Assert.Equal(AppointmentPaymentStatus.Paid, appt.PaymentStatus);
    Assert.NotNull(appt.PaidAtUtc);
  }

  [Fact]
  public async Task Webhook_is_idempotent()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var (appointmentId, sessionId) = SeedAwaitingPaymentAppointment(factory.Services, "dep-wh-idem");

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var first = await client.PostAsync(WebhookPath, WebhookBody("PaymentSucceeded", sessionId, appointmentId), ct);
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);

    DateTime? paidAtAfterFirst;
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      paidAtAfterFirst = (await db.Appointments.IgnoreQueryFilters().AsNoTracking()
        .FirstAsync(a => a.Id == appointmentId, ct)).PaidAtUtc;
    }

    var second = await client.PostAsync(WebhookPath, WebhookBody("PaymentSucceeded", sessionId, appointmentId), ct);
    Assert.Equal(HttpStatusCode.OK, second.StatusCode);

    using var scope2 = factory.Services.CreateScope();
    var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = await db2.Appointments.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == appointmentId, ct);
    Assert.Equal(AppointmentPaymentStatus.Paid, appt.PaymentStatus);
    Assert.Equal(paidAtAfterFirst, appt.PaidAtUtc); // bez zmiany — drugi webhook to no-op
  }

  [Fact]
  public async Task Webhook_flood_is_rate_limited()
  {
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(b =>
      b.ConfigureAppConfiguration((_, cfg) =>
        cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["RateLimiting:Webhook:PermitLimit"] = "3",
          ["RateLimiting:Webhook:WindowSeconds"] = "60",
        })));

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var statuses = new List<HttpStatusCode>();
    for (var i = 0; i < 6; i++)
    {
      var r = await client.PostAsync(
        WebhookPath, WebhookBody("PaymentSucceeded", "cs_x", Guid.NewGuid()), ct);
      statuses.Add(r.StatusCode);
    }

    // Limit 3/okno → przy 6 żądaniach z tego samego IP min. jedno musi dostać 429.
    Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
  }

  [Fact]
  public async Task Webhook_for_unknown_appointment_is_noop()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;

    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsync(
      WebhookPath, WebhookBody("PaymentSucceeded", "cs_unknown", Guid.NewGuid()), ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task AnonymizeCustomer_scrubs_payment_link_and_shortlinks()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var now = DateTime.UtcNow;

    Guid customerId, appointmentId;
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var tenant = new Tenant("Anon Salon", "dep-anon");
      var category = new ServiceCategory(tenant.Id, "Cat", 0);
      var vat = new VatRate(tenant.Id, "VAT", 0.23m);
      var ownerEmployee = new Employee(tenant.Id, TestIdentity.Ensure(db, IntegrationTestUserIds.SalonOwner, "owner@anon.local"), "Owner", "Test", "owner@anon.local");
      var service = new Service(tenant.Id, category.Id, vat.Id, "Usługa", new Money(100m, "PLN"), 30);
      ownerEmployee.AssignService(tenant.Id, service.Id, service.DurationInMinutes,
        new Money(service.Price.Amount, service.Price.Currency));
      var customer = new Customer(tenant.Id, "Jan", "Kowalski", "jan@klient.local", null, string.Empty);

      var appt = new Appointment(
        tenant.Id, ownerEmployee.Id, service.Id, customer.Id,
        TestDates.InDays(60), new TimeOnly(9, 0), new TimeOnly(9, 30),
        AppointmentStatus.Booked, new Money(100m, "PLN"), "Uczulenie na lateks", lease: null);
      appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_anon", "http://localhost/p/anoncode", now.AddHours(24), now);

      db.Tenants.Add(tenant);
      db.ServiceCategories.Add(category);
      db.VatRates.Add(vat);
      db.Employees.Add(ownerEmployee);
      db.Services.Add(service);
      db.Customers.Add(customer);
      db.Appointments.Add(appt);
      db.ShortLinks.Add(new ShortLink("anoncode", "https://stripe.test/x", "deposit", tenant.Id, appt.Id, now, now.AddHours(24)));
      db.SaveChanges();
      customerId = customer.Id;
      appointmentId = appt.Id;
    }

    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsync($"/api/Customers/{customerId}/anonymize", content: null, ct);
    Assert.True(response.IsSuccessStatusCode, $"anonymize zwrócił {(int)response.StatusCode}");

    using var scope2 = factory.Services.CreateScope();
    var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt2 = await db2.Appointments.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.Id == appointmentId, ct);
    Assert.Null(appt2.PaymentLinkUrl);
    Assert.Null(appt2.PaymentSessionId);
    Assert.Equal(string.Empty, appt2.AppointmentNotes); // RODO art. 17 — notatka (potencjalnie dane art. 9) wyczyszczona.
    Assert.False(await db2.ShortLinks.AnyAsync(l => l.AppointmentId == appointmentId, ct));
  }

  [Fact]
  public async Task ShortLink_cleanup_removes_expired_keeps_fresh()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var now = DateTime.UtcNow;

    Guid expiredId, freshId;
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      var tenantId = Guid.NewGuid();
      var expired = new ShortLink("expiredd", "https://x/old", "deposit", tenantId, null,
        now.AddDays(-30), now.AddDays(-29)); // wygasł 29 dni temu (poza karencją 7 dni)
      var fresh = new ShortLink("freshhhh", "https://x/new", "deposit", tenantId, null,
        now, now.AddHours(24)); // ważny
      expiredId = expired.Id;
      freshId = fresh.Id;
      db.ShortLinks.Add(expired);
      db.ShortLinks.Add(fresh);
      await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    using var scope2 = factory.Services.CreateScope();
    var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var ct = TestContext.Current.CancellationToken;

    var deleted = await ShortLinkCleanupHostedService.RunCycleAsync(
      db2, now, TimeSpan.FromDays(7), ct, NullLogger.Instance);

    Assert.Equal(1, deleted);
    Assert.False(await db2.ShortLinks.AnyAsync(l => l.Id == expiredId, ct));
    Assert.True(await db2.ShortLinks.AnyAsync(l => l.Id == freshId, ct));
  }

  // --- helpers ---

  private static StringContent WebhookBody(string type, string sessionId, Guid appointmentId)
  {
    var json = $$"""
      {"type":"{{type}}","sessionId":"{{sessionId}}","appointmentId":"{{appointmentId}}"}
      """;
    return new StringContent(json, Encoding.UTF8, "application/json");
  }

  private static void SeedSalon(
    IServiceProvider rootServices, string slug, bool chargesEnabled, out Guid appointmentId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Deposit Salon", slug);
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email,
      depositSettings: new DepositSettings(true, DepositMode.Percentage, 30m, DepositInstrument.Zadatek));
    tenant.ConnectMerchantAccount("stripe", "acct_test");
    tenant.UpdateMerchantAccountStatus(
      chargesEnabled ? MerchantOnboardingStatus.Complete : MerchantOnboardingStatus.Pending,
      chargesEnabled);

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, TestIdentity.Ensure(db, IntegrationTestUserIds.SalonOwner, "owner@dep.local"), "Owner", "Test", "owner@dep.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Usługa", new Money(100m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes,
      new Money(service.Price.Amount, service.Price.Currency));

    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, customerId: null,
      TestDates.InDays(60), new TimeOnly(9, 0), new TimeOnly(9, 30),
      AppointmentStatus.Booked, new Money(100m, "PLN"), string.Empty, lease: null);
    appointmentId = appt.Id;

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Appointments.Add(appt);
    db.SaveChanges();
  }

  private static void SetCustomDomain(IServiceProvider rootServices, string slug, string domain)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var tenant = db.Tenants.IgnoreQueryFilters().First(t => t.Slug == slug);
    tenant.SetCustomDomain(domain);
    db.SaveChanges();
  }

  /// <summary>
  /// Salon z wizytą przypisaną INNEMU pracownikowi niż konto SalonOwner (caller). Pracownik
  /// powiązany z SalonOwner istnieje (do rozwiązania tenanta), ale wizyta należy do drugiego.
  /// Polityka ustawiona JAWNIE na OwnCalendarOnly (default tenanta to TeamFull) → Employee nie
  /// może operować na cudzej wizycie.
  /// </summary>
  private static Guid SeedSalonAppointmentOwnedByOtherEmployee(IServiceProvider rootServices, string slug)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Deposit Salon", slug);
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email,
      staffCalendarVisibilityPolicy: StaffCalendarVisibilityPolicy.OwnCalendarOnly,
      depositSettings: new DepositSettings(true, DepositMode.Percentage, 30m, DepositInstrument.Zadatek));
    tenant.ConnectMerchantAccount("stripe", "acct_test");
    tenant.UpdateMerchantAccountStatus(MerchantOnboardingStatus.Complete, true);

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var ownerEmployee = new Employee(tenant.Id, TestIdentity.Ensure(db, IntegrationTestUserIds.SalonOwner, "owner@dep.local"), "Owner", "Test", "owner@dep.local");
    var otherEmployee = new Employee(tenant.Id, TestIdentity.Ensure(db, Guid.NewGuid(), "other@dep.local"), "Other", "Emp", "other@dep.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Usługa", new Money(100m, "PLN"), 30);
    otherEmployee.AssignService(tenant.Id, service.Id, service.DurationInMinutes,
      new Money(service.Price.Amount, service.Price.Currency));

    var appt = new Appointment(
      tenant.Id, otherEmployee.Id, service.Id, customerId: null,
      TestDates.InDays(60), new TimeOnly(9, 0), new TimeOnly(9, 30),
      AppointmentStatus.Booked, new Money(100m, "PLN"), string.Empty, lease: null);

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(ownerEmployee);
    db.Employees.Add(otherEmployee);
    db.Services.Add(service);
    db.Appointments.Add(appt);
    db.SaveChanges();

    return appt.Id;
  }

  private static (Guid appointmentId, string sessionId) SeedAwaitingPaymentAppointment(
    IServiceProvider rootServices, string slug)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Deposit Salon", slug);
    tenant.ConnectMerchantAccount("stripe", "acct_test");
    tenant.UpdateMerchantAccountStatus(MerchantOnboardingStatus.Complete, true);
    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, TestIdentity.Ensure(db, Guid.NewGuid(), "eva@dep.local"), "Eva", "Test", "eva@dep.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Usługa", new Money(100m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes,
      new Money(service.Price.Amount, service.Price.Currency));

    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, customerId: null,
      TestDates.InDays(60), new TimeOnly(9, 0), new TimeOnly(9, 30),
      AppointmentStatus.Booked, new Money(100m, "PLN"), string.Empty, lease: null);

    var sessionId = "cs_fake_" + appt.Id.ToString("N");
    appt.GenerateDepositLink(new Money(30m, "PLN"), sessionId, "http://localhost/p/abc1234", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Appointments.Add(appt);
    db.SaveChanges();

    return (appt.Id, sessionId);
  }

  private static void MarkAppointmentPaid(IServiceProvider rootServices, Guid appointmentId)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var appt = db.Appointments.IgnoreQueryFilters().First(a => a.Id == appointmentId);
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_paid", "http://localhost/p/paid", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);
    appt.MarkDepositPaid(DateTime.UtcNow);
    db.SaveChanges();
  }

  private static Guid SeedSalonWithCustomerAndLink(
    IServiceProvider rootServices, string slug, CustomerVerificationChannel channel)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Send Salon", slug);
    tenant.Update(tenant.Name, tenant.Slug, channel,
      depositSettings: new DepositSettings(true, DepositMode.Percentage, 30m, DepositInstrument.Zadatek));
    tenant.ConnectMerchantAccount("stripe", "acct_test");
    tenant.UpdateMerchantAccountStatus(MerchantOnboardingStatus.Complete, true);

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, TestIdentity.Ensure(db, IntegrationTestUserIds.SalonOwner, "owner@send.local"), "Owner", "Test", "owner@send.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Usługa", new Money(100m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes,
      new Money(service.Price.Amount, service.Price.Currency));
    var customer = new Customer(tenant.Id, "Jan", "Kowalski", "jan@klient.local", null, string.Empty);

    var appt = new Appointment(
      tenant.Id, employee.Id, service.Id, customer.Id,
      TestDates.InDays(60), new TimeOnly(9, 0), new TimeOnly(9, 30),
      AppointmentStatus.Booked, new Money(100m, "PLN"), string.Empty, lease: null);
    appt.GenerateDepositLink(new Money(30m, "PLN"), "cs_send", "http://localhost/p/sendabc", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Customers.Add(customer);
    db.Appointments.Add(appt);
    db.SaveChanges();

    return appt.Id;
  }

  private sealed record SendResult(string Channel);
  private sealed record GenerateLinkResult(string Url, DateTime ExpiresAtUtc, decimal Amount, string Currency);
  private sealed record ProblemResponse(string ErrorCode);
  private sealed record MoneyDto(decimal Amount, string Currency);
  private sealed record AppointmentDetailResponse(string PaymentStatus, MoneyDto? DepositAmount, DateTime? PaidAtUtc);
}
