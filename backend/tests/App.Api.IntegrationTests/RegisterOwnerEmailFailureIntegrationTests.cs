using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Api;
using App.Api.E2eSupport;
using App.Infrastructure.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace App.Api.IntegrationTests;

/// <summary>
/// REG-EMAIL-001 — Rejestracja musi zakończyć się 200 nawet gdy mailer rzuca wyjątkiem.
///
/// Tło: SendConfirmEmailAsync idzie PO commit-cie tenant/employee/VAT. Jeśli zewnętrzny
/// mailer (Azure Communication Services) padnie, kod NIE może propagować wyjątku, bo
/// klient zobaczyłby 500 mimo że konto już istnieje (false-negative). Catch + log to fix.
/// </summary>
public sealed class RegisterOwnerEmailFailureIntegrationTests
{
  private static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

  [Fact]
  public async Task Register_owner_returns_200_when_email_sender_throws()
  {
    // Nadbudowa na bazowej fabryce, a NIE własny WebApplicationFactory<Program>.
    // Własna fabryka nie tworzy sobie bazy testowej, więc na Postgresie dziedziczyła connection
    // string z procesowej zmiennej środowiskowej — czyli bazę OSTATNIEJ `BookingApiApplicationFactory`,
    // która akurat zdążyła ją ustawić. Gdy tamta fabryka kończyła i robiła `DROP DATABASE`,
    // ten test tracił bazę pod nogami i dostawał 500 zamiast 200 („3D000: database does not exist”).
    // Na InMemory problem nie istniał, więc ujawnił się dopiero po włączeniu realnego Postgresa.
    using var baseFactory = new BookingApiApplicationFactory();
    using var factory = baseFactory.WithWebHostBuilder(builder =>
      builder.ConfigureTestServices(services =>
      {
        foreach (var d in services.Where(x => x.ServiceType == typeof(IAuthEmailSender)).ToList())
        {
          services.Remove(d);
        }
        services.AddSingleton<IAuthEmailSender, ThrowingAuthEmailSender>();
      }));
    var client = factory.CreateClient();
    var ct = TestContext.Current.CancellationToken;

    var response = await client.PostAsJsonAsync(
      "/api/auth/register-owner",
      new
      {
        email = "owner@mailer-failure.local",
        password = "Password123!",
        phoneNumber = "+48501111030",
      },
      ct);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<RegisterOwnerResponseBody>(JsonRead, ct);
    Assert.NotNull(body);
    Assert.NotEqual(Guid.Empty, body!.UserId);
    Assert.False(body.EmailConfirmed);
    Assert.Equal("owner@mailer-failure.local", body.Email);
  }

  private sealed record RegisterOwnerResponseBody(Guid UserId, string Email, bool EmailConfirmed);

  private sealed class ThrowingAuthEmailSender : IAuthEmailSender
  {
    public Task SendConfirmEmailAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default) =>
      throw new InvalidOperationException("Symulowany błąd ACS — mailer down.");

    public Task SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken cancellationToken = default) =>
      throw new InvalidOperationException("Symulowany błąd ACS — mailer down.");

    public Task SendEmployeeInviteAsync(string toEmail, string inviteUrl, CancellationToken cancellationToken = default) =>
      throw new InvalidOperationException("Symulowany błąd ACS — mailer down.");

    public Task SendChangeEmailConfirmationAsync(string toEmail, string confirmUrl, CancellationToken cancellationToken = default) =>
      throw new InvalidOperationException("Symulowany błąd ACS — mailer down.");
  }
}
