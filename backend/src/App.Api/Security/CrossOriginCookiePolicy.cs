namespace App.Api.Security;

/// <summary>
/// Wspólna polityka SameSite/Secure dla cookies, które muszą jechać cross-site.
///
/// booking-web → api oraz admin → api to różne *site*: w prod admin.zapisz.me ≠ api.zapisz.me,
/// a lokalnie `localhost` jest na Public Suffix List, więc api.localhost ≠ localhost też są
/// cross-site. Cross-site <c>fetch</c> z <c>credentials: 'include'</c> dołącza cookie TYLKO
/// gdy ma <c>SameSite=None; Secure</c>. <c>SameSite=Lax</c> jedzie wyłącznie przy top-level
/// nawigacji — cookie z Lax po prostu nie dotrze do API w wywołaniu XHR/fetch.
///
/// Dev (`astro dev` / `ng serve` po HTTP) i Testing (xUnit TestServer po HTTP) nie mogą użyć
/// <c>Secure</c> (przeglądarka odrzuca Secure cookie na http://), więc tam świadomie schodzimy
/// do <c>Lax</c> — i tak wszystko jest tam same-site, więc Lax wystarcza.
/// </summary>
public static class CrossOriginCookiePolicy
{
    /// <summary>
    /// <c>true</c> dla każdego środowiska poza Development i Testing — czyli wszędzie tam,
    /// gdzie frontend i API stoją za HTTPS (Production oraz LOCAL_PROD, który jedzie jako
    /// Production env za Caddy <c>tls internal</c>).
    /// </summary>
    public static bool RequiresCrossSite(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return !environment.IsDevelopment()
            && !environment.IsEnvironment("Testing")
            && !environment.IsEnvironment("E2E");
    }

    /// <summary>
    /// Ustawia <see cref="CookieOptions.SameSite"/> i <see cref="CookieOptions.Secure"/> tak,
    /// żeby cookie działało w cross-site fetch na prod, a w Dev/Testing po HTTP nie zostało
    /// odrzucone przez przeglądarkę.
    /// </summary>
    /// <param name="options">Cookie do skonfigurowania — mutowane w miejscu.</param>
    /// <param name="environment">Środowisko hosta.</param>
    /// <param name="requestIsHttps">Czy bieżące żądanie przyszło po HTTPS (gałąź Dev/Testing).</param>
    public static void Apply(CookieOptions options, IHostEnvironment environment, bool requestIsHttps)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (RequiresCrossSite(environment))
        {
            // SameSite=None WYMAGA Secure — bez tego przeglądarka odrzuca całe cookie.
            options.SameSite = SameSiteMode.None;
            options.Secure = true;
        }
        else
        {
            options.SameSite = SameSiteMode.Lax;
            options.Secure = requestIsHttps;
        }
    }
}
