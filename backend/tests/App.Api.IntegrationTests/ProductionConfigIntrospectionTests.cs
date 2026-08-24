using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using App.Api.E2eSupport;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace App.Api.IntegrationTests;

/// <summary>
/// SEC-050b — config-level introspection cookie security w prod env.
///
/// Tu nie testujemy zachowania HTTP (TestServer i tak nie egzekwuje SameSite/Secure jako browser),
/// tylko czy `IOptions&lt;CookieXxxOptions&gt;` w trybie Production mają poprawne wartości:
///   - antiforgery cookie: SameSite=None + Secure=Always (cross-origin admin↔api)
///   - identity application cookie: to samo (chodzimy do api z innej subdomeny)
///
/// Cel: następna regresja w konfiguracji cookie (ktoś zmieni SameSite na Lax, usunie Secure)
/// zostanie złapana przez CI zanim trafi do produkcji.
/// </summary>
public sealed class ProductionConfigIntrospectionTests : IDisposable
{
  // SKIP_DB_STARTUP musi być env-var (Program.cs czyta Environment.GetEnvironmentVariable
  // bezpośrednio, nie przez IConfiguration). Ustawiamy w ctor, usuwamy w Dispose.
  private const string SkipDbVar = "SKIP_DB_STARTUP";
  private readonly string? _prevSkipDb = Environment.GetEnvironmentVariable(SkipDbVar);

  // Prawdziwy prod (nie-LOCAL_PROD) wymaga DataProtection:CertificatePath do szyfrowania
  // key ringu at-rest — fail-fast w ValidateProductionConfiguration + realne ładowanie certu
  // (ProtectKeysWithCertificate) przy starcie. Generujemy efemeryczny self-signed PFX per-klasa.
  private const string CertPassword = "test-dp-cert-pass";
  private readonly string _certPath = CreateSelfSignedPfx(CertPassword);

  public ProductionConfigIntrospectionTests()
  {
    Environment.SetEnvironmentVariable(SkipDbVar, "1");
  }

  public void Dispose()
  {
    Environment.SetEnvironmentVariable(SkipDbVar, _prevSkipDb);
    if (File.Exists(_certPath))
    {
      File.Delete(_certPath);
    }
  }

  private static string CreateSelfSignedPfx(string password)
  {
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest(
      "CN=zapiszme-dataprotection-test",
      rsa,
      HashAlgorithmName.SHA256,
      RSASignaturePadding.Pkcs1);
    using var cert = request.CreateSelfSigned(
      DateTimeOffset.UtcNow.AddDays(-1),
      DateTimeOffset.UtcNow.AddYears(1));
    var pfxBytes = cert.Export(X509ContentType.Pfx, password);
    var path = Path.Combine(Path.GetTempPath(), $"zapiszme-dp-cert-{Guid.NewGuid():N}.pfx");
    File.WriteAllBytes(path, pfxBytes);
    return path;
  }

  [Fact]
  public void Production_antiforgery_cookie_has_SameSite_None_and_Secure_Always_and_HttpOnly()
  {
    using var factory = CreateProductionLikeFactory();
    var options = factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

    Assert.Equal(SameSiteMode.None, options.Cookie.SameSite);
    Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    Assert.True(options.Cookie.HttpOnly, "Antiforgery cookie must be HttpOnly w prod");
    Assert.Equal("booking_saas_antiforgery", options.Cookie.Name);
    Assert.Equal("X-XSRF-TOKEN", options.HeaderName);
  }

  [Fact]
  public void Production_identity_application_cookie_has_SameSite_None_and_Secure_Always_and_HttpOnly()
  {
    using var factory = CreateProductionLikeFactory();
    var monitor = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
    var options = monitor.Get(IdentityConstants.ApplicationScheme);

    Assert.Equal(SameSiteMode.None, options.Cookie.SameSite);
    Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    Assert.True(options.Cookie.HttpOnly, "Identity cookie must be HttpOnly w prod");
    Assert.Equal("booking_saas_identity", options.Cookie.Name);
  }

  // ACS connection-string format guard. Złap inny niż "endpoint=...;accesskey=..." przy
  // starcie — w prod ten sam błąd przeszedł niezauważony przez ValidateProductionConfiguration
  // (sprawdzała tylko obecność) i wywalił EmailClient parser dopiero przy pierwszym
  // request do /api/auth/csrf (500 per-request).
  [Theory]
  [InlineData("https://zapiszmeotp.europe.communication.azure.com/")] // sam URL — produkcyjny bug
  [InlineData("endpoint=https://x.communication.azure.com/")]          // brak accesskey
  [InlineData("accesskey=ABCD==")]                                       // brak endpointu
  [InlineData("")]                                                       // pusty
  public void Production_startup_rejects_malformed_ACS_connection_string(string badConnString)
  {
    var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
    {
      var baseFactory = new BookingApiApplicationFactory();
      try
      {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
          builder.UseEnvironment("Production");
          builder.UseSetting("Dashboard:BaseUrl", "https://test.local");
          builder.UseSetting("Cors:AllowedOrigins:0", "https://test.local");
          builder.UseSetting("Turnstile:Enabled", "true");
          builder.UseSetting("Turnstile:SecretKey", "test-managed-secret");
          builder.UseSetting("Turnstile:InvisibleSecretKey", "test-invisible-secret");
          builder.UseSetting("ReverseProxy:TrustedProxies:0", "127.0.0.1");
          builder.UseSetting("DataProtection:KeysPath", "/tmp/zapiszme-dp-test");
          builder.UseSetting("DataProtection:CertificatePath", _certPath);
          builder.UseSetting("DataProtection:CertificatePassword", CertPassword);
          // Wymagane przez ValidateProductionConfiguration (fail-fast SMS-first) — bez tego
          // walidacja wywaliłaby się na braku Sms:OAuthToken zanim dojdzie do checku ACS.
          builder.UseSetting("Sms:OAuthToken", "test-sms-token");
          builder.UseSetting("AzureCommunication:Email:ConnectionString", badConnString);
          ApplyValidStorageSettings(builder);
          builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Host=stub;Database=stub;Username=stub;Password=stub");
        });
        _ = factory.Services;
      }
      finally
      {
        baseFactory.Dispose();
      }
    });

    Assert.Contains("AzureCommunication:Email:ConnectionString", ex.Message);
  }

  // SEC: puste Sms:SenderName = smsapi odrzuca każdą wysyłkę dopiero w runtime, a health-check
  // pozostaje zielony. Cicho niedziałające OTP jest gorsze niż brak startu.
  [Fact]
  public void Production_startup_rejects_empty_Sms_SenderName()
  {
    var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
    {
      var baseFactory = new BookingApiApplicationFactory();
      try
      {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
          ApplyValidProductionSettings(builder);
          builder.UseSetting("Sms:SenderName", "");
        });
        _ = factory.Services;
      }
      finally
      {
        baseFactory.Dispose();
      }
    });

    Assert.Contains("Sms:SenderName", ex.Message);
  }

  // SEC: Sms:TestMode=true w prawdziwym prod cicho wyłącza realny OTP SMS i znosi wymóg tokenu —
  // weryfikacja kodem staje się fikcją. ValidateProductionConfiguration MUSI to zatrzymać przy starcie.
  [Fact]
  public void Production_startup_rejects_Sms_TestMode_true()
  {
    var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
    {
      var baseFactory = new BookingApiApplicationFactory();
      try
      {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
          ApplyValidProductionSettings(builder);
          builder.UseSetting("Sms:TestMode", "true");
        });
        _ = factory.Services;
      }
      finally
      {
        baseFactory.Dispose();
      }
    });

    Assert.Contains("Sms:TestMode", ex.Message);
  }

  // SEC: Auth:DevFixedOtpCode w prod = każdy zna kod SMS każdego konta → OTP przestaje cokolwiek
  // weryfikować. Runtime i tak ignoruje flagę (gate na IsDevelopment), ale sama jej obecność
  // w prodowym configu znaczy, że ktoś wlał dev-owe ustawienia — start MUSI paść.
  [Fact]
  public void Production_startup_rejects_Auth_DevFixedOtpCode()
  {
    var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
    {
      var baseFactory = new BookingApiApplicationFactory();
      try
      {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
          ApplyValidProductionSettings(builder);
          builder.UseSetting("Auth:DevFixedOtpCode", "000000");
        });
        _ = factory.Services;
      }
      finally
      {
        baseFactory.Dispose();
      }
    });

    Assert.Contains("Auth:DevFixedOtpCode", ex.Message);
  }

  // SEC: w przeciwieństwie do Sms:TestMode, Auth:DevFixedOtpCode NIE ma wyjątku dla LOCAL_PROD.
  // LOCAL_PROD to jedyna furtka, przez którą ta flaga mogłaby wjechać na prawdziwy prod przy
  // literówce w env — a w LOCAL_PROD kod OTP i tak jest w logach (Sms:TestMode → brak wysyłki),
  // więc stały kod niczego tam nie odblokowuje. Ten test pilnuje, żeby ktoś nie „naprawił"
  // niedziałającego stałego kodu w LOCAL_PROD dopisując wyjątek.
  [Fact]
  public void Local_prod_startup_also_rejects_Auth_DevFixedOtpCode()
  {
    var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
    {
      var baseFactory = new BookingApiApplicationFactory();
      try
      {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
          ApplyValidProductionSettings(builder);
          builder.UseSetting("LOCAL_PROD", "true");
          builder.UseSetting("Auth:DevFixedOtpCode", "000000");
        });
        _ = factory.Services;
      }
      finally
      {
        baseFactory.Dispose();
      }
    });

    Assert.Contains("Auth:DevFixedOtpCode", ex.Message);
  }

  // SEC: bez DataProtection:CertificatePath master-keye zapisują się jawnie at-rest —
  // wyciek pliku = sfałszowanie sesji/tokenów Identity. W prawdziwym prod to twardy błąd startu.
  [Fact]
  public void Production_startup_rejects_missing_DataProtection_certificate()
  {
    var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
    {
      var baseFactory = new BookingApiApplicationFactory();
      try
      {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
          ApplyValidProductionSettings(builder);
          // Celowo NIE ustawiamy DataProtection:CertificatePath — fail-fast ma to złapać.
          builder.UseSetting("DataProtection:CertificatePath", string.Empty);
          builder.UseSetting("DataProtection:CertificatePassword", string.Empty);
        });
        _ = factory.Services;
      }
      finally
      {
        baseFactory.Dispose();
      }
    });

    Assert.Contains("DataProtection:CertificatePath", ex.Message);
  }

  // Minimalny komplet ustawień, przy których ValidateProductionConfiguration PRZECHODZI.
  // Poszczególne testy nadpisują pojedyncze klucze, żeby udowodnić konkretny fail-fast.
  private void ApplyValidProductionSettings(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Production");
    builder.UseSetting("Dashboard:BaseUrl", "https://test.local");
    builder.UseSetting("Cors:AllowedOrigins:0", "https://test.local");
    builder.UseSetting("Turnstile:Enabled", "true");
    builder.UseSetting("Turnstile:SecretKey", "test-managed-secret");
    builder.UseSetting("Turnstile:InvisibleSecretKey", "test-invisible-secret");
    builder.UseSetting("ReverseProxy:TrustedProxies:0", "127.0.0.1");
    builder.UseSetting("DataProtection:KeysPath", "/tmp/zapiszme-dp-test");
    builder.UseSetting("DataProtection:CertificatePath", _certPath);
    builder.UseSetting("DataProtection:CertificatePassword", CertPassword);
    builder.UseSetting("Sms:OAuthToken", "test-sms-token");
    builder.UseSetting("AzureCommunication:Email:ConnectionString",
      "endpoint=https://stub.communication.azure.com/;accesskey=AAAA");
    builder.UseSetting("ConnectionStrings:DefaultConnection",
      "Host=stub;Database=stub;Username=stub;Password=stub");
    ApplyValidStorageSettings(builder);
  }

  // Storage (Cloudflare R2) jest wymagane przez ValidateProductionConfiguration, gdy
  // Storage:Provider=R2 (domyślnie w appsettings.json). Bez kompletu app nie wstaje w Production —
  // walidacja rzuca, zanim dojdzie do checków ACS/cookies.
  private static void ApplyValidStorageSettings(IWebHostBuilder builder)
  {
    builder.UseSetting("Storage:Provider", "R2");
    builder.UseSetting("Storage:Endpoint", "https://test.r2.cloudflarestorage.com");
    builder.UseSetting("Storage:Bucket", "test-bucket");
    builder.UseSetting("Storage:PublicBaseUrl", "https://media.test.local");
    builder.UseSetting("Storage:AccessKey", "test-access-key");
    builder.UseSetting("Storage:SecretKey", "test-secret-key");

    // Web Push (VAPID) — ValidateProductionConfiguration wymaga kompletu kluczy w Production.
    // Wartości testowe (walidacja sprawdza tylko czy niepuste; startup ich nie parsuje). Wołane
    // przez wszystkie ścieżki budujące „ważny prod-config", więc dodane tu raz pokrywa je wszystkie.
    builder.UseSetting("WebPush:VapidPublicKey", "vapid-public-key-test-placeholder");
    builder.UseSetting("WebPush:VapidPrivateKey", "vapid-private-key-test-placeholder");
    builder.UseSetting("WebPush:Subject", "mailto:test@zapisz.me");
  }

  /// <summary>
  /// Buduje factory z env=Production + minimalny zestaw GH-style sekretów żeby
  /// ValidateProductionConfiguration przeszło. Nie odpalamy HTTP — tylko inspekcja DI.
  /// </summary>
  private WebApplicationFactoryWrapper CreateProductionLikeFactory()
  {
    var baseFactory = new BookingApiApplicationFactory();
    var factory = baseFactory.WithWebHostBuilder(builder =>
    {
      builder.UseEnvironment("Production");

      // Wymagane przez ValidateProductionConfiguration:
      builder.UseSetting("Dashboard:BaseUrl", "https://test.local");
      builder.UseSetting("Cors:AllowedOrigins:0", "https://test.local");
      builder.UseSetting("Turnstile:Enabled", "true");
      builder.UseSetting("Turnstile:SecretKey", "test-managed-secret");
      builder.UseSetting("Turnstile:InvisibleSecretKey", "test-invisible-secret");
      builder.UseSetting("ReverseProxy:TrustedProxies:0", "127.0.0.1");
      builder.UseSetting("DataProtection:KeysPath", "/tmp/zapiszme-dp-test");
      builder.UseSetting("DataProtection:CertificatePath", _certPath);
      builder.UseSetting("DataProtection:CertificatePassword", CertPassword);
      // SMS-first: ValidateProductionConfiguration wymaga Sms:OAuthToken (albo TestMode).
      builder.UseSetting("Sms:OAuthToken", "test-sms-token");
      builder.UseSetting("AzureCommunication:Email:ConnectionString",
        "endpoint=https://stub.communication.azure.com/;accesskey=AAAA");

      // Connection string dla prod env (treść nie ma znaczenia — SKIP_DB_STARTUP=1).
      builder.UseSetting("ConnectionStrings:DefaultConnection",
        "Host=stub;Database=stub;Username=stub;Password=stub");
      ApplyValidStorageSettings(builder);
    });
    return new WebApplicationFactoryWrapper(baseFactory, factory);
  }

  /// <summary>Wraps owner + derived factory żeby Dispose obu poszło razem.</summary>
  private sealed class WebApplicationFactoryWrapper : IDisposable
  {
    private readonly BookingApiApplicationFactory _owner;
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _derived;

    public WebApplicationFactoryWrapper(
      BookingApiApplicationFactory owner,
      Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> derived)
    {
      _owner = owner;
      _derived = derived;
    }

    public IServiceProvider Services => _derived.Services;

    public void Dispose()
    {
      _derived.Dispose();
      _owner.Dispose();
    }
  }
}
