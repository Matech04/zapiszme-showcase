using System.Net;
using App.Api;
using App.Application.Booking;
using App.Application.Common.Interfaces;
using App.Application.Common.Security;
using App.Application.Notifications;
using App.Application.Payments.Abstractions;
using App.Infrastructure.Email;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using App.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace App.Api.E2eSupport;

/// <summary>
/// Nazwa nagłówka, którym testy integracyjne sterują "client IP" (TestServer nie wypełnia
/// Connection.RemoteIpAddress). Test-only — middleware ustawiające go jest dodawane tylko w
/// <see cref="BookingApiApplicationFactory"/>.
/// </summary>
public static class TestClientIpHeader
{
  public const string Name = "X-Test-Client-Ip";
}

/// <summary>
/// IStartupFilter (test-only) wpinający na SAMYM początku pipeline middleware, które przepisuje
/// nagłówek <see cref="TestClientIpHeader.Name"/> na Connection.RemoteIpAddress — dzięki temu
/// per-IP capy (M1/M3, per-IP SMS) są deterministycznie testowalne pod TestServerem.
/// </summary>
internal sealed class TestClientIpStartupFilter : IStartupFilter
{
  public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
  {
    app.Use(async (ctx, nextDelegate) =>
    {
      if (ctx.Request.Headers.TryGetValue(TestClientIpHeader.Name, out var ip)
          && IPAddress.TryParse(ip.ToString(), out var parsed))
      {
        ctx.Connection.RemoteIpAddress = parsed;
      }

      await nextDelegate();
    });
    next(app);
  };
}

/// <summary>
/// Host API w środowisku <c>Testing</c> (InMemory EF w <see cref="Program"/>).
/// Podmienia tylko wysyłkę e-mail OTP na implementację testową.
/// </summary>
public sealed class BookingApiApplicationFactory : WebApplicationFactory<Program>
{
  private static readonly bool UsePostgres =
    string.Equals(Environment.GetEnvironmentVariable("INTEGRATION_DB_PROVIDER"), "Postgres", StringComparison.OrdinalIgnoreCase);

  private static readonly SemaphoreSlim PostgresInitSemaphore = new(1, 1);
  private static PostgreSqlContainer? _postgresContainer;

  /// <summary>Baza-szablon: schemat migrowany RAZ na cały przebieg, potem tylko klonowany.</summary>
  private const string TemplateDatabase = "booking_saas_template";

  /// <summary>
  /// Serializuje tworzenie hosta. Konieczne, bo konfigurację przekazujemy ZMIENNYMI ŚRODOWISKOWYMI
  /// (proces-wide), a xunit domyślnie zrównolegla klasy testowe — bez tego dwie fabryki potrafiłyby
  /// sobie nadpisać connection string między ustawieniem a odczytem przez CreateBuilder.
  /// </summary>
  private static readonly object HostCreationGate = new();

  /// <summary>
  /// Własna baza tej fabryki. Odpowiednik unikatowej nazwy bazy InMemory (`AppApiTests_{guid}`)
  /// z Program.cs — bez tego 576 testów dzieliłoby jedną bazę i wchodziło sobie w dane.
  /// </summary>
  private readonly string _databaseName = "t_" + Guid.NewGuid().ToString("N");

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");
    if (UsePostgres)
    {
      EnsurePostgresContainerStarted();

      // ZMIENNE ŚRODOWISKOWE, nie ConfigureAppConfiguration — i to nie jest kwestia gustu.
      //
      // `Program.cs` wybiera providera EF w kodzie top-level, czytając `Testing:UsePostgres`
      // z `builder.Configuration` JESZCZE PRZED `builder.Build()`. Delegaty rejestrowane przez
      // WebApplicationFactory (`ConfigureAppConfiguration`) wykonują się dopiero W TRAKCIE
      // `Build()`, czyli o krok za późno. Skutek był taki, że `INTEGRATION_DB_PROVIDER=Postgres`
      // podnosiło kontener Testcontainers, po czym aplikacja i tak jechała na InMemory —
      // kontener marnował się w tle, a suita (łącznie z krokiem „PostgreSQL" w CI) NIGDY nie
      // dotknęła prawdziwej bazy. Diagnozę mylił dodatkowo `Assert.SkipUnless(UsePostgres, …)`,
      // bo czyta zmienną środowiskową, a nie realnego providera — testy „postgresowe" ruszały
      // i przechodziły na InMemory.
      //
      // Zmienne środowiskowe czyta `WebApplication.CreateBuilder` od razu, przez domyślny
      // `AddEnvironmentVariables()`, więc trafiają na czas. Podwójny podkreślnik to separator
      // sekcji w konfiguracji .NET.
      // Zostawione świadomie: dla hostów, które czytają konfigurację dopiero po `Build()`,
      // to nadal poprawna droga. Kosztuje nic, a nie zakłada jednej ścieżki startu.
      builder.ConfigureAppConfiguration((_, configBuilder) =>
      {
        configBuilder.AddInMemoryCollection(
          new Dictionary<string, string?>
          {
            ["Testing:UsePostgres"] = "true",
            ["ConnectionStrings:DefaultConnection"] = ConnectionStringFor(_databaseName),
          });
      });
    }


    builder.ConfigureTestServices(services =>
    {
      // Test-only: pozwala testom ustawić "client IP" przez nagłówek (TestServer nie ma realnego
      // RemoteIpAddress). Wymagane do deterministycznego testowania per-IP capów (M1/M3).
      services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();

      foreach (var d in services.Where(x => x.ServiceType == typeof(IBookingOtpEmailSender)).ToList())
      {
        services.Remove(d);
      }

      foreach (var d in services.Where(x => x.ServiceType == typeof(IAuthEmailSender)).ToList())
      {
        services.Remove(d);
      }

      foreach (var d in services.Where(x => x.ServiceType == typeof(IEmailTransport)).ToList())
      {
        services.Remove(d);
      }

      foreach (var d in services.Where(x => x.ServiceType == typeof(IRealtimeNotifier)).ToList())
      {
        services.Remove(d);
      }

      foreach (var d in services.Where(x => x.ServiceType == typeof(IPhoneOtpSender)).ToList())
      {
        services.Remove(d);
      }

      foreach (var d in services.Where(x => x.ServiceType == typeof(IPaymentProvider)).ToList())
      {
        services.Remove(d);
      }

      services.AddSingleton<FakePaymentProvider>();
      services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<FakePaymentProvider>());

      // Storage: realny IFileStorage konstruuje AmazonS3Client, który bez ServiceURL/region rzuca
      // w środowisku testowym. Podmieniamy na fake (zero I/O), by ścieżki dotykające storage
      // (np. UpdateService — best-effort kasowanie osieroconych obrazów) działały w testach.
      foreach (var d in services.Where(x => x.ServiceType == typeof(IFileStorage)).ToList())
      {
        services.Remove(d);
      }
      services.AddSingleton<IFileStorage, FakeFileStorage>();

      services.AddSingleton<TestPhoneOtpMailbox>();
      services.AddSingleton<IPhoneOtpSender>(sp => sp.GetRequiredService<TestPhoneOtpMailbox>());
      services.AddSingleton<TestBookingOtpMailbox>();
      services.AddSingleton<IBookingOtpEmailSender>(sp => sp.GetRequiredService<TestBookingOtpMailbox>());
      services.AddSingleton<TestAuthEmailMailbox>();
      services.AddSingleton<IAuthEmailSender>(sp => sp.GetRequiredService<TestAuthEmailMailbox>());
      services.AddSingleton<TestEmailTransport>();
      services.AddSingleton<IEmailTransport>(sp => sp.GetRequiredService<TestEmailTransport>());
      services.AddSingleton<TestRealtimeNotifier>();
      services.AddSingleton<IRealtimeNotifier>(sp => sp.GetRequiredService<TestRealtimeNotifier>());
    });
  }

  private static void EnsurePostgresContainerStarted()
  {
    if (_postgresContainer is not null)
    {
      return;
    }

    PostgresInitSemaphore.Wait();
    try
    {
      if (_postgresContainer is not null)
      {
        return;
      }

      var container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("booking_saas_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        // Domyślne max_connections=100 nie wystarcza: każdy test dostaje własny host z własną pulą,
        // a xunit trzyma kilka klas naraz. Bez tego sypie się „53300: sorry, too many clients".
        // fsync/synchronous_commit off — baza testowa jest jednorazowa, trwałość nas nie obchodzi,
        // a bez tego klonowanie szablonu i seed kosztują niepotrzebne I/O.
        .WithCommand(
          "-c", "max_connections=400",
          "-c", "fsync=off",
          "-c", "synchronous_commit=off",
          "-c", "full_page_writes=off")
        .Build();

      // Publikuj statyczne pole DOPIERO po Running — inaczej fast-path check (poza semaforem)
      // przepuszcza równoległe wątki xunit do GetConnectionString() na niewystartowanym kontenerze.
      container.StartAsync().GetAwaiter().GetResult();
      CreateTemplateDatabase(container);
      _postgresContainer = container;
    }
    finally
    {
      PostgresInitSemaphore.Release();
    }
  }

  /// <summary>
  /// Jedyny moment, w którym da się wstrzyknąć konfigurację tak, żeby Program.cs ją zobaczył:
  /// zmienne środowiskowe ustawiamy TUŻ PRZED zbudowaniem hosta, bo to `base.CreateHost` uruchamia
  /// kod top-level `Program.cs` (a ten czyta `Testing:UsePostgres` przed `builder.Build()`).
  ///
  /// Całość pod zamkiem: zmienne środowiskowe są wspólne dla procesu, a xunit zrównolegla klasy,
  /// więc bez serializacji dwie fabryki nadpisałyby sobie connection string między zapisem
  /// a odczytem. Zamek trzymamy wyłącznie na czas budowy hosta — same testy biegną równolegle.
  /// </summary>
  protected override IHost CreateHost(IHostBuilder builder)
  {
    if (!UsePostgres)
    {
      return base.CreateHost(builder);
    }

    lock (HostCreationGate)
    {
      CreateDatabaseFromTemplate(_databaseName);
      Environment.SetEnvironmentVariable("Testing__UsePostgres", "true");
      Environment.SetEnvironmentVariable(
        "ConnectionStrings__DefaultConnection", ConnectionStringFor(_databaseName));

      return base.CreateHost(builder);
    }
  }

  /// <summary>
  /// Sprząta bazę tej fabryki. Bez tego przebieg zostawia ~576 baz i garść otwartych połączeń,
  /// co przy współdzielonym kontenerze kończy się „too many clients" w drugiej połowie suity.
  /// Best-effort: błąd sprzątania nie może wywrócić testu, który już przeszedł.
  /// </summary>
  protected override void Dispose(bool disposing)
  {
    base.Dispose(disposing);

    if (!disposing || !UsePostgres || _postgresContainer is null)
    {
      return;
    }

    try
    {
      NpgsqlConnection.ClearAllPools();
      ExecuteNonQuery(
        _postgresContainer.GetConnectionString(),
        $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE);");
    }
    catch
    {
      // Kontener i tak zniknie po przebiegu (Ryuk) — nie warto psuć wyniku testu.
    }
  }

  /// <summary>
  /// Buduje bazę-szablon i migruje ją RAZ. W środowisku <c>Testing</c> Program.cs świadomie pomija
  /// <c>MigrateAsync</c> (InMemory schematu nie potrzebuje), więc na Postgresie nikt by go nie
  /// utworzył — stąd 532 testy padające na „relation AspNetRoles does not exist".
  /// Klonowanie <c>TEMPLATE</c> jest w Postgresie kopiowaniem plików, więc per test kosztuje
  /// milisekundy zamiast przebiegu 70+ migracji.
  /// </summary>
  private static void CreateTemplateDatabase(PostgreSqlContainer container)
  {
    var adminConnection = container.GetConnectionString();
    ExecuteNonQuery(adminConnection, $"DROP DATABASE IF EXISTS \"{TemplateDatabase}\";");
    ExecuteNonQuery(adminConnection, $"CREATE DATABASE \"{TemplateDatabase}\";");

    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
      .UseNpgsql(Unpooled(ReplaceDatabase(adminConnection, TemplateDatabase)))
      .Options;

    using (var db = new ApplicationDbContext(options, new NoTenantService()))
    {
      db.Database.Migrate();
    }

    // KLUCZOWE: `CREATE DATABASE ... TEMPLATE x` wymaga, żeby do `x` NIKT nie był podłączony.
    // Pula Npgsql trzyma połączenie żywe po Dispose kontekstu, więc bez tego pierwszy klon
    // wywala się na „55006: source database is being accessed by other users".
    NpgsqlConnection.ClearAllPools();
  }

  /// <summary>Klon szablonu — świeża, pusta-ale-zmigrowana baza dla jednej fabryki.</summary>
  private static void CreateDatabaseFromTemplate(string databaseName)
  {
    var adminConnection = _postgresContainer!.GetConnectionString();
    ExecuteNonQuery(
      adminConnection,
      $"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{TemplateDatabase}\";");
  }

  /// <summary>
  /// Connection string testowego hosta. Pula ograniczona do 4: hostów jest tyle, co testów,
  /// a każdy z domyślną pulą (100) wyczerpałby `max_connections` po kilkunastu klasach.
  /// </summary>
  private static string ConnectionStringFor(string databaseName) =>
    new NpgsqlConnectionStringBuilder(_postgresContainer!.GetConnectionString())
    {
      Database = databaseName,
      MaxPoolSize = 4,
      MinPoolSize = 0,
      // Bez tego Npgsql redaguje DETAIL („may contain sensitive data") i naruszenie więzu mówi
      // tylko JAKI indeks, a nie JAKIE wartości. W bazie testowej nie ma czego chronić, a różnica
      // w diagnostyce jest ogromna.
      IncludeErrorDetail = true,
    }.ConnectionString;

  private static string ReplaceDatabase(string connectionString, string databaseName) =>
    new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName }.ConnectionString;

  /// <summary>Bez puli — dla połączeń administracyjnych, które muszą się realnie zamknąć.</summary>
  private static string Unpooled(string connectionString) =>
    new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;

  private static void ExecuteNonQuery(string connectionString, string sql)
  {
    using var connection = new NpgsqlConnection(Unpooled(connectionString));
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
  }

  /// <summary>Migracje nie dotykają filtrów tenanta, więc szablonowi wystarczy pusty resolver.</summary>
  private sealed class NoTenantService : ICurrentTenantService
  {
    public Guid? TenantId { get; set; }
  }
}
