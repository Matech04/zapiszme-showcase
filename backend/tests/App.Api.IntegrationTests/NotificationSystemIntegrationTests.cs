using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api.E2eSupport;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// NOTIF-SYS-* — kategoria ustawień powiadomień (round-trip) oraz kanał e-mail systemu
/// powiadomień end-to-end (potwierdzenie rezerwacji + bramka ustawień).
/// </summary>
public sealed class NotificationSystemIntegrationTests
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  // NOTIF-SYS-001: ustawienia powiadomień są w GET /api/SalonSettings i przeżywają PUT round-trip.
  [Fact]
  public async Task Salon_settings_notification_preferences_round_trip()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    _ = RestApiIntegrationSeed.Seed(factory.Services);
    var client = factory.CreateOwnerClient();
    var ct = TestContext.Current.CancellationToken;

    var get1 = await client.GetAsync("/api/SalonSettings", ct);
    Assert.Equal(HttpStatusCode.OK, get1.StatusCode);
    var dto1 = await get1.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
    var ns1 = dto1.GetProperty("notificationSettings");
    // Domyślnie wyłączone dla nowego salonu (NotificationSettings.Defaults()).
    Assert.False(ns1.GetProperty("bookingConfirmationToCustomer").GetBoolean());
    Assert.True(ns1.GetProperty("newBookingToSalon").GetBoolean());

    var putPayload = new
    {
      name = dto1.GetProperty("name").GetString(),
      slug = dto1.GetProperty("slug").GetString(),
      customerVerificationChannel = dto1.GetProperty("customerVerificationChannel").GetInt32(),
      appointmentSlotStepMinutes = dto1.GetProperty("appointmentSlotStepMinutes").GetInt32(),
      timeZoneId = dto1.GetProperty("timeZoneId").GetString(),
      currency = dto1.GetProperty("currency").GetString(),
      bookingAccessPolicy = dto1.GetProperty("bookingAccessPolicy").GetInt32(),
      appointmentConfirmationMode = dto1.GetProperty("appointmentConfirmationMode").GetInt32(),
      notificationSettings = new
      {
        newBookingToSalon = true,
        bookingConfirmationToCustomer = false,
        cancellationToSalon = true,
        cancellationToCustomer = true,
        rescheduleToSalon = true,
        rescheduleToCustomer = true,
        appointmentReminderToCustomer = false,
      },
    };

    var put = await client.PutAsJsonAsync("/api/SalonSettings", putPayload, ct);
    Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

    var get2 = await client.GetAsync("/api/SalonSettings", ct);
    var dto2 = await get2.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
    var ns2 = dto2.GetProperty("notificationSettings");
    Assert.False(ns2.GetProperty("bookingConfirmationToCustomer").GetBoolean());
    Assert.False(ns2.GetProperty("appointmentReminderToCustomer").GetBoolean());
    Assert.True(ns2.GetProperty("newBookingToSalon").GetBoolean());
  }

  // NOTIF-SYS-002: po verify-OTP kanał e-mail wysyła potwierdzenie do klienta i powiadomienie do salonu.
  [Fact]
  public async Task Verify_otp_dispatches_confirmation_email_to_customer_and_salon()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    // Potwierdzenie do klienta jest domyślnie wyłączone (Defaults()) — tu testujemy ścieżkę,
    // gdy salon je włączył, więc jawnie AllEnabled().
    SeedPendingAppointmentWithLease(factory.Services, "notif-email-salon", NotificationSettings.AllEnabled(),
      out var appointmentId, out var leaseToken);
    var transport = factory.Services.GetRequiredService<TestEmailTransport>();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var requestOtp = await client.PostAsJsonAsync(
      $"/api/booking/notif-email-salon/public-appointment/{appointmentId}/request-otp",
      new { token = leaseToken, phoneNumber = (string?)null, email = "booker@example.com" },
      ct);
    Assert.Equal(HttpStatusCode.OK, requestOtp.StatusCode);

    var otpMailbox = factory.Services.GetRequiredService<TestBookingOtpMailbox>();
    var verify = await client.PostAsJsonAsync(
      $"/api/booking/notif-email-salon/public-appointment/{appointmentId}/verify-otp",
      new { token = leaseToken, otp = otpMailbox.LastCode },
      ct);
    Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

    Assert.Contains(transport.Sent, m => m.To == "booker@example.com");
    Assert.Contains(transport.Sent, m => m.To == "eva@test.local");
  }

  // NOTIF-SYS-003: wyłączone BookingConfirmationToCustomer → klient nie dostaje maila, salon dalej tak.
  [Fact]
  public async Task Verify_otp_respects_disabled_customer_confirmation_setting()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var settings = new NotificationSettings(
      newBookingToSalon: true,
      bookingConfirmationToCustomer: false,
      cancellationToSalon: true,
      cancellationToCustomer: true,
      rescheduleToSalon: true,
      rescheduleToCustomer: true,
      appointmentReminderToCustomer: true,
      awaitingConfirmationToSalon: true,
      cancelledBySalonToCustomer: true,
      rescheduledBySalonToCustomer: true,
      appointmentReminder2hToCustomer: true,
      staffBookedAppointmentToCustomer: true);
    SeedPendingAppointmentWithLease(factory.Services, "notif-gate-salon", settings,
      out var appointmentId, out var leaseToken);
    var transport = factory.Services.GetRequiredService<TestEmailTransport>();
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    await client.PostAsJsonAsync(
      $"/api/booking/notif-gate-salon/public-appointment/{appointmentId}/request-otp",
      new { token = leaseToken, phoneNumber = (string?)null, email = "booker@example.com" },
      ct);
    var otpMailbox = factory.Services.GetRequiredService<TestBookingOtpMailbox>();
    var verify = await client.PostAsJsonAsync(
      $"/api/booking/notif-gate-salon/public-appointment/{appointmentId}/verify-otp",
      new { token = leaseToken, otp = otpMailbox.LastCode },
      ct);
    Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

    Assert.DoesNotContain(transport.Sent, m => m.To == "booker@example.com");
    Assert.Contains(transport.Sent, m => m.To == "eva@test.local");
  }

  private static void SeedPendingAppointmentWithLease(
    IServiceProvider rootServices,
    string slug,
    NotificationSettings? notificationSettings,
    out Guid appointmentId,
    out Guid leaseToken)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Notif Salon", slug);
    tenant.Update(tenant.Name, tenant.Slug, CustomerVerificationChannel.Email,
      notificationSettings: notificationSettings);

    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, userId: null, "Eva", "Test", "eva@test.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Service", new Money(50m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes,
      new Money(service.Price.Amount, service.Price.Currency));

    leaseToken = Guid.NewGuid();
    var lease = new HoldLease(leaseToken, DateTime.UtcNow.AddHours(4));
    var appointment = new Appointment(
      tenant.Id, employee.Id, service.Id, customerId: null,
      TestDates.InDays(15), new TimeOnly(9, 0), new TimeOnly(10, 0),
      AppointmentStatus.Pending, new Money(50m, "PLN"), string.Empty, lease);

    appointmentId = appointment.Id;

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Appointments.Add(appointment);
    db.SaveChanges();
  }
}
