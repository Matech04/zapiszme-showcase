using App.Api.E2eSupport;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// Strażnik providera bazy: gdy proszono o Postgresa, host MUSI faktycznie na nim jechać.
///
/// Powód powstania: <c>INTEGRATION_DB_PROVIDER=Postgres</c> przez długi czas podnosił kontener
/// Testcontainers, ale aplikacja i tak używała InMemory — `Program.cs` czytał `Testing:UsePostgres`
/// przed `builder.Build()`, a `ConfigureAppConfiguration` fabryki wykonywało się dopiero w trakcie.
/// Awaria była CICHA: kontener wstawał, suita świeciła na zielono, krok „PostgreSQL" w CI raportował
/// sukces — a prawdziwej bazy nie dotykał nikt. To wprost przeczy zasadzie projektu („Don't mock
/// the database in App.Api.IntegrationTests"), która powstała po incydencie, gdy zamockowane testy
/// przeszły, a realna migracja położyła produkcję.
///
/// Dlaczego nie wystarczy <c>Assert.SkipUnless(UsePostgres, …)</c> rozsiane po testach: ono czyta
/// ZMIENNĄ ŚRODOWISKOWĄ, czyli deklarację intencji, a nie stan faktyczny. Ten test porównuje
/// intencję z realnym <c>ProviderName</c> i tylko dlatego łapie rozjazd.
/// </summary>
public sealed class DatabaseProviderGuardTests
{
  private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
  private const string InMemoryProvider = "Microsoft.EntityFrameworkCore.InMemory";

  [Fact]
  public void Provider_bazy_odpowiada_temu_o_co_poproszono()
  {
    var poproszonoOPostgresa = string.Equals(
      Environment.GetEnvironmentVariable("INTEGRATION_DB_PROVIDER"),
      "Postgres",
      StringComparison.OrdinalIgnoreCase);

    using var factory = new BookingApiApplicationFactory();
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var provider = db.Database.ProviderName;

    if (poproszonoOPostgresa)
    {
      Assert.True(
        provider == NpgsqlProvider,
        $"INTEGRATION_DB_PROVIDER=Postgres, ale EF używa '{provider}'. Kontener Testcontainers "
        + "prawdopodobnie wstał i marnuje się, a testy jadą na InMemory — czyli suita NIE dotyka "
        + "prawdziwej bazy, mimo że tak raportuje. Sprawdź kolejność: Program.cs czyta "
        + "Testing:UsePostgres przed builder.Build(), więc konfiguracja musi trafić do procesu "
        + "ZMIENNYMI ŚRODOWISKOWYMI (patrz BookingApiApplicationFactory), nie przez "
        + "ConfigureAppConfiguration.");
    }
    else
    {
      // Tryb domyślny (szybki, bez Dockera) — pilnujemy, że nikt nie wpiął Postgresa przypadkiem,
      // bo wtedy „szybka" suita zaczęłaby po cichu wymagać działającego demona Dockera.
      Assert.True(
        provider == InMemoryProvider,
        $"Bez INTEGRATION_DB_PROVIDER oczekujemy InMemory, a EF używa '{provider}'.");
    }
  }
}
