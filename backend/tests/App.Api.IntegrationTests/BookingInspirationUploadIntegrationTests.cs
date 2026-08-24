using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.TenantAggregate;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Common;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace App.Api.IntegrationTests;

/// <summary>
/// BOOKING-INSPIRATION-UPLOAD-* — autoryzowany endpoint POST
/// /api/booking/{slug}/appointments/{appointmentId}/inspirations (#2, deferred-upload). Zdjęcia
/// trzymane w przeglądarce do potwierdzenia OTP; upload autoryzuje krótkożyjący token grantu
/// (nagłówek X-Inspiration-Upload-Token) wydany przez verify-otp / confirm. Sprawdzamy: happy path
/// (200 + obraz podpięty pod wizytę + prefiks inspirations/), 403 dla braku/zepsutego tokenu, 403 dla
/// tokenu wydanego dla innej wizyty (anti-cross-appointment), 400 dla nie-obrazu. Pipeline obróbki
/// zmockowany (FakeImageProcessingService).
/// </summary>
public sealed class BookingInspirationUploadIntegrationTests
{
  private const string Slug = "integration-inspiration-salon";

  private static byte[] CreatePng()
  {
    using var image = new Image<Rgba32>(48, 48);
    using var ms = new MemoryStream();
    image.Save(ms, new PngEncoder());
    return ms.ToArray();
  }

  private static void SeedAppointment(
      IServiceProvider rootServices, string slug, out Guid appointmentId, bool collectInspirations = true)
  {
    using var scope = rootServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var tenant = new Tenant("Inspiration Salon", slug);
    // Domyślnie funkcja jest wyłączona — ustawiamy jawnie (happy-path testy włączają ją).
    tenant.Update(tenant.Name, tenant.Slug, collectInspirationImages: collectInspirations);
    var category = new ServiceCategory(tenant.Id, "Cat", 0);
    var vat = new VatRate(tenant.Id, "VAT", 0.23m);
    var employee = new Employee(tenant.Id, userId: null, "Eva", "Test", "eva@test.local");
    var service = new Service(tenant.Id, category.Id, vat.Id, "Service", new Money(50m, "PLN"), 30);
    employee.AssignService(tenant.Id, service.Id, service.DurationInMinutes, new Money(service.Price.Amount, service.Price.Currency));

    var appointment = new Appointment(
        tenant.Id, employee.Id, service.Id, customerId: null,
        TestDates.InDays(15), new TimeOnly(9, 0), new TimeOnly(10, 0),
        AppointmentStatus.Booked, new Money(50m, "PLN"), string.Empty, lease: null);

    appointmentId = appointment.Id;

    db.Tenants.Add(tenant);
    db.ServiceCategories.Add(category);
    db.VatRates.Add(vat);
    db.Employees.Add(employee);
    db.Services.Add(service);
    db.Appointments.Add(appointment);
    db.SaveChanges();
  }

  private static WebApplicationFactory<Program> FactoryWithFakeImageProcessing(FakeImageProcessingService fake)
  {
    return new BookingApiApplicationFactory().WithWebHostBuilder(builder =>
    {
      builder.ConfigureTestServices(services =>
      {
        foreach (var d in services.Where(x => x.ServiceType == typeof(IImageProcessingService)).ToList())
        {
          services.Remove(d);
        }

        services.AddSingleton<IImageProcessingService>(fake);
      });
    });
  }

  private static string IssueToken(IServiceProvider services, Guid appointmentId)
      => services.GetRequiredService<IInspirationUploadTokenService>().Issue(appointmentId);

  private static MultipartFormDataContent BuildMultipart(byte[] bytes, string fileName, string contentType)
  {
    var content = new MultipartFormDataContent();
    var fileContent = new ByteArrayContent(bytes);
    fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
    content.Add(fileContent, "file", fileName);
    return content;
  }

  private static async Task<HttpResponseMessage> PostAsync(
      HttpClient client, string slug, Guid appointmentId, string? token, byte[] bytes, CancellationToken ct)
  {
    using var content = BuildMultipart(bytes, "hair.png", "image/png");
    var request = new HttpRequestMessage(
        HttpMethod.Post, $"/api/booking/{slug}/appointments/{appointmentId}/inspirations")
    {
      Content = content,
    };
    if (token is not null)
    {
      request.Headers.Add("X-Inspiration-Upload-Token", token);
    }
    return await client.SendAsync(request, ct);
  }

  [Fact]
  public async Task AttachInspiration_ValidToken_ReturnsUrls_AndPersistsImage()
  {
    var fake = new FakeImageProcessingService();
    using var factory = FactoryWithFakeImageProcessing(fake);
    SeedAppointment(factory.Services, Slug, out var appointmentId);
    using var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var token = IssueToken(factory.Services, appointmentId);
    var response = await PostAsync(client, Slug, appointmentId, token, CreatePng(), ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<UploadInspirationResponseDto>(ct);
    Assert.NotNull(body);
    Assert.Equal("https://cdn.test/inspirations/abc.webp", body!.Url);
    Assert.Equal("inspirations", fake.LastKeyPrefix);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var reloaded = await db.Appointments.AsNoTracking().IgnoreQueryFilters()
        .Include(a => a.InspirationImages)
        .FirstAsync(a => a.Id == appointmentId, ct);
    Assert.Single(reloaded.InspirationImages);
  }

  [Fact]
  public async Task AttachInspiration_MissingOrInvalidToken_Returns403_NoProcessing()
  {
    var fake = new FakeImageProcessingService();
    using var factory = FactoryWithFakeImageProcessing(fake);
    SeedAppointment(factory.Services, Slug, out var appointmentId);
    using var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await PostAsync(client, Slug, appointmentId, "garbage-token", CreatePng(), ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    Assert.Null(fake.LastKeyPrefix);
  }

  [Fact]
  public async Task AttachInspiration_TokenForOtherAppointment_Returns403()
  {
    var fake = new FakeImageProcessingService();
    using var factory = FactoryWithFakeImageProcessing(fake);
    SeedAppointment(factory.Services, Slug, out var appointmentId);
    using var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Token wydany dla INNEJ wizyty — nie wolno go użyć do podpięcia zdjęć pod tę wizytę.
    var tokenForOther = IssueToken(factory.Services, Guid.NewGuid());
    var response = await PostAsync(client, Slug, appointmentId, tokenForOther, CreatePng(), ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    Assert.Null(fake.LastKeyPrefix);
  }

  [Fact]
  public async Task AttachInspiration_FeatureDisabled_Returns403_NoProcessing()
  {
    var fake = new FakeImageProcessingService();
    using var factory = FactoryWithFakeImageProcessing(fake);
    SeedAppointment(factory.Services, Slug, out var appointmentId, collectInspirations: false);
    using var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    // Token ważny, ale salon wyłączył zbieranie inspiracji → serwer odrzuca (defense-in-depth).
    var token = IssueToken(factory.Services, appointmentId);
    var response = await PostAsync(client, Slug, appointmentId, token, CreatePng(), ct);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    Assert.Null(fake.LastKeyPrefix);
  }

  [Fact]
  public async Task AttachInspiration_NonImage_PropagatesBadRequest()
  {
    var fake = new FakeImageProcessingService { ThrowInvalidImage = true };
    using var factory = FactoryWithFakeImageProcessing(fake);
    SeedAppointment(factory.Services, Slug, out var appointmentId);
    using var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var token = IssueToken(factory.Services, appointmentId);
    var response = await PostAsync(client, Slug, appointmentId, token, "not an image"u8.ToArray(), ct);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  private sealed record UploadInspirationResponseDto(string Url, string ThumbnailUrl, string Key);

  private sealed class FakeImageProcessingService : IImageProcessingService
  {
    public bool ThrowInvalidImage { get; set; }
    public string? LastKeyPrefix { get; private set; }

    public Task<ProcessedImageResult> ProcessAndStoreAsync(Stream content, string keyPrefix, CancellationToken ct = default)
    {
      if (ThrowInvalidImage)
      {
        throw new App.Domain.Exceptions.InvalidImageException("Nieprawidłowy obraz.");
      }

      LastKeyPrefix = keyPrefix;
      return Task.FromResult(new ProcessedImageResult(
        "inspirations/abc.webp",
        "https://cdn.test/inspirations/abc.webp",
        "https://cdn.test/inspirations/abc_thumb.webp",
        "inspirations/abc_thumb.webp"));
    }
  }
}
