using App.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace App.Api.IntegrationTests;

/// <summary>
/// Czysty unit test polityki cookies cross-site. Pilnuje, że w prawdziwej produkcji cookie
/// anon-session (i każde inne objęte tą polityką) dostaje SameSite=None+Secure — bez tego
/// cross-site fetch z booking-web do api NIE wysyła cookie i anti-squatting przestaje działać.
///
/// Gałąź produkcyjna jest nieosiągalna z testów integracyjnych (te biegną w env "Testing"),
/// dlatego całą matrycę środowisk weryfikujemy tutaj.
/// </summary>
public sealed class CrossOriginCookiePolicyTests
{
  [Theory]
  [InlineData("Development")]
  [InlineData("Testing")]
  public void Dev_and_testing_stay_on_lax_so_http_localhost_is_not_rejected(string environmentName)
  {
    var environment = new FakeHostEnvironment { EnvironmentName = environmentName };

    Assert.False(CrossOriginCookiePolicy.RequiresCrossSite(environment));

    var options = new CookieOptions();
    CrossOriginCookiePolicy.Apply(options, environment, requestIsHttps: false);

    Assert.Equal(SameSiteMode.Lax, options.SameSite);
    Assert.False(options.Secure);
  }

  [Theory]
  [InlineData("Production")]
  [InlineData("Staging")]
  [InlineData("LocalProd")]
  public void Non_dev_environments_get_none_plus_secure_for_cross_site_fetch(string environmentName)
  {
    var environment = new FakeHostEnvironment { EnvironmentName = environmentName };

    Assert.True(CrossOriginCookiePolicy.RequiresCrossSite(environment));

    var options = new CookieOptions();
    // requestIsHttps=false celowo — w prod gałąź NIE zależy od żądania, Secure ma być zawsze.
    CrossOriginCookiePolicy.Apply(options, environment, requestIsHttps: false);

    Assert.Equal(SameSiteMode.None, options.SameSite);
    Assert.True(options.Secure);
  }

  [Theory]
  [InlineData("Development")]
  [InlineData("Testing")]
  [InlineData("Production")]
  [InlineData("Staging")]
  [InlineData("LocalProd")]
  public void None_is_never_emitted_without_secure(string environmentName)
  {
    // SameSite=None bez Secure jest odrzucane przez wszystkie współczesne przeglądarki —
    // ta kombinacja nie może wyjść z Apply w ŻADNYM środowisku.
    var environment = new FakeHostEnvironment { EnvironmentName = environmentName };

    var options = new CookieOptions();
    CrossOriginCookiePolicy.Apply(options, environment, requestIsHttps: false);

    if (options.SameSite == SameSiteMode.None)
    {
      Assert.True(options.Secure);
    }
  }

  [Fact]
  public void Dev_branch_keeps_secure_in_sync_with_request_scheme()
  {
    // W Dev jadącym akurat po HTTPS cookie powinno dostać Secure — żeby nie degradować
    // bezpieczeństwa, gdy transport i tak jest szyfrowany.
    var environment = new FakeHostEnvironment { EnvironmentName = "Development" };

    var httpOptions = new CookieOptions();
    CrossOriginCookiePolicy.Apply(httpOptions, environment, requestIsHttps: false);
    Assert.False(httpOptions.Secure);

    var httpsOptions = new CookieOptions();
    CrossOriginCookiePolicy.Apply(httpsOptions, environment, requestIsHttps: true);
    Assert.True(httpsOptions.Secure);
  }

  [Fact]
  public void Apply_guards_against_null_arguments()
  {
    var environment = new FakeHostEnvironment { EnvironmentName = "Production" };

    Assert.Throws<ArgumentNullException>(
      () => CrossOriginCookiePolicy.Apply(null!, environment, requestIsHttps: true));
    Assert.Throws<ArgumentNullException>(
      () => CrossOriginCookiePolicy.Apply(new CookieOptions(), null!, requestIsHttps: true));
    Assert.Throws<ArgumentNullException>(
      () => CrossOriginCookiePolicy.RequiresCrossSite(null!));
  }

  private sealed class FakeHostEnvironment : IHostEnvironment
  {
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "App.Api";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
  }
}
