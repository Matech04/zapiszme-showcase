using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using App.Api.E2eSupport;
using App.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace App.Api.IntegrationTests;

/// <summary>
/// MEDIA-UPLOAD-* — endpoint POST /api/media/images: autoryzacja (staff), happy path z multipart,
/// odrzucenie nie-obrazu. Pipeline obróbki zmockowany (FakeImageProcessingService) — bez MinIO/S3.
/// </summary>
public sealed class MediaUploadIntegrationTests
{
  private static byte[] CreatePng()
  {
    using var image = new Image<Rgba32>(64, 64);
    using var ms = new MemoryStream();
    image.Save(ms, new PngEncoder());
    return ms.ToArray();
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

  private static MultipartFormDataContent BuildMultipart(byte[] bytes, string fileName, string contentType)
  {
    var content = new MultipartFormDataContent();
    var fileContent = new ByteArrayContent(bytes);
    fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
    content.Add(fileContent, "file", fileName);
    return content;
  }

  private static HttpClient OwnerClient(WebApplicationFactory<Program> factory)
  {
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(
      App.Api.Authentication.IntegrationTestAuthHeaders.UserId,
      IntegrationTestUserIds.SalonOwner.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(
      App.Api.Authentication.IntegrationTestAuthHeaders.Roles, "Owner");
    return client;
  }

  [Fact]
  public async Task UploadImage_AsOwner_ReturnsUrls()
  {
    var fake = new FakeImageProcessingService();
    using var factory = FactoryWithFakeImageProcessing(fake);
    using var client = OwnerClient(factory);

    using var content = BuildMultipart(CreatePng(), "photo.png", "image/png");
    var response = await client.PostAsync("/api/media/images", content, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<UploadImageResponseDto>(TestContext.Current.CancellationToken);
    Assert.NotNull(body);
    Assert.Equal("https://cdn.test/services/abc.webp", body!.Url);
    Assert.Equal("https://cdn.test/services/abc_thumb.webp", body.ThumbnailUrl);
    Assert.Equal("services/abc.webp", body.Key);
    Assert.Equal("services", fake.LastKeyPrefix);
  }

  [Fact]
  public async Task UploadImage_Unauthenticated_Returns401()
  {
    var fake = new FakeImageProcessingService();
    using var factory = FactoryWithFakeImageProcessing(fake);
    using var client = factory.CreateClient();

    using var content = BuildMultipart(CreatePng(), "photo.png", "image/png");
    var response = await client.PostAsync("/api/media/images", content, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    Assert.Null(fake.LastKeyPrefix);
  }

  [Fact]
  public async Task UploadImage_NonImageRejected_PropagatesBadRequest()
  {
    var fake = new FakeImageProcessingService { ThrowInvalidImage = true };
    using var factory = FactoryWithFakeImageProcessing(fake);
    using var client = OwnerClient(factory);

    using var content = BuildMultipart("not an image"u8.ToArray(), "evil.png", "image/png");
    var response = await client.PostAsync("/api/media/images", content, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  private sealed record UploadImageResponseDto(string Url, string ThumbnailUrl, string Key);

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
        "services/abc.webp",
        "https://cdn.test/services/abc.webp",
        "https://cdn.test/services/abc_thumb.webp",
        "services/abc_thumb.webp"));
    }
  }
}
