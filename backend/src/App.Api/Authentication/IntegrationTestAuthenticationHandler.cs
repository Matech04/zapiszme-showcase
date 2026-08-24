using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace App.Api.Authentication;

/// <summary>
/// Zastępuje cookie Identity w środowisku Testing: buduje <see cref="ClaimsPrincipal"/> z nagłówków user id i ról
/// zgodnych z middleware tenanta.
/// </summary>
public sealed class IntegrationTestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
  public IntegrationTestAuthenticationHandler(
      IOptionsMonitor<AuthenticationSchemeOptions> options,
      ILoggerFactory logger,
      UrlEncoder encoder)
      : base(options, logger, encoder)
  {
  }

  protected override Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    if (!Request.Headers.TryGetValue(IntegrationTestAuthHeaders.UserId, out var userIdValues))
    {
      return Task.FromResult(AuthenticateResult.NoResult());
    }

    var userIdRaw = userIdValues.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(userIdRaw) || !Guid.TryParse(userIdRaw, out _))
    {
      return Task.FromResult(AuthenticateResult.NoResult());
    }

    var rolesRaw = Request.Headers[IntegrationTestAuthHeaders.Roles].FirstOrDefault() ?? "Owner";
    var claims = new List<Claim>
    {
      new(ClaimTypes.NameIdentifier, userIdRaw),
    };

    foreach (var role in rolesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      claims.Add(new Claim(ClaimTypes.Role, role));
    }

    var identity = new ClaimsIdentity(claims, Scheme.Name);
    var principal = new ClaimsPrincipal(identity);
    var ticket = new AuthenticationTicket(principal, Scheme.Name);
    return Task.FromResult(AuthenticateResult.Success(ticket));
  }
}
