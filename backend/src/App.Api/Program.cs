using App.Application;
using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Infrastructure;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using App.Domain.Aggregates.UserAggregate;
using App.Domain.Exceptions;
using App.Infrastructure.Persistence.Repositories;
using App.Infrastructure.Services;
using App.Infrastructure.Storage;
using App.Infrastructure.BackgroundJobs;
using App.Infrastructure.Booking;
using App.Infrastructure.Email;
using App.Application.Booking;
using App.Application.Booking.SelfService;
using App.Application.Notifications;
using App.Infrastructure.Notifications;
using App.Api.Hubs;
using App.Api.Notifications;
using App.Domain.Aggregates.VatRateAggregate;
using App.Domain.Aggregates.ServiceAggregate;
using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Services;
using App.Api;
using App.Api.Authentication;
using App.Api.E2eSupport;
using App.Api.Health;
using App.Api.Middleware;
using App.Api.Options;
using App.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Events;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.RateLimiting;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    // Nie ujawniaj nagłówka `Server: Kestrel` (drobny information disclosure wersji runtime).
    options.AddServerHeader = false;
    // API jest czysto JSON (brak uploadów plików — zweryfikowane: zero IFormFile/FromForm).
    // Domyślny limit 30 MB jest zbędnie duży; 1 MB z zapasem pokrywa największe payloady CRUD
    // i ogranicza wektor abuzywnie dużego body. Request timeout per-endpoint świadomie pomijamy
    // tutaj (globalny zabiłby długożyjące połączenia SignalR) — do dodania selektywnie później.
    options.Limits.MaxRequestBodySize = 1 * 1024 * 1024;
});

// Dorzucamy `appsettings.{Environment}.local.json` jako opcjonalny override — gitignored,
// trzyma lokalne sekrety dev (token smsapi.pl, ACS connection string itp.). Default ASP.NET
// ładuje tylko base + per-env, więc bez tego pliki .local.json są ignorowane.
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

// LOCAL_PROD = ten sam Production build + walidacja, ale działający na HTTP localhost
// (docker-compose.prod.local.yml). Wpływa na: politykę cookies (Secure/SameSite),
// HttpsRedirection/HSTS, ValidateProductionConfiguration, szyfrowanie DataProtection.
// Czytany raz tutaj, używany w wielu miejscach jako warunek równolegle do
// IsDevelopment()/IsEnvironment("Testing").
var isLocalProd = string.Equals(
    builder.Configuration["LOCAL_PROD"]
        ?? Environment.GetEnvironmentVariable("LOCAL_PROD"),
    "true",
    StringComparison.OrdinalIgnoreCase);

// DataProtection key ring persistence — bez tego każdy `docker compose ... --force-recreate`
// inwaliduje wszystkie tokeny Identity (confirm-email/password-reset/accept-invite),
// ciasteczka Auth i antiforgery. SetApplicationName jest krytyczne, bo bez niego
// ASP.NET wylicza app-name z hosta/ścieżki i kluczy nie da się załadować po recreate
// (różny hostname kontenera = inny purpose-string).
{
    var keysPath = builder.Configuration["DataProtection:KeysPath"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
    Directory.CreateDirectory(keysPath);
    var dataProtectionBuilder = builder.Services
        .AddDataProtection()
        .SetApplicationName("BookingSaas.Api")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

    // Szyfrowanie master-keyów at-rest certyfikatem X509. Bez tego key ring zapisuje się
    // JAWNIE na dysku/wolumenie — wyciek pliku = sfałszowanie sesji/tokenów Identity. Włączamy
    // TYLKO w prawdziwym Production i tylko gdy cert jest skonfigurowany; Dev/Testing/E2E
    // oraz LOCAL_PROD zostają bez zmian (inaczej testy i lokalny dev przestają ładować klucze).
    // Brak certu w prawdziwym prod jest twardym błędem startu — patrz ValidateProductionConfiguration.
    if (builder.Environment.IsProduction() && !isLocalProd)
    {
        var certPath = builder.Configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certPath))
        {
            var certPassword = builder.Configuration["DataProtection:CertificatePassword"];
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword);
            dataProtectionBuilder.ProtectKeysWithCertificate(certificate);
        }
    }
}

// Ciasteczka cross-origin (admin.zapisz.me → api.zapisz.me) wymagają SameSite=None + Secure,
// żeby browser je przesłał w XHR/fetch. SameSite=Lax NIE wysyła cookies w cross-origin XHR
// (tylko w top-level navigation), więc CSRF/Identity by się wykrzaczyły lokalnie.
//
// W LOCAL_PROD mamy HTTPS przez Caddy `tls internal` (caddyfile.local), więc Secure
// cookie działa tak samo jak w prawdziwym Production. Dlatego LOCAL_PROD jest tutaj
// po stronie "secure".
//
// Tylko Dev (ng serve na :4200) i Testing (xUnit z TestServer) muszą iść po HTTP →
// tam relax do Lax+SameAsRequest.
var useCrossOriginSecureCookies = CrossOriginCookiePolicy.RequiresCrossSite(builder.Environment);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.With<App.Api.Logging.SensitiveDataMaskingEnricher>()
        .Enrich.WithProperty("Application", "BookingSaas.Api")
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName);

    // W Production stdout idzie w formacie CLEF (JSON): Grafana Alloy scrape'uje logi
    // kontenera i wypycha je do Loki w Grafana Cloud. JSON zachowuje pola strukturalne
    // (TenantId, CorrelationId, StatusCode, Elapsed) do filtrowania LogQL — tekstowy
    // sink by je skleił w jeden string. W Dev/Testing zostaje czytelny tekst w konsoli.
    if (context.HostingEnvironment.IsProduction())
    {
        loggerConfiguration.WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter());
    }
    else
    {
        loggerConfiguration.WriteTo.Console();
    }

    var seqUrl = context.Configuration["Serilog:SeqUrl"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
    {
        loggerConfiguration.WriteTo.Seq(seqUrl);
    }
});

var skipDbStartup =
    string.Equals(
        Environment.GetEnvironmentVariable("SKIP_DB_STARTUP"),
        "1",
        StringComparison.Ordinal)
    || builder.Environment.IsEnvironment("Testing");
var runMigrationsAndExit =
    string.Equals(
        Environment.GetEnvironmentVariable("RUN_MIGRATIONS_AND_EXIT"),
        "1",
        StringComparison.Ordinal);

ValidateProductionConfiguration(builder);

// ==========================================
// 1. KONFIGURACJA SERWISÓW (DI)
// ==========================================

// --- Autoryzacja i tożsamość: w Testing/E2E nagłówki testowe + cookie Identity ---
// Testing: in-proc tests używają wyłącznie nagłówków (DefaultScheme=IntegrationTest).
// E2E:     Playwright używa OBU dróg:
//   (a) realny login UI przez formularz → cookie Identity.Application
//   (b) fixture ownerApi → APIRequestContext z nagłówkami X-Integration-Test-UserId
// Dla (a)+(b) używamy PolicyScheme "E2eAuto" jako default: forwarder wybiera scheme
// na podstawie obecności nagłówka. Bez tego cookie ustawione przez login UI jest
// ignorowane (DefaultScheme=IntegrationTest sprawdza tylko nagłówki) i kolejne
// żądania w teście dostają 401.
var isE2eEnvironment = builder.Environment.IsEnvironment("E2E");
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddAuthentication(o =>
    {
        o.DefaultAuthenticateScheme = IntegrationTestAuthHeaders.SchemeName;
        o.DefaultChallengeScheme = IntegrationTestAuthHeaders.SchemeName;
    })
        .AddScheme<AuthenticationSchemeOptions, IntegrationTestAuthenticationHandler>(
            IntegrationTestAuthHeaders.SchemeName,
            _ => { })
        .AddIdentityCookies();
}
else if (isE2eEnvironment)
{
    const string AutoScheme = "E2eAuto";
    builder.Services.AddAuthentication(AutoScheme)
        .AddPolicyScheme(AutoScheme, AutoScheme, options =>
        {
            options.ForwardDefaultSelector = context =>
                context.Request.Headers.ContainsKey(IntegrationTestAuthHeaders.UserId)
                    ? IntegrationTestAuthHeaders.SchemeName
                    : IdentityConstants.ApplicationScheme;
        })
        .AddScheme<AuthenticationSchemeOptions, IntegrationTestAuthenticationHandler>(
            IntegrationTestAuthHeaders.SchemeName,
            _ => { })
        .AddIdentityCookies();
}

builder.Services.AddAuthorization(options =>
{
    // Tylko Ty
    options.AddPolicy("SystemAdminOnly", policy => policy.RequireRole("Admin"));

    // UWAGA (support impersonation): Admin jest dopisany do polityk tenantowych, by przejść
    // autoryzację endpointów salonu. To NIE otwiera danych samo z siebie — Admin uzyskuje
    // TenantId wyłącznie przy aktywnej sesji wsparcia (ImpersonationMiddleware); bez niej
    // TenantHandler rzuca NoTenantHeader. Realną bramką jest rozwiązanie tenanta, nie rola.

    // Właściciel biznesu
    options.AddPolicy("BusinessManagement", policy => policy.RequireRole("Owner", "Admin"));

    // Właściciel LUB upoważniony pracownik (Manager)
    options.AddPolicy("StaffManagement", policy => policy.RequireRole("Owner", "Manager", "Admin"));

    // Każdy pracownik (Employee, Manager, Owner) oraz konto „Recepcja" (Kiosk).
    // Kiosk dostaje dostęp operacyjny (wizyty, klienci, odczyt katalogu) potrzebny do obsługi
    // recepcji, ale NIE trafia do StaffManagement/BusinessManagement — nie zmieni ustawień,
    // pracowników ani subskrypcji.
    options.AddPolicy("GeneralAccess", policy => policy.RequireRole("Owner", "Manager", "Employee", "Admin", "Kiosk"));
});

builder.Services.AddControllers(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(new AuthorizeFilter(policy));
});

builder.Services
    .AddOptions<DashboardOptions>()
    .Bind(builder.Configuration.GetSection(DashboardOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl), "Dashboard:BaseUrl is required.")
    .ValidateOnStart();

builder.Services
    .AddOptions<TurnstileOptions>()
    .Bind(builder.Configuration.GetSection(TurnstileOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => !options.Enabled || !string.IsNullOrWhiteSpace(options.SecretKey),
        "Turnstile:SecretKey is required when Turnstile is enabled.")
    .ValidateOnStart();

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "booking_saas_antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = useCrossOriginSecureCookies ? SameSiteMode.None : SameSiteMode.Lax;
    options.Cookie.SecurePolicy = useCrossOriginSecureCookies
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
    options.HeaderName = "X-XSRF-TOKEN";
});

// White-label: rejestr customowych domen klientów (snapshot w pamięci, odświeżany w tle).
// Referencję przechwytujemy do `customDomainRegistry` — delegat SetIsOriginAllowed (niżej) biegnie
// per-request, więc instancję podstawiamy po app.Build(), a delegat widzi aktualny snapshot.
builder.Services.AddSingleton<App.Infrastructure.Services.CustomDomainRegistry>();
builder.Services.AddSingleton<App.Application.Common.Interfaces.ICustomDomainRegistry>(
    sp => sp.GetRequiredService<App.Infrastructure.Services.CustomDomainRegistry>());
App.Application.Common.Interfaces.ICustomDomainRegistry? customDomainRegistry = null;

// --- CORS: dev = dowolny port na localhost / 127.0.0.1 / ::1 (http i https); prod = lista z konfiguracji ---
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularClient", policy =>
    {
        // E2E to lokalny test env (Playwright + dashboard:4201 + web:4321) — używa
        // permissive policy jak Development. Bez tego CORS preflight odrzuca origin
        // 4201 (nie ma go w defaults) i UI pokazuje "Brak połączenia z serwerem".
        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("E2E"))
        {
            policy.SetIsOriginAllowed(static origin =>
            {
                if (string.IsNullOrWhiteSpace(origin)) return false;
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
                if (uri.Scheme is not ("http" or "https")) return false;
                var host = uri.IdnHost;
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    || host.Equals("::1", StringComparison.OrdinalIgnoreCase)
                    || host.Equals("[::1]", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Tylko dev/E2E: dopuść prywatne adresy LAN (RFC1918: 10/8, 172.16/12, 192.168/16),
                // żeby dało się testować/demo na telefonie w tej samej sieci. W prod ta gałąź się nie wykonuje.
                if (System.Net.IPAddress.TryParse(host, out var ip)
                    && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    var b = ip.GetAddressBytes();
                    return b[0] == 10
                        || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                        || (b[0] == 192 && b[1] == 168);
                }

                return false;
            });
        }
        else
        {
            var fromConfig = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            if (builder.Environment.IsProduction() && fromConfig.Length == 0)
            {
                throw new InvalidOperationException("Production requires Cors:AllowedOrigins configuration.");
            }

            var defaults = builder.Environment.IsProduction()
                ? []
                : new[]
                {
                    "http://localhost:4200",
                    "http://127.0.0.1:4200",
                    "http://localhost:4321",
                    "http://127.0.0.1:4321",
                };
            var staticOrigins = defaults.Concat(fromConfig).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            // Statyczna whitelista (zapisz.me / admin.zapisz.me) + dynamicznie zarejestrowane white-label
            // originy (https://rezerwacja.{domena-klienta}). Delegat biegnie per-request → widzi aktualny
            // snapshot rejestru. SPA bookingu na rezerwacja.X woła api.X (cross-origin) → CORS wymagany.
            policy.SetIsOriginAllowed(origin =>
                staticOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)
                || (customDomainRegistry?.IsAllowedBookingOrigin(origin) ?? false));
        }

        policy.AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("Retry-After")
            .AllowCredentials();
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    var trustedProxies = builder.Configuration.GetSection("ReverseProxy:TrustedProxies").Get<string[]>() ?? [];
    foreach (var proxy in trustedProxies)
    {
        // CIDR (np. "172.16.0.0/12") → KnownNetworks. Reverse-proxy w Docker bridge
        // dostaje IP z dynamicznej sieci 172.x — bez subnet-trust .NET ignoruje
        // X-Forwarded-Proto i Antiforgery z SecurePolicy=Always rzuca w prod (HTTPS via Caddy).
        if (proxy.Contains('/'))
        {
            if (System.Net.IPNetwork.TryParse(proxy, out var network))
            {
                options.KnownIPNetworks.Add(network);
            }
        }
        else if (IPAddress.TryParse(proxy, out var ipAddress))
        {
            options.KnownProxies.Add(ipAddress);
        }
    }
});

var globalRateLimitPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Global:PermitLimit") ?? 300;
var globalRateLimitWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Global:WindowSeconds") ?? 60;
var globalRateLimitQueueLimit = builder.Configuration.GetValue<int?>("RateLimiting:Global:QueueLimit") ?? 0;
// Zalogowany użytkownik dashboardu (SPA) generuje serie żądań przy każdym przejściu widoku
// — dostaje znacznie większy budżet niż anonimowy IP, dla którego limit pozostaje antyabuse'owy.
var authenticatedRateLimitPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Authenticated:PermitLimit") ?? 2000;
var authenticatedRateLimitWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Authenticated:WindowSeconds") ?? 60;
var authenticatedRateLimitQueueLimit = builder.Configuration.GetValue<int?>("RateLimiting:Authenticated:QueueLimit") ?? 0;
var authRateLimitPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Auth:PermitLimit") ?? 20;
var authRateLimitWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Auth:WindowSeconds") ?? 60;
var publicBookingWriteRateLimitPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:PublicBookingWrite:PermitLimit") ?? 30;
var publicBookingWriteRateLimitWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:PublicBookingWrite:WindowSeconds") ?? 60;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partitionKey = GetRateLimitPartitionKey(context);

        // Health endpointy NIE dzielą globalnej puli per-IP z ruchem aplikacyjnym — mają własny,
        // niezależny budżet (/health/live → polityka "HealthCheck"; /health/ready|smoke|root →
        // DisableRateLimiting). Bez tego flood na innym endpoincie z tego samego IP mógłby zabić
        // monitoring jako collateral. Zob. MapHealthEndpoints.
        // UWAGA: stały klucz partycji ("__health__"), NIE per-IP — PartitionedRateLimiter
        // cache'uje limiter per klucz, więc użycie partitionKey (ip:...) zwróciłoby już
        // zcache'owany FixedWindow z ruchu aplikacji zamiast NoLimiter.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            return RateLimitPartition.GetNoLimiter("__health__");
        }

        // Caddy On-Demand TLS odpytuje /internal/tls-allowed przy KAŻDYM handshake nieznanego hosta —
        // zawsze z jednego IP (kontener Caddy w sieci docker). Per-IP limit zabiłby legalne wystawianie
        // certów podczas floodu nieznanych SNI. Własny stały klucz bez limitu; throttling provisioningu
        // robi on_demand_tls w Caddy, a /internal/* jest blokowane publicznie na edge.
        if (context.Request.Path.StartsWithSegments("/internal"))
        {
            return RateLimitPartition.GetNoLimiter("__internal__");
        }

        var isAuthenticated = partitionKey.StartsWith("user:", StringComparison.Ordinal);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = isAuthenticated ? authenticatedRateLimitPermitLimit : globalRateLimitPermitLimit,
                Window = TimeSpan.FromSeconds(isAuthenticated ? authenticatedRateLimitWindowSeconds : globalRateLimitWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = isAuthenticated ? authenticatedRateLimitQueueLimit : globalRateLimitQueueLimit,
                AutoReplenishment = true,
            });
    });

    options.AddPolicy("AuthSensitive", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authRateLimitPermitLimit,
                Window = TimeSpan.FromSeconds(authRateLimitWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    options.AddPolicy("PublicBookingWrite", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = publicBookingWriteRateLimitPermitLimit,
                Window = TimeSpan.FromSeconds(publicBookingWriteRateLimitWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    // Anonimowy upload obrazów (inspiracje klientek w public bookingu, #2). Upload + obróbka obrazu
    // (dekodowanie, resize, WebP) jest droga CPU/RAM/IO — tightszy niż zwykły booking-write.
    // Policy gotowa do użycia; faktyczny anonimowy endpoint dobuduje #2 na BookingApiControllerBase.
    var publicImageUploadPermit = builder.Configuration.GetValue<int?>("RateLimiting:PublicImageUpload:PermitLimit") ?? 10;
    var publicImageUploadWindow = builder.Configuration.GetValue<int?>("RateLimiting:PublicImageUpload:WindowSeconds") ?? 60;
    options.AddPolicy("PublicImageUpload", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = publicImageUploadPermit,
                Window = TimeSpan.FromSeconds(publicImageUploadWindow),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    // Health-check publiczny (/health/live) — jedyny health endpoint wystawiony przez Caddy
    // dla zewnętrznego uptime-monitora. Liveness NIE dotyka bazy, ale limit per-IP zapobiega
    // używaniu go jako taniego always-200 endpointu do floodu/szumu. /health/smoke (dotyka DB)
    // nie wychodzi przez Caddy — jest weryfikowany wewnętrznie po deployu, więc go nie limitujemy.
    var healthCheckPermit = builder.Configuration.GetValue<int?>("RateLimiting:HealthCheck:PermitLimit") ?? 30;
    var healthCheckWindow = builder.Configuration.GetValue<int?>("RateLimiting:HealthCheck:WindowSeconds") ?? 60;
    options.AddPolicy("HealthCheck", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = healthCheckPermit,
                Window = TimeSpan.FromSeconds(healthCheckWindow),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    // Anonimowe, kosztowne GET-y dostępności (available-slots, month-availability liczy sloty
    // dla każdego dnia miesiąca). Globalny 300/min/IP jest za hojny dla tego kosztu CPU/DB —
    // dedykowany, niższy limit per IP. Wyższy niż write (przeglądanie kalendarza generuje
    // legalnie sporo odczytów), ale ogranicza tani DoS.
    var publicBookingReadPermit = builder.Configuration.GetValue<int?>("RateLimiting:PublicBookingRead:PermitLimit") ?? 60;
    var publicBookingReadWindow = builder.Configuration.GetValue<int?>("RateLimiting:PublicBookingRead:WindowSeconds") ?? 60;
    options.AddPolicy("PublicBookingRead", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = publicBookingReadPermit,
                Window = TimeSpan.FromSeconds(publicBookingReadWindow),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    // Raporty awarii z przeglądarki klientki (publiczny kalendarz). Anonimowe i tanie (sam log,
    // zero DB), ale bez limitu byłyby darmowym generatorem szumu w Seq. Front i tak ogranicza się
    // sam (20 zgłoszeń na kartę + dedup), więc realny ruch mieści się w kilku żądaniach na minutę —
    // limit jest tu wyłącznie przed obcym, który chciałby zapchać nam logi.
    var clientDiagnosticsPermit = builder.Configuration.GetValue<int?>("RateLimiting:PublicClientDiagnostics:PermitLimit") ?? 20;
    var clientDiagnosticsWindow = builder.Configuration.GetValue<int?>("RateLimiting:PublicClientDiagnostics:WindowSeconds") ?? 60;
    options.AddPolicy("PublicClientDiagnostics", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = clientDiagnosticsPermit,
                Window = TimeSpan.FromSeconds(clientDiagnosticsWindow),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    // Webhook operatora płatności (Stripe). Anonimowy, antiforgery-exempt, czyta body do 1 MB i
    // liczy HMAC przed odrzuceniem — bez limitu byłby tanim wektorem DoS (CPU/wątki) na 1 instancji.
    // Limit hojny per-IP: znacznie powyżej realnego retry Stripe (kilka/min z wąskiej puli IP),
    // ale ucina flood. NIE używamy DisableRateLimiting (to wyłącza też GlobalLimiter).
    var webhookPermit = builder.Configuration.GetValue<int?>("RateLimiting:Webhook:PermitLimit") ?? 300;
    var webhookWindow = builder.Configuration.GetValue<int?>("RateLimiting:Webhook:WindowSeconds") ?? 60;
    options.AddPolicy("Webhook", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = webhookPermit,
                Window = TimeSpan.FromSeconds(webhookWindow),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    // Walidacja kodu promo — kody Founding Members są krótkie/zgadywalne, a endpoint zwraca
    // IsValid true/false → wektor enumeracji. Tighter niż zwykły write (10/min/IP).
    var promoValidatePermit = builder.Configuration.GetValue<int?>("RateLimiting:PublicPromoValidate:PermitLimit") ?? 10;
    var promoValidateWindow = builder.Configuration.GetValue<int?>("RateLimiting:PublicPromoValidate:WindowSeconds") ?? 60;
    options.AddPolicy("PublicPromoValidate", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = promoValidatePermit,
                Window = TimeSpan.FromSeconds(promoValidateWindow),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    // 5 zgłoszeń na 10 minut per użytkownik — chroni skrzynkę przed spamem
    options.AddPolicy("FeedbackWrite", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString("0");
        }

        await context.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Zbyt wiele żądań",
                Detail = "Przekroczono limit żądań. Spróbuj ponownie później.",
                Instance = context.HttpContext.Request.Path,
                Extensions =
                {
                    ["errorCode"] = ErrorCodes.RateLimitExceeded,
                    ["messageKey"] = ErrorCodes.RateLimitExceeded,
                },
            },
            cancellationToken);
    };
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("API process is running."), tags: ["live", "ready", "smoke"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready", "smoke"])
    .AddCheck<ConfigurationHealthCheck>("configuration", tags: ["smoke"]);

// --- Baza danych: PostgreSQL (normalnie) lub InMemory (środowisko Testing — testy integracyjne) ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!builder.Environment.IsEnvironment("Testing")
    && string.IsNullOrWhiteSpace(connectionString))
{
  throw new InvalidOperationException(
    "Brak ConnectionStrings:DefaultConnection. Ustaw appsettings.Development.json (lokalnie), zmienne środowiskowe lub sekrety w Azure Key Vault.");
}

var usePostgresForTesting = builder.Environment.IsEnvironment("Testing")
    && builder.Configuration.GetValue<bool>("Testing:UsePostgres");

if (builder.Environment.IsEnvironment("Testing") && !usePostgresForTesting)
{
  // Nazwa musi być ustalona raz na proces budowy hosta — lambda AddDbContext może być wywoływana wielokrotnie;
  // inaczej seed i żądania HTTP lądują w różnych instancjach InMemory.
  var inMemoryDatabaseName = "AppApiTests_" + Guid.NewGuid().ToString("N");
  builder.Services.AddDbContext<ApplicationDbContext>(options =>
      options.UseInMemoryDatabase(inMemoryDatabaseName));
}
else
{
  builder.Services.AddDbContext<ApplicationDbContext>(options =>
      // Jawny CommandTimeout — bound na czas zapytania, żeby patologiczne/zablokowane zapytanie
      // nie trzymało połączenia z puli w nieskończoność (domyślnie 30s; ustawiamy wprost).
      options.UseNpgsql(connectionString, npgsql => npgsql.CommandTimeout(30)));
}

builder.Services.AddScoped<IApplicationDbContext>(provider =>
    provider.GetRequiredService<ApplicationDbContext>());
builder.Services.AddScoped<IUnitOfWork>(provider =>
    provider.GetRequiredService<ApplicationDbContext>());

// W env Testing/E2E identity cookies są już zarejestrowane razem z testowym handlerem
// nagłówkowym (~linia 131) — ponowne AddIdentityCookies tutaj rzuca "Scheme already exists".
if (!builder.Environment.IsEnvironment("Testing") && !isE2eEnvironment)
{
    builder.Services
        .AddAuthentication(IdentityConstants.ApplicationScheme)
        .AddIdentityCookies();
}

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "booking_saas_identity";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = useCrossOriginSecureCookies ? SameSiteMode.None : SameSiteMode.Lax;
    options.Cookie.SecurePolicy = useCrossOriginSecureCookies
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    // ExpireTimeSpan jest globalny, więc różnicowanie "Zapamiętaj mnie" musi iść przez
    // zdarzenie sign-in. Bez RememberMe (isPersistent=false) zostaje cookie sesyjne na
    // 8h. Z RememberMe ustawiamy ExpiresUtc na 30 dni → trwałe cookie; SlidingExpiration
    // odnawia oryginalny czas trwania (30 dni), więc aktywny użytkownik się nie wylogowuje.
    options.Events.OnSigningIn = context =>
    {
        if (context.Properties.IsPersistent)
        {
            context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
        }

        return Task.CompletedTask;
    };
});
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    // Linki wysyłane e-mailem (zaproszenie pracownika, reset hasła, potwierdzenie adresu)
    // korzystają z providera "Default" (DataProtector) — muszą przeżyć dobę, żeby adresat
    // zdążył kliknąć. Dotyczy prowiderów tokenów ustawionych na TokenOptions.DefaultProvider.
    options.TokenLifespan = TimeSpan.FromHours(24);
});

builder.Services
    .AddIdentityCore<User>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 8;
        options.SignIn.RequireConfirmedEmail = !builder.Environment.IsDevelopment()
            && !builder.Environment.IsEnvironment("Testing");
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        // Potwierdzenie e-maila, zaproszenia pracowników i reset hasła to linki wysyłane e-mailem
        // — muszą żyć 24h (DataProtectionTokenProviderOptions.TokenLifespan powyżej). DefaultEmailProvider
        // to token TOTP ważny tylko ~kilka minut, przez co linki umierały zanim adresat zdążył kliknąć.
        options.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultProvider;
        options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// --- Repozytoria i Serwisy Domenowe ---
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<ITenantPurgeService, App.Infrastructure.Persistence.TenantPurgeService>();
builder.Services.AddScoped<IAppointmentHistoryPurger, App.Infrastructure.Persistence.AppointmentHistoryPurger>();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<App.Infrastructure.Persistence.IDemoDataSeeder, App.Infrastructure.Persistence.DemoDataSeeder>();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
builder.Services.AddSingleton<IImpersonationTicketService, App.Api.Security.ImpersonationTicketService>();
builder.Services.AddSingleton<IInspirationUploadTokenService, App.Api.Security.InspirationUploadTokenService>();
builder.Services.Configure<App.Application.Admin.Impersonation.ImpersonationOptions>(
    builder.Configuration.GetSection(App.Application.Admin.Impersonation.ImpersonationOptions.SectionName));
builder.Services.AddScoped<IEmployeeTenantResolver, EmployeeTenantResolver>();
builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
builder.Services.AddScoped<IShiftTemplateRepository, ShiftTemplateRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IServiceCategoryRepository, ServiceCategoryRepository>();
builder.Services.AddScoped<IVatRateRepository, VatRateRepository>();
builder.Services.AddScoped<IServiceRepository, ServiceRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<App.Application.Booking.PromoCodes.IPromoCodeRedemptionService,
                          App.Infrastructure.Services.PromoCodeRedemptionService>();

builder.Services.Configure<AzureCommunicationEmailOptions>(
    builder.Configuration.GetSection(AzureCommunicationEmailOptions.SectionName));
builder.Services.Configure<SmtpEmailOptions>(
    builder.Configuration.GetSection(SmtpEmailOptions.SectionName));
builder.Services.AddMemoryCache();
builder.Services.Configure<App.Infrastructure.Booking.BookingOtpProtectionOptions>(
    builder.Configuration.GetSection(App.Infrastructure.Booking.BookingOtpProtectionOptions.Section));
builder.Services.AddSingleton<IBookingOtpProtection, BookingOtpProtectionService>();
builder.Services.AddSingleton<App.Application.Common.Interfaces.IPlatformMaintenanceState,
                              App.Infrastructure.Middleware.PlatformMaintenanceState>();
builder.Services.AddSingleton<IPasswordResetEmailThrottle, PasswordResetEmailThrottle>();
builder.Services.AddSingleton<IConfirmEmailResendThrottle, ConfirmEmailResendThrottle>();
// Unieważnianie cache'u slug→tenant przy zmianie slugu salonu. Singleton, bo opakowuje
// ten sam IMemoryCache, z którego czyta TenantIdentifierMiddleware.
builder.Services.AddSingleton<App.Application.Common.Interfaces.ITenantSlugCache,
                              App.Infrastructure.Middleware.TenantSlugCache>();

// EMAIL_SENDER_MODE wybiera tylko transport e-mail (IEmailTransport). Sendery
// (auth / OTP / powiadomienia / feedback) są transport-agnostyczne i rejestrowane raz.
// "Smtp" celuje w Mailpit (docker-compose.prod.local.yml) — wszystko trafia do lokalnego
// inboxa, nic nie wychodzi na zewnątrz. Default (brak/Azure) = Azure Communication Services.
var emailSenderMode = builder.Configuration["EmailSender:Mode"]
    ?? Environment.GetEnvironmentVariable("EMAIL_SENDER_MODE");
if (string.Equals(emailSenderMode, "Smtp", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
}
else
{
    builder.Services.AddSingleton<IEmailTransport, AzureCommunicationEmailTransport>();
}

builder.Services.AddSingleton<IAuthEmailSender, AuthEmailSender>();
builder.Services.AddSingleton<IBookingOtpEmailSender, BookingOtpEmailSender>();
builder.Services.AddSingleton<App.Application.Feedback.IFeedbackSender, FeedbackSender>();

// W env "E2E" podmieniamy email + clock na in-memory testowe instancje.
// E2eController (rejestrowany tylko w E2E) wystawia je dla Playwrighta przez backdoor:
// GET /api/_e2e/otp/{email}, /auth-email/{email}, POST /api/_e2e/time/advance.
if (isE2eEnvironment)
{
    var testEmailTransport = new TestEmailTransport();
    var testAuthMailbox = new TestAuthEmailMailbox();
    var testOtpMailbox = new TestBookingOtpMailbox();
    var testPhoneOtpMailbox = new TestPhoneOtpMailbox();
    // E2eTimeProvider: zamrożony zegar ustawialny w obie strony (FakeTimeProvider nie cofa się,
    // przez co /_e2e/time/reset rzucał 500 i zostawiał zegar przesunięty między testami).
    var e2eTimeProvider = new App.Api.E2eSupport.E2eTimeProvider(DateTimeOffset.UtcNow);

    builder.Services.Replace(ServiceDescriptor.Singleton<IEmailTransport>(testEmailTransport));
    builder.Services.Replace(ServiceDescriptor.Singleton<IAuthEmailSender>(testAuthMailbox));
    builder.Services.Replace(ServiceDescriptor.Singleton<IBookingOtpEmailSender>(testOtpMailbox));
    builder.Services.Replace(ServiceDescriptor.Singleton<App.Application.Common.Security.IPhoneOtpSender>(testPhoneOtpMailbox));
    builder.Services.Replace(ServiceDescriptor.Singleton<TimeProvider>(e2eTimeProvider));

    // Dodatkowo wystawiamy konkretne typy (nie tylko interfejsy) — backdoor je czyta bezpośrednio.
    builder.Services.AddSingleton(testEmailTransport);
    builder.Services.AddSingleton(testAuthMailbox);
    builder.Services.AddSingleton(testOtpMailbox);
    builder.Services.AddSingleton(testPhoneOtpMailbox);
    builder.Services.AddSingleton(e2eTimeProvider);
}

// System powiadomień: dispatcher rozsyła NotificationMessage do wszystkich kanałów.
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddScoped<INotificationChannel, EmailNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, InAppNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, SmsNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, App.Infrastructure.Notifications.WebPushNotificationChannel>();
// Custom szablony SMS: resolver (zatwierdzona treść → render+sanityzacja) + podgląd domyślnych.
builder.Services.AddScoped<App.Application.Notifications.Sms.ISmsTemplateResolver, App.Infrastructure.Notifications.Sms.SmsTemplateResolver>();
builder.Services.AddScoped<App.Application.Notifications.Sms.ISmsDefaultTemplateProvider, App.Infrastructure.Notifications.Sms.SmsDefaultTemplateProvider>();
builder.Services.AddScoped<App.Application.Notifications.Sms.ISmsTemplateModerationNotifier, App.Infrastructure.Notifications.Sms.SmsTemplateModerationNotifier>();
// Dziennik zużycia SMS/e-mail (zasila panel „Zużycie SMS / e-mail").
builder.Services.AddScoped<INotificationUsageRecorder, NotificationUsageRecorder>();
// Anti-abuse: twardy miesięczny limit SMS (blokuje wysyłkę po przekroczeniu).
builder.Services.AddScoped<App.Application.Notifications.ISmsUsageGuard, App.Application.Notifications.SmsUsageGuard>();

// SMS provider (smsapi.pl). Opcje + typed HttpClient. Brak retry/Polly w MVP — dispatcher
// izoluje wyjątki, więc awaria SMS nie blokuje pozostałych kanałów.
builder.Services.Configure<App.Infrastructure.Notifications.Sms.SmsApiOptions>(
    builder.Configuration.GetSection("Sms"));
builder.Services.AddHttpClient<
    App.Infrastructure.Notifications.Sms.ISmsApiClient,
    App.Infrastructure.Notifications.Sms.SmsApiClient>((sp, client) =>
    {
        var opts = sp.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<App.Infrastructure.Notifications.Sms.SmsApiOptions>>().Value;
        client.BaseAddress = new Uri(string.IsNullOrWhiteSpace(opts.BaseUrl)
            ? "https://api.smsapi.pl/"
            : opts.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds > 0 ? opts.TimeoutSeconds : 10);
    });

// Web Push (VAPID) — kanał powiadomień do zainstalowanego panelu (PWA), w tym iOS (Safari 16.4+).
// Klucze z env (WEBPUSH__VAPIDPUBLICKEY/VAPIDPRIVATEKEY/SUBJECT). Typed HttpClient z timeoutem 10s
// (jak SMS/Stripe) — push-service nie może wisieć i wysycać wątków API.
builder.Services.Configure<App.Infrastructure.Notifications.Push.WebPushOptions>(
    builder.Configuration.GetSection(App.Infrastructure.Notifications.Push.WebPushOptions.SectionName));
builder.Services.AddSingleton<
    App.Application.Notifications.Push.IWebPushKeys,
    App.Infrastructure.Notifications.Push.WebPushKeys>();
builder.Services.AddHttpClient<
    App.Application.Notifications.Push.IWebPushSender,
    App.Infrastructure.Notifications.Push.WebPushSender>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
    });

// Payment provider (Stripe Connect — zadatki). Direct charges: środki wprost na konto salonu,
// platforma nigdy ich nie trzyma. Sekrety z env (PAYMENTS__STRIPE__SECRETKEY/WEBHOOKSECRET).
// Typed HttpClient z jawnym timeoutem 10s (domyślny StripeClient ma 80s — przy incydencie Stripe
// requesty panelu/generowania wisiałyby i wyczerpywały wątki/DB pool). Retry obsługuje SystemNetHttpClient.
builder.Services.Configure<App.Infrastructure.Payments.Stripe.StripePaymentOptions>(
    builder.Configuration.GetSection("Payments:Stripe"));

// Payments:DevFakeProvider=true (poza Production) → DevFakePaymentProvider: onboarding kończy się
// natychmiast, konto raportuje charges_enabled, więc zadatki są widoczne i klikalne w dev bez konta
// Stripe. Hardening jak przy Sms:DevBackdoorLogCode — w Production flaga jest ignorowana.
var useDevFakePayments = builder.Configuration.GetValue<bool>("Payments:DevFakeProvider")
    && !builder.Environment.IsProduction();

if (useDevFakePayments)
{
    builder.Services.AddSingleton<
        App.Application.Payments.Abstractions.IPaymentProvider,
        App.Infrastructure.Payments.Dev.DevFakePaymentProvider>();
}
else
{
    builder.Services.AddHttpClient<
        App.Application.Payments.Abstractions.IPaymentProvider,
        App.Infrastructure.Payments.Stripe.StripeConnectPaymentProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
}

// Phone OTP sender — wybór implementacji wg środowiska + flagi konfiguracyjnej.
//
//  - E2E env             → TestPhoneOtpMailbox (rejestrowany w bloku isE2eEnvironment WYŻEJ).
//                          Pomijamy default rejestrację — ostatnia rejestracja `IPhoneOtpSender`
//                          wygrywa, więc bez tego guard'a SmsApiPhoneOtpSender nadpisuje mailbox
//                          i testy E2E padają z `SmsServiceUnavailableException`.
//  - Sms:DevBackdoorLogCode=true (poza Production) → LoggingPhoneOtpSender — kod w logach,
//    bez wołania smsapi.pl; pozwala odpalić flow rejestracji w dev bez kosztów.
//  - Default            → SmsApiPhoneOtpSender (realna wysyłka przez smsapi.pl)
//
// Hardening: w env Production flaga DevBackdoorLogCode jest ignorowana — backdoor NIE może
// być włączony przypadkiem w prod.
if (!isE2eEnvironment)
{
    var smsDevBackdoorLogCode = builder.Configuration.GetValue<bool>("Sms:DevBackdoorLogCode");
    if (smsDevBackdoorLogCode && !builder.Environment.IsProduction())
    {
        builder.Services.AddSingleton<
            App.Application.Common.Security.IPhoneOtpSender,
            App.Infrastructure.Notifications.Sms.LoggingPhoneOtpSender>();
    }
    else
    {
        builder.Services.AddScoped<
            App.Application.Common.Security.IPhoneOtpSender,
            App.Infrastructure.Notifications.Sms.SmsApiPhoneOtpSender>();
    }
}

// Auth:DevFixedOtpCode — stały kod OTP (np. "000000") zamiast losowego, żeby dało się przeklikać
// rejestrację i kreator onboardingu bez czytania kodu z logów przy każdym przebiegu.
//
// Gate to IsDevelopment(), a NIE !IsProduction() jak przy DevBackdoorLogCode: pod LOCAL_PROD
// IsProduction() jest true, więc wariant z !IsProduction() byłby tam martwy i kusił, by dopisać
// wyjątek dla LOCAL_PROD — a stamtąd jedna literówka LOCAL_PROD=true na prawdziwym prodzie
// dzieli nas od wyłączenia OTP klientom. Poza Development flaga jest po prostu ignorowana,
// a ValidateProductionConfiguration dodatkowo twardo ubija start prod, jeśli ją ustawiono.
var devFixedOtpCode = builder.Environment.IsDevelopment()
    ? builder.Configuration["Auth:DevFixedOtpCode"]
    : null;

if (!string.IsNullOrWhiteSpace(devFixedOtpCode)
    && !System.Text.RegularExpressions.Regex.IsMatch(devFixedOtpCode, @"^\d{6}$"))
{
    // Fail-fast: walidator ConfirmPhone wymaga ^\d{6}$, więc kod spoza tego formatu dałby OTP,
    // którego NIE DA SIĘ wpisać — cichy ślepy zaułek zamiast ułatwienia.
    throw new InvalidOperationException(
      $"Auth:DevFixedOtpCode musi być dokładnie 6 cyframi (jest: '{devFixedOtpCode}'). "
      + "Walidator ConfirmPhone wymaga ^\\d{6}$ — inny format tworzy OTP nie do wpisania.");
}

builder.Services.AddSingleton(
    string.IsNullOrWhiteSpace(devFixedOtpCode)
        ? App.Application.Common.Security.PhoneOtpDevOptions.Disabled
        : new App.Application.Common.Security.PhoneOtpDevOptions(devFixedOtpCode));

// Real-time push powiadomień in-app przez SignalR. Backplane Redis tylko gdy skonfigurowany
// ConnectionStrings:Redis — inaczej single-instance in-memory (zgodnie z obecnym posture'em aplikacji).
var signalRBuilder = builder.Services.AddSignalR();
var signalRRedisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(signalRRedisConn))
{
    signalRBuilder.AddStackExchangeRedis(signalRRedisConn, o =>
        o.Configuration.ChannelPrefix =
            StackExchange.Redis.RedisChannel.Literal("booking_saas_signalr"));
}
builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddHttpClient<ITurnstileVerifier, CloudflareTurnstileVerifier>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Storage obrazów (S3-compatible): PROD = Cloudflare R2, LOKALNIE = MinIO — przełączane sekcją
// `Storage` w konfiguracji. Sekrety (access/secret key) TYLKO z env STORAGE__ACCESSKEY/SECRETKEY.
// Rejestruje IFileStorage + IImageProcessingService (pipeline: magic-bytes, EXIF strip, WebP+thumb).
// FUNDAMENT dla #5 (zdjęcia usług, MediaController) i #2 (inspiracje klientek w public bookingu).
builder.Services.AddImageStorage(builder.Configuration);

// --- Warstwy Zewnętrzne i Zadania w tle ---
builder.Services.AddApplicationServices(builder.Configuration);

// W env E2E NIE startujemy BG services jako HostedService (ich pętle by przeszkadzały
// w deterministycznych testach), ale rejestrujemy je jako Singleton — backdoor
// /api/_e2e/bg/run-* je pobiera i woła ExecuteOnceAsync() na żądanie z Playwrighta.
if (!skipDbStartup && !isE2eEnvironment)
{
    builder.Services.AddHostedService<AppointmentStatusLifecycleHostedService>();
    builder.Services.AddHostedService<AppointmentReminderHostedService>();
}
else if (isE2eEnvironment)
{
    builder.Services.AddSingleton<AppointmentStatusLifecycleHostedService>();
    builder.Services.AddSingleton<AppointmentReminderHostedService>();
}

// Cleanup niepotwierdzonych rejestracji nie powinien startować w testach integracyjnych
// ani w E2E (kasowałby świeżo seedowane konta) — w E2E wciąż dostępny przez backdoor.
// Nie startuje też przy skipDbStartup (np. ProductionConfigIntrospectionTests: env=Production
// + SKIP_DB_STARTUP=1) — pętla biłaby do nieistniejącego DB i sypała stackami w logach.
if (!builder.Environment.IsEnvironment("Testing") && !isE2eEnvironment && !skipDbStartup)
{
    builder.Services.AddHostedService<UnconfirmedAccountCleanupHostedService>();
}
else if (isE2eEnvironment)
{
    builder.Services.AddSingleton<UnconfirmedAccountCleanupHostedService>();
}

// Cleanup efemerycznych demo-tenantów (hard-delete po TTL). Jak wyżej: nie startuje
// w Testing/E2E jako pętla (ani przy skipDbStartup) — w E2E dostępny przez backdoor.
if (!builder.Environment.IsEnvironment("Testing") && !isE2eEnvironment && !skipDbStartup)
{
    builder.Services.AddHostedService<DemoTenantCleanupHostedService>();
}
else if (isE2eEnvironment)
{
    builder.Services.AddSingleton<DemoTenantCleanupHostedService>();
}

// Cleanup wygasłych skróconych linków (zapisz.me/p/{code}) — jak wyżej: pętla nie startuje
// w Testing/E2E ani przy skipDbStartup; w E2E dostępny przez backdoor.
if (!builder.Environment.IsEnvironment("Testing") && !isE2eEnvironment && !skipDbStartup)
{
    builder.Services.AddHostedService<ShortLinkCleanupHostedService>();
}
else if (isE2eEnvironment)
{
    builder.Services.AddSingleton<ShortLinkCleanupHostedService>();
}

// Cleanup starych powiadomień in-app (Notifications) po oknie retencji — jak wyżej: pętla nie
// startuje w Testing/E2E ani przy skipDbStartup; w E2E dostępny przez backdoor.
if (!builder.Environment.IsEnvironment("Testing") && !isE2eEnvironment && !skipDbStartup)
{
    builder.Services.AddHostedService<NotificationCleanupHostedService>();
}
else if (isE2eEnvironment)
{
    builder.Services.AddSingleton<NotificationCleanupHostedService>();
}

// Cleanup wygasłych OTP (SelfServiceOtps + PhoneConfirmationOtps + Appointments.OtpVerification)
// po oknie retencji — usuwa surowe PII (e-mail/telefon) trzymane przy kodach OTP. Jak wyżej:
// pętla nie startuje w Testing/E2E ani przy skipDbStartup; w E2E dostępny przez backdoor.
if (!builder.Environment.IsEnvironment("Testing") && !isE2eEnvironment && !skipDbStartup)
{
    builder.Services.AddHostedService<OtpCleanupHostedService>();
}
else if (isE2eEnvironment)
{
    builder.Services.AddSingleton<OtpCleanupHostedService>();
}

// Tryb „nie przechowuj historii wizyt": cyklicznie hard-kasuje terminalne/przeszłe wizyty
// salonów z włączoną flagą (Tenant.DoNotRetainAppointmentHistory). Jak wyżej: pętla nie startuje
// w Testing/E2E ani przy skipDbStartup; w E2E dostępny przez backdoor.
if (!builder.Environment.IsEnvironment("Testing") && !isE2eEnvironment && !skipDbStartup)
{
    builder.Services.AddHostedService<AppointmentHistoryPurgeHostedService>();
}
else if (isE2eEnvironment)
{
    builder.Services.AddSingleton<AppointmentHistoryPurgeHostedService>();
}

// White-label: odświeżanie snapshotu customowych domen (zasila CORS + ask On-Demand TLS). Tani odczyt
// z bazy co kilka minut. Nie startuje w Testing (white-label nieużywane) ani przy skipDbStartup (brak DB);
// w E2E działa — to nieszkodliwy refresh cache.
if (!builder.Environment.IsEnvironment("Testing") && !skipDbStartup)
{
    builder.Services.AddHostedService<App.Infrastructure.BackgroundJobs.CustomDomainRegistryRefresher>();
}

// --- Kontrolery, Błędy i OpenAPI (Swagger) ---
builder.Services.AddHttpContextAccessor();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalFallbackExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();

// Pełna specyfikacja — klient Angular (NSwag: documentName = v1).
builder.Services.AddOpenApiDocument(config =>
{
    config.DocumentName = OpenApiDocuments.FullApi;
    config.PostProcess = document =>
    {
        document.Info.Version = "v1";
        document.Info.Title = "Booking SaaS API";
        document.Info.Description = "API for Barber Shop Booking System";
    };
});

// Tylko kontrolery z grupy ApiExplorer „Booking” (folder Controllers/Booking) — klient Astro (NSwag: v1-booking).
builder.Services.AddOpenApiDocument(config =>
{
    config.DocumentName = OpenApiDocuments.BookingOnly;
    config.ApiGroupNames = [OpenApiDocuments.BookingApiGroupName];
    config.PostProcess = document =>
    {
        document.Info.Version = "v1";
        document.Info.Title = "Booking SaaS — Booking API";
        document.Info.Description = "Publiczne endpointy rezerwacji (kalendarz Astro / Svelte).";
    };
});


// HSTS hardening: domyślny UseHsts() daje max-age 30 dni bez includeSubDomains/preload.
// Dla domeny zapisz.me chcemy pełny rok + subdomeny + preload (api/admin/booking to subdomeny).
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

var app = builder.Build();

// White-label: podstaw zbudowaną instancję rejestru do delegata CORS (SetIsOriginAllowed biegnie
// per-request, więc po tym przypisaniu widzi aktualny snapshot customowych domen).
customDomainRegistry = app.Services.GetRequiredService<App.Application.Common.Interfaces.ICustomDomainRegistry>();

// ==========================================
// 2. POTOK PRZETWARZANIA (MIDDLEWARE PIPELINE)
// ==========================================

if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi();
}

// KOLEJNOŚĆ: log żądań MUSI stać PRZED UseExceptionHandler, czyli być od niego ZEWNĘTRZNY.
// Wcześniej było odwrotnie i log kłamał o statusie: wyjątek domenowy (np. NoTenantHeader) przechodził
// przez middleware Serilloga w górę, więc ten zapisywał Error + 500 + pełny stack — a handler,
// stojący wyżej, zamieniał to na czyste 400 `tenant.missing` i TO dostawał klient. W strumieniu
// produkcyjnym dawało to 14 fałszywych piątek na 2 godziny, każdą ze stack tracem.
// Teraz Serilog widzi odpowiedź PO obsłużeniu wyjątku, więc loguje status faktycznie zwrócony.
// Nie tracimy diagnostyki 5xx: `GlobalFallbackExceptionHandler` w gałęzi `default` sam robi
// LogError(exception, ...) ze stackiem, niezależnie od logu żądania.
// EnrichDiagnosticContext wykonuje się przy ZAMYKANIU żądania, więc wartości ustawiane przez
// middleware wewnętrzne (TraceIdentifier, przepisane RemoteIpAddress z UseForwardedHeaders,
// TenantId/ImpersonationSessionId z HttpContext.Items) są wtedy już na miejscu.
// Powyżej tego progu (ms) udane żądanie zostaje na Information — sygnał o problemie wydajnościowym.
const double SlowRequestThresholdMs = 500;
app.UseSerilogRequestLogging(options =>
{
    // Logger TEGO hosta, nie globalny `Log.Logger`. Domyślnie middleware Serilloga sięga po
    // statyczny logger procesu — w produkcji to bez różnicy (host jest jeden), ale wiąże log żądań
    // ze stanem globalnym. W testach integracyjnych, gdzie xunit trzyma wiele hostów naraz,
    // `Log.Logger` należy do tego, który zbudował się OSTATNI, więc sinki wpięte przez
    // `ReadFrom.Services` w pozostałych hostach nie dostawały żadnych zdarzeń żądań.
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, elapsed, exception) =>
    {
        if (exception != null || httpContext.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        // Sonda Caddy On-Demand TLS: 404 jest tu POPRAWNĄ odpowiedzią „ta domena niedozwolona",
        // a nie błędem. Caddy pyta przy KAŻDYM handshake nieznanego hosta, więc boty skanujące IP
        // z losowym SNI generują tysiące takich pytań — w 8 dni produkcji 13 661 z 16 469 wszystkich
        // 404, każde jako Warning. To był największy pojedynczy producent szumu w strumieniu.
        // 5xx obsłużone wyżej i nadal leci jako Error: awaria samego endpointu to realny problem,
        // bo wtedy Caddy przestaje wystawiać certyfikaty dla domen klientów.
        if (httpContext.Request.Path.StartsWithSegments("/internal/tls-allowed"))
        {
            return LogEventLevel.Debug;
        }

        if (httpContext.Response.StatusCode >= 400)
        {
            return LogEventLevel.Warning;
        }

        // Trwałe połączenie huba SignalR. „Czas trwania" to tu ŻYWOTNOŚĆ POŁĄCZENIA, nie opóźnienie:
        // zmierzone na produkcji 4,5 s – 97,6 s, i im zdrowsze połączenie, tym większa liczba. Bez
        // tego wyjątku każde rozłączenie wpadało w próg „wolnego żądania" niżej i lądowało na
        // Information, zaśmiecając JEDYNY sygnał o realnym problemie wydajnościowym przy 2xx.
        //
        // Dwa warunki, bo transport nie jest z góry znany: panel buduje HubConnection bez
        // ograniczenia transportów, więc przy blokadzie WebSocketów SignalR schodzi na SSE albo
        // long polling — a te są długie i mają status 200, nie 101.
        //
        // Handshake (POST /hubs/*/negotiate) świadomie ZOSTAJE pod progiem: jest krótki, więc wolne
        // nawiązywanie sesji nadal zobaczymy. Nieudane połączenie ma 4xx/5xx i łapie je warunek wyżej.
        var path = httpContext.Request.Path;
        var trwalePolaczenieHuba =
            httpContext.Response.StatusCode == StatusCodes.Status101SwitchingProtocols
            || (path.StartsWithSegments("/hubs")
                && !(path.Value ?? string.Empty).EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase));

        if (trwalePolaczenieHuba)
        {
            return LogEventLevel.Debug;
        }

        // Wolne żądania (>500ms) zostają na Information — to jedyny sygnał o problemie wydajnościowym
        // przy statusie 2xx, więc nie chcemy go tłumić.
        if (elapsed > SlowRequestThresholdMs)
        {
            return LogEventLevel.Information;
        }

        // Zdrowe, szybkie 2xx/3xx (w tym cykliczne sondy /health/*) -> Debug. Jedna linia na KAŻDE
        // żądanie panelu zalewała strumień bez treści diagnostycznej; realny ruch widać w metrykach,
        // a wszystko nietrywialne (4xx/5xx, wolne) nadal leci wyżej. Na produkcji (min. Information)
        // te wpisy nie trafiają do Seq.
        return LogEventLevel.Debug;
    };
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.TraceIdentifier);
        diagnosticContext.Set("ClientIp", SanitizeIpAddress(httpContext.Connection.RemoteIpAddress));
        diagnosticContext.Set("UserId", GetUserId(httpContext));
        diagnosticContext.Set("ClientMode", GetClientMode(httpContext));

        if (httpContext.Items.TryGetValue(TenantResolutionHttpContextKeys.ResolvedTenantId, out var tenantId))
        {
            diagnosticContext.Set("TenantId", tenantId);
        }

        if (httpContext.Items.TryGetValue(TenantResolutionHttpContextKeys.ResolvedCallerEmployeeId, out var employeeId))
        {
            diagnosticContext.Set("CallerEmployeeId", employeeId);
        }

        // Audyt support impersonation: każdy log żądania w trakcie sesji niesie id sesji + admina.
        if (httpContext.Items.TryGetValue(ImpersonationHttpContextKeys.SessionId, out var impSessionId))
        {
            diagnosticContext.Set("ImpersonationSessionId", impSessionId);
        }

        if (httpContext.Items.TryGetValue(ImpersonationHttpContextKeys.AdminUserId, out var impAdminId))
        {
            diagnosticContext.Set("ImpersonationAdminUserId", impAdminId);
        }
    };
});

app.UseExceptionHandler(); // Obsługa wyjątków przez zdefiniowany wcześniej fallback
// W Development API zwykle działa na http://localhost:5140 — przekierowanie na HTTPS psuje CORS (fetch + redirect).
// W LOCAL_PROD ten sam Production build siedzi za Caddy bez SSL (caddyfile.local) — redirect na HTTPS
// kończyłby się pętlą.
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders();
    if (!isLocalProd)
    {
        app.UseHttpsRedirection();
        app.UseHsts();
    }
}

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd(
        "Permissions-Policy",
        "camera=(), microphone=(), geolocation=(), payment=()");
    context.Response.Headers.TryAdd(
        "Content-Security-Policy",
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
    context.Response.Headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin");
    // API jest Z ZAŁOŻENIA zasobem cross-site: booking-web (zapisz.me) i panel
    // (admin.zapisz.me) to inne *site* niż api.zapisz.me. CORP musi być `cross-origin`,
    // inaczej przeglądarka blokuje legalne wywołania frontendów (m.in. /public-appointment/hold).
    // Faktyczną kontrolą dostępu pozostaje CORS (whitelista origin + AllowCredentials).
    context.Response.Headers.TryAdd("Cross-Origin-Resource-Policy", "cross-origin");
    await next();
});

// Kolejność tutaj jest KRYTYCZNA
app.UseRouting();
app.UseCors("AngularClient");      // 1. Zezwól Angularowi na dostęp
app.UseAuthentication();           // 2. Kim jest użytkownik? (cookie Identity albo nagłówki testowe)
if (!app.Environment.IsEnvironment("Testing") && !app.Environment.IsEnvironment("E2E"))
{
    app.Use(async (context, next) =>
    {
        if (RequiresAntiforgeryValidation(context))
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            await antiforgery.ValidateRequestAsync(context);
        }

        await next();
    });
}
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRateLimiter();              // 2a. Globalny limit żądań per użytkownik/IP (in-memory)
app.UseMiddleware<TenantIdentifierMiddleware>(); // 2b. Tenant z Employee.user_id wg Identity user id
app.UseMiddleware<App.Infrastructure.Middleware.ImpersonationMiddleware>(); // 2c. Support: Admin z cookie sesji → nadpisanie tenanta
app.UseMiddleware<App.Api.Middleware.PlatformMaintenanceMiddleware>(); // 2d. Globalny tryb serwisowy: blokada zapisu poza adminem platformy
app.UseAuthorization();            // 3. Czy ma uprawnienia do tego zasobu?

app.MapControllers().RequireCors("AngularClient");
app.MapHub<NotificationHub>("/hubs/notifications").RequireCors("AngularClient");
MapHealthEndpoints(app);


// ==========================================
// 3. ZADANIA STARTOWE (MIGRACJE I SEED)
// ==========================================

if (!skipDbStartup)
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    if (!app.Environment.IsEnvironment("Testing"))
    {
        await context.Database.MigrateAsync();
        // Bootstrap prod admina z BOOKING_ADMIN_EMAIL/PASSWORD (idempotentne).
        await DbSeeder.EnsureProductionBootstrapAsync(context);
    }

    if (ShouldSeedDemoData(app.Environment))
    {
        await DbSeeder.SeedAsync(context);
    }
}

if (runMigrationsAndExit)
{
    return;
}

app.Run();

static string GetRateLimitPartitionKey(HttpContext context)
{
    var userId = GetUserId(context);
    if (!string.IsNullOrWhiteSpace(userId))
    {
        return $"user:{userId}";
    }

    return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

static string? GetUserId(HttpContext context)
{
    return context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}

/// <summary>
/// Czy żądanie przyszło z ZAINSTALOWANEJ PWA, czy ze zwykłej przeglądarki — z nagłówka
/// <c>X-Client-Mode</c>, który dokleja dashboard.
///
/// Serwer nie ma jak tego wywnioskować sam: na iOS User-Agent aplikacji z ekranu głównego jest
/// identyczny jak Safari. Bez tego rozróżnienia log pokazywał utracone sesje bez ani jednej
/// przesłanki, w którym kontekście zniknęły — a kontekst jest tu główną hipotezą, bo zainstalowana
/// PWA na iOS ma osobny magazyn ciasteczek niż przeglądarka.
///
/// Nagłówek pochodzi od klienta, więc nie ufamy mu poza diagnostyką: dopuszczamy wyłącznie dwie
/// znane wartości, żeby dowolny napis nie trafiał do logu (i do indeksu Loki) jako etykieta.
/// </summary>
static string GetClientMode(HttpContext context)
{
    var raw = context.Request.Headers["X-Client-Mode"].ToString();
    return raw is "standalone" or "browser" ? raw : "unknown";
}

static void ValidateProductionConfiguration(WebApplicationBuilder builder)
{
    if (!builder.Environment.IsProduction())
    {
        return;
    }

    var isLocalProd = string.Equals(
        builder.Configuration["LOCAL_PROD"]
            ?? Environment.GetEnvironmentVariable("LOCAL_PROD"),
        "true",
        StringComparison.OrdinalIgnoreCase);

    // LOCAL_PROD=true wycina tylko walidację ACS connection string (bo lokalnie ide
    // przez SmtpEmailTransport → Mailpit). Reszta walidacji jest dokładnie taka
    // sama jak prod — w tym wymaganie HTTPS w Dashboard.BaseUrl i CORS, bo lokalny
    // Caddy wystawia self-signed cert przez `tls internal` (caddyfile.local).
    var dashboardBaseUrl = builder.Configuration["Dashboard:BaseUrl"];
    if (!Uri.TryCreate(dashboardBaseUrl, UriKind.Absolute, out var dashboardUri)
        || dashboardUri.Scheme != Uri.UriSchemeHttps)
    {
        throw new InvalidOperationException("Production requires Dashboard:BaseUrl with an HTTPS absolute URL.");
    }

    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (allowedOrigins.Length == 0
        || allowedOrigins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps))
    {
        throw new InvalidOperationException("Production requires Cors:AllowedOrigins with HTTPS absolute URLs.");
    }

    var turnstileEnabled = builder.Configuration.GetValue<bool>("Turnstile:Enabled");
    var turnstileSecretKey = builder.Configuration["Turnstile:SecretKey"];
    var turnstileInvisibleSecretKey = builder.Configuration["Turnstile:InvisibleSecretKey"];
    if (!turnstileEnabled
        || string.IsNullOrWhiteSpace(turnstileSecretKey)
        || string.IsNullOrWhiteSpace(turnstileInvisibleSecretKey))
    {
        throw new InvalidOperationException(
          "Production requires Cloudflare Turnstile enabled + Turnstile:SecretKey (Managed widget) + Turnstile:InvisibleSecretKey (Invisible widget).");
    }

    var trustedProxies = builder.Configuration.GetSection("ReverseProxy:TrustedProxies").Get<string[]>() ?? [];
    if (trustedProxies.Length == 0)
    {
        throw new InvalidOperationException("Production requires ReverseProxy:TrustedProxies configuration.");
    }

    // DataProtection: bez trwałego KeysPath klucze lądują w ulotnym katalogu kontenera i KAŻDY deploy
    // unieważnia tokeny Identity (confirm-email / reset-hasła / invite) oraz ciasteczka sesji → masowe
    // wylogowania i martwe linki. Lepiej crashnąć przy starcie niż cicho stracić trwałość kluczy.
    var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
    if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
    {
        throw new InvalidOperationException(
          "Production requires DataProtection:KeysPath (trwały wolumen) — bez tego każdy deploy "
          + "unieważnia tokeny Identity i ciasteczka sesji.");
    }

    // DataProtection at-rest encryption: bez certyfikatu master-keye zapisują się JAWNIE na
    // wolumenie — wyciek pliku = sfałszowanie sesji/tokenów Identity. W prawdziwym prod cert
    // jest WYMAGANY (twardy błąd startu zamiast cichej luki). LOCAL_PROD jedzie na HTTP localhost
    // bez sekretów PKI, więc tam szyfrowanie key ringu pomijamy.
    if (!isLocalProd)
    {
        var dataProtectionCertPath = builder.Configuration["DataProtection:CertificatePath"];
        if (string.IsNullOrWhiteSpace(dataProtectionCertPath))
        {
            throw new InvalidOperationException(
              "Production requires DataProtection:CertificatePath (PFX) do szyfrowania key ringu "
              + "at-rest — bez tego master-keye zapisują się jawnie i wyciek pliku pozwala sfałszować "
              + "sesje/tokeny Identity. Ustaw DataProtection:CertificatePath (+ CertificatePassword).");
        }
    }

    // SMS-first: OTP SMS jest WYMAGANE przy rejestracji (właściciel + klient). Bez tokenu
    // smsapi.pl każda rejestracja kończy się 503 w runtime — a health-check tego nie czerwieni.
    // Lepiej crashnąć przy starcie niż cicho zablokować pozyskiwanie kont.
    var smsTestMode = builder.Configuration.GetValue<bool>("Sms:TestMode");
    var smsOAuthToken = builder.Configuration["Sms:OAuthToken"];

    // Sms:TestMode=true w PRAWDZIWYM prod cicho wyłącza realny OTP SMS i znosi wymóg tokenu —
    // klienci/właściciele „weryfikują" się dowolnym kodem, OTP staje się fikcją. To musi
    // zatrzymać start. LOCAL_PROD świadomie jedzie w trybie test (brak realnego smsapi lokalnie).
    if (smsTestMode && !isLocalProd)
    {
        throw new InvalidOperationException(
          "Production: Sms:TestMode=true cicho wyłącza realny OTP SMS i znosi wymóg tokenu smsapi.pl "
          + "— weryfikacja kodem staje się fikcją (krytyczna luka uwierzytelniania). Ustaw "
          + "Sms:TestMode=false w prod; TestMode wolno włączać wyłącznie w LOCAL_PROD/dymnych testach.");
    }

    // Auth:DevFixedOtpCode w prod = każdy zna kod SMS każdego konta → OTP przestaje cokolwiek
    // weryfikować. Runtime i tak zignoruje flagę (gate na IsDevelopment), ale sama jej OBECNOŚĆ
    // w prodowym configu oznacza, że ktoś skopiował dev-owe ustawienia — wolimy crashnąć start
    // niż dowiedzieć się o tym z incydentu.
    //
    // ŚWIADOMIE bez wyjątku dla LOCAL_PROD (inaczej niż Sms:TestMode): LOCAL_PROD to jedyna
    // furtka, przez którą ta flaga mogłaby wjechać na prawdziwy prod przy literówce w env.
    // W LOCAL_PROD kod OTP i tak jest w logach (Sms:TestMode → brak realnej wysyłki).
    if (!string.IsNullOrWhiteSpace(builder.Configuration["Auth:DevFixedOtpCode"]))
    {
        throw new InvalidOperationException(
          "Production: Auth:DevFixedOtpCode jest ustawione — stały kod OTP oznacza, że każdy zna "
          + "kod SMS każdego konta (krytyczna luka uwierzytelniania). Usuń Auth:DevFixedOtpCode "
          + "z konfiguracji produkcyjnej; ta flaga jest wyłącznie dla Development. "
          + "Brak wyjątku dla LOCAL_PROD jest celowy.");
    }

    if (!smsTestMode && string.IsNullOrWhiteSpace(smsOAuthToken))
    {
        throw new InvalidOperationException(
          "Production requires Sms:OAuthToken (SMSAPI__OAUTH_TOKEN) — SMS-first OTP przy rejestracji "
          + "nie zadziała bez tokenu smsapi.pl.");
    }

    // Pole nadawcy musi być zarejestrowane w smsapi. Puste = każda wysyłka odrzucana dopiero w runtime,
    // a health-check pozostaje zielony — czyli cicho niedziałające OTP. Lepiej nie wystartować.
    if (!smsTestMode && string.IsNullOrWhiteSpace(builder.Configuration["Sms:SenderName"]))
    {
        throw new InvalidOperationException(
          "Production requires Sms:SenderName (Sms__SENDER_NAME) — pole nadawcy zarejestrowane w smsapi.pl.");
    }

    // Web Push (VAPID): kanał powiadomień do zainstalowanego panelu jest WYMAGANY w prod
    // (decyzja produktowa). Bez kompletu kluczy VAPID push nie zadziała — twardy błąd startu
    // zamiast cichej luki. UWAGA: przed deployem dodaj klucze do zaszyfrowanego env, inaczej
    // start prod padnie. Wygeneruj: `npx web-push generate-vapid-keys`.
    var webPushPublicKey = builder.Configuration["WebPush:VapidPublicKey"];
    var webPushPrivateKey = builder.Configuration["WebPush:VapidPrivateKey"];
    var webPushSubject = builder.Configuration["WebPush:Subject"];
    if (string.IsNullOrWhiteSpace(webPushPublicKey)
        || string.IsNullOrWhiteSpace(webPushPrivateKey)
        || string.IsNullOrWhiteSpace(webPushSubject))
    {
      throw new InvalidOperationException(
        "Production requires WebPush VAPID keys (WEBPUSH__VAPIDPUBLICKEY + WEBPUSH__VAPIDPRIVATEKEY "
        + "+ WEBPUSH__SUBJECT) — powiadomienia push do panelu nie zadziałają bez kompletu kluczy VAPID.");
    }

    // Zadatki (Stripe) są opcjonalne w prod (pusty klucz = funkcja wyłączona, bezpieczny default).
    // ALE jeśli klucz jest ustawiony, to webhook secret MUSI być — inaczej salon pobiera płatność,
    // a webhook potwierdzający zwraca 400 i wizyta nigdy nie przechodzi w „opłacona" (klient płaci,
    // rezerwacja niepotwierdzona). Lepiej crashnąć przy starcie niż dopuścić tę asymetrię.
    var stripeSecretKey = builder.Configuration["Payments:Stripe:SecretKey"];
    var stripeWebhookSecret = builder.Configuration["Payments:Stripe:WebhookSecret"];
    if (!string.IsNullOrWhiteSpace(stripeSecretKey) && string.IsNullOrWhiteSpace(stripeWebhookSecret))
    {
        throw new InvalidOperationException(
          "Production: Payments:Stripe:SecretKey jest ustawiony, ale brak Payments:Stripe:WebhookSecret "
          + "(PAYMENTS__STRIPE__WEBHOOKSECRET). Bez sekretu webhooka płatność nie zostanie potwierdzona "
          + "(klient płaci, wizyta nie-opłacona). Ustaw oba albo wyłącz zadatki (pusty SecretKey).");
    }

    // Storage obrazów: w prawdziwym prod jedziemy na Cloudflare R2. Gdy provider=R2, wymagamy
    // kompletu: endpoint + bucket + publiczny base URL + sekrety (z env STORAGE__ACCESSKEY/SECRETKEY).
    // Brak któregokolwiek = upload/odczyt obrazów cicho pada w runtime → lepiej crashnąć przy starcie.
    // LOCAL_PROD jedzie na MinIO (provider=MinIO), więc tej walidacji R2 tam nie egzekwujemy.
    var storageProvider = builder.Configuration["Storage:Provider"];
    if (string.Equals(storageProvider, "R2", StringComparison.OrdinalIgnoreCase))
    {
        var storageEndpoint = builder.Configuration["Storage:Endpoint"];
        var storageBucket = builder.Configuration["Storage:Bucket"];
        var storagePublicBaseUrl = builder.Configuration["Storage:PublicBaseUrl"];
        var storageAccessKey = builder.Configuration["Storage:AccessKey"];
        var storageSecretKey = builder.Configuration["Storage:SecretKey"];

        if (string.IsNullOrWhiteSpace(storageEndpoint)
            || string.IsNullOrWhiteSpace(storageBucket)
            || string.IsNullOrWhiteSpace(storagePublicBaseUrl)
            || string.IsNullOrWhiteSpace(storageAccessKey)
            || string.IsNullOrWhiteSpace(storageSecretKey))
        {
            throw new InvalidOperationException(
              "Production (Storage:Provider=R2) requires Storage:Endpoint, Storage:Bucket, "
              + "Storage:PublicBaseUrl oraz sekrety STORAGE__ACCESSKEY / STORAGE__SECRETKEY. "
              + "Bez kompletu konfiguracji upload/odczyt obrazów pada w runtime.");
        }

        // Upload do R2 wymusza UNSIGNED-PAYLOAD (StorageOptions.DisablePayloadSigning), a AWS SDK
        // dopuszcza to WYŁĄCZNIE po HTTPS. Ta flaga jest wyprowadzana ze schematu endpointu, więc
        // endpoint bez "https://" cicho odwróciłby ją na false → R2 zwraca 500 na każdym uploadzie.
        // Egzekwujemy schemat przy starcie: lepiej crashnąć niż wywalić się dopiero na uploadzie.
        if (!storageEndpoint!.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
              "Production (Storage:Provider=R2) requires Storage:Endpoint to start with 'https://' "
              + $"(got '{storageEndpoint}'). R2 upload uses UNSIGNED-PAYLOAD, which the AWS SDK only "
              + "permits over HTTPS — a non-HTTPS endpoint would return 500 on every upload.");
        }
    }

    // W LOCAL_PROD email leci przez SmtpEmailTransport, więc ACS connection string
    // w ogóle nie jest używany — walidujemy go tylko gdy jedziemy na prawdziwym Azure.
    var emailMode = builder.Configuration["EmailSender:Mode"]
        ?? Environment.GetEnvironmentVariable("EMAIL_SENDER_MODE");
    if (string.Equals(emailMode, "Smtp", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    // Walidujemy *format* ACS connection-stringa, nie tylko obecność. ACS oczekuje
    // "endpoint=...;accesskey=..." — sam URL endpointa wywala EmailClient parser
    // per-request (500 przy /api/auth/*). Lepiej crashnąć przy starcie.
    var acsConnectionString = builder.Configuration["AzureCommunication:Email:ConnectionString"];
    if (string.IsNullOrWhiteSpace(acsConnectionString))
    {
        throw new InvalidOperationException(
          "Production requires AzureCommunication:Email:ConnectionString.");
    }
    if (acsConnectionString.IndexOf("endpoint=", StringComparison.OrdinalIgnoreCase) < 0
        || acsConnectionString.IndexOf("accesskey=", StringComparison.OrdinalIgnoreCase) < 0)
    {
        throw new InvalidOperationException(
          "AzureCommunication:Email:ConnectionString must be a full ACS connection string "
          + "in the form 'endpoint=https://...;accesskey=...', not just the endpoint URL.");
    }
}

static bool RequiresAntiforgeryValidation(HttpContext context)
{
    if (HttpMethods.IsGet(context.Request.Method)
        || HttpMethods.IsHead(context.Request.Method)
        || HttpMethods.IsOptions(context.Request.Method)
        || HttpMethods.IsTrace(context.Request.Method))
    {
        return false;
    }

    var path = context.Request.Path;
    if (!path.StartsWithSegments("/api"))
    {
        return false;
    }

    if (path.StartsWithSegments("/api/booking"))
    {
        return false;
    }

    // Raport awarii z publicznego kalendarza. `StartsWithSegments("/api/booking")` go NIE łapie
    // (osobny segment `booking-diagnostics`), a token antiforgery jest tu nie do zdobycia: zgłoszenie
    // leci właśnie wtedy, gdy aplikacja klientki jest w rozsypce, czasem przy zamykaniu karty.
    // Ryzyko CSRF zerowe — endpoint nic nie zmienia, tylko dopisuje linię do logu (i ma własny limit).
    if (path.StartsWithSegments("/api/booking-diagnostics"))
    {
        return false;
    }

    // Webhook operatora płatności (Stripe) — żądanie serwer-serwer, bez przeglądarki/cookie CSRF.
    // Autentyczność zapewnia weryfikacja podpisu Stripe (EventUtility.ConstructEvent), nie token
    // antiforgery. Bez tego wyłączenia każdy webhook kończy się 500 (brak cookie antiforgery).
    if (path.StartsWithSegments("/api/payments/webhook"))
    {
        return false;
    }

    // Backdoor E2E (PR #5) — endpointy są gated env-em, antiforgery byłby martwym
    // narzutem dla Playwrighta. Endpointy nie istnieją poza env "E2E".
    if (path.StartsWithSegments("/api/_e2e"))
    {
        return false;
    }

    return true;
}

static void MapHealthEndpoints(WebApplication app)
{
    // /health/live — JEDYNY publicznie wystawiony health endpoint (Caddy przepuszcza tylko ten).
    // Liveness nie dotyka bazy. Rate-limit "HealthCheck" (30/min/IP) chroni przed floodem.
    app.MapHealthChecks(
            "/health/live",
            new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live"),
                ResponseWriter = WriteHealthCheckResponseAsync,
            })
        .AllowAnonymous()
        .RequireRateLimiting("HealthCheck");

    app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready"),
                ResponseWriter = WriteHealthCheckResponseAsync,
            })
        .AllowAnonymous()
        .DisableRateLimiting();

    // Smoke = self + database + configuration. Endpoint dla post-deploy verification.
    app.MapHealthChecks(
            "/health/smoke",
            new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("smoke"),
                ResponseWriter = WriteHealthCheckResponseAsync,
            })
        .AllowAnonymous()
        .DisableRateLimiting();

    app.MapHealthChecks(
            "/health",
            new HealthCheckOptions
            {
                Predicate = _ => true,
                ResponseWriter = WriteHealthCheckResponseAsync,
            })
        .AllowAnonymous()
        .DisableRateLimiting();
}

static async Task WriteHealthCheckResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var hostEnvironment = context.RequestServices.GetRequiredService<IHostEnvironment>();
    var showErrors = hostEnvironment.IsDevelopment() || hostEnvironment.IsEnvironment("Testing");

    var payload = new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description,
            durationMs = entry.Value.Duration.TotalMilliseconds,
            tags = entry.Value.Tags,
            error = showErrors ? entry.Value.Exception?.Message : null,
        }),
    };

    await context.Response.WriteAsync(JsonSerializer.Serialize(payload), context.RequestAborted);
}

static bool ShouldSeedDemoData(IHostEnvironment environment)
{
    if (environment.IsEnvironment("Testing"))
    {
        return false;
    }

    var enableDemoSeed =
        string.Equals(
            Environment.GetEnvironmentVariable("ENABLE_DEMO_SEED"),
            "1",
            StringComparison.Ordinal);

    return environment.IsDevelopment() || enableDemoSeed;
}

static string SanitizeIpAddress(IPAddress? ipAddress)
{
    if (ipAddress is null)
    {
        return "unknown";
    }

    if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
    {
        var bytes = ipAddress.GetAddressBytes();
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0";
    }

    if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
    {
        var bytes = ipAddress.GetAddressBytes();
        return $"{bytes[0]:x2}{bytes[1]:x2}:{bytes[2]:x2}{bytes[3]:x2}:****:****:****:****";
    }

    return "unknown";
}