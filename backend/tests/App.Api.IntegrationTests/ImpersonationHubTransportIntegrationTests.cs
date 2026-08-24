using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using App.Api.E2eSupport;
using App.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace App.Api.IntegrationTests;

/// <summary>
/// Dzwonek musi działać także w sesji wsparcia TYLKO DO ODCZYTU.
///
/// Powód powstania: `ImpersonationMiddleware` blokował wszystko, co nie jest GET/HEAD/OPTIONS/TRACE,
/// a handshake SignalR (`POST /hubs/*/negotiate`) jest POST-em z definicji protokołu. Efekt zmierzony
/// w przeglądarce na stacku prod-local: 403 `impersonation.read_only`, trzy błędy w konsoli i zejście
/// dzwonka na odpytywanie REST-em przy KAŻDYM wejściu w tryb wsparcia. Co gorsza, martwym kodem
/// stawała się gałąź w <see cref="NotificationHub"/>, która jawnie dopisuje sesję wsparcia do grupy
/// salonu — funkcja była napisana, ale nieosiągalna.
/// </summary>
public sealed class ImpersonationHubTransportIntegrationTests
{
  [Fact]
  public async Task Sesja_tylko_do_odczytu_moze_nawiazac_polaczenie_dzwonka()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var adminClient = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    await adminClient.PostAsJsonAsync(
      "/api/admin/impersonation",
      new { tenantId = seed.TenantId, reason = "Podgląd dzwonka", readOnly = true },
      ct);

    var negotiate = await adminClient.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null, ct);

    Assert.NotEqual(HttpStatusCode.Forbidden, negotiate.StatusCode);
    Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);
  }

  /// <summary>
  /// Kontrola drugiej strony wyjątku: zapis biznesowy w trybie tylko-do-odczytu MA dalej padać.
  /// Bez tego łatwo byłoby „naprawić" dzwonek, rozszczelniając przy okazji całą blokadę.
  /// </summary>
  [Fact]
  public async Task Wyjatek_dla_hubow_nie_odblokowuje_zwyklych_mutacji()
  {
    using var factory = new BookingApiApplicationFactory();
    _ = factory.Services;
    var seed = RestApiIntegrationSeed.Seed(factory.Services);
    var adminClient = factory.CreateAdminClient();
    var ct = TestContext.Current.CancellationToken;

    await adminClient.PostAsJsonAsync(
      "/api/admin/impersonation",
      new { tenantId = seed.TenantId, reason = "Podgląd dzwonka", readOnly = true },
      ct);

    var mutate = await adminClient.PostAsJsonAsync(
      $"/api/Employees/{seed.EmployeeId}/leaves",
      new { startDate = TestDates.IsoInDays(30), endDate = TestDates.IsoInDays(31) },
      ct);

    Assert.Equal(HttpStatusCode.Forbidden, mutate.StatusCode);
  }

  /// <summary>
  /// PRZESŁANKA wyjątku w `ImpersonationMiddleware`: huby są jednokierunkowe (serwer → klient),
  /// więc przez połączenie realtime nie da się nic zapisać. Gdy ktoś doda metodę wołaną z klienta,
  /// ten test zapali się na czerwono — i wtedy trzeba przemyśleć wyjątek, a nie ten test poprawić.
  /// </summary>
  [Fact]
  public void Huby_nie_wystawiaja_metod_wolanych_z_klienta()
  {
    var huby = typeof(NotificationHub).Assembly
      .GetTypes()
      .Where(t => !t.IsAbstract && typeof(Hub).IsAssignableFrom(t))
      .ToList();

    Assert.NotEmpty(huby);

    foreach (var hub in huby)
    {
      var wolalneZKlienta = hub
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        // Metody cyklu życia (OnConnectedAsync / OnDisconnectedAsync) to NADPISANIA bazowego Hub-a,
        // wołane przez framework — ich `GetBaseDefinition()` wskazuje na `Hub`. Metoda wołana
        // z klienta jest zadeklarowana w samym hubie, więc jest własną definicją bazową.
        .Where(m => m.GetBaseDefinition().DeclaringType == m.DeclaringType)
        .Where(m => !m.IsSpecialName)
        .Select(m => $"{hub.Name}.{m.Name}")
        .ToList();

      Assert.True(
        wolalneZKlienta.Count == 0,
        "Hub wystawia metodę wołaną z klienta, co unieważnia wyjątek dla /hubs w "
          + "ImpersonationMiddleware (sesja tylko-do-odczytu mogłaby przez nią zapisywać): "
          + string.Join(", ", wolalneZKlienta));
    }
  }
}
