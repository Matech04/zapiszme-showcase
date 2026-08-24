using System.ComponentModel.DataAnnotations;

namespace App.Api.Options;

public sealed class TurnstileOptions
{
  public const string SectionName = "Turnstile";

  public bool Enabled { get; set; }

  /// <summary>Site-key Managed widget — używany w login / register / forgot-password (dashboard).</summary>
  public string? SiteKey { get; set; }

  /// <summary>Secret pairing z <see cref="SiteKey"/> (Managed widget).</summary>
  public string? SecretKey { get; set; }

  /// <summary>
  /// Site-key Invisible widget — używany w publicznym booking-flow (Astro web).
  /// Inny widget = inny secret. CF weryfikator paruje token z konkretnym secretem.
  /// </summary>
  public string? InvisibleSiteKey { get; set; }

  /// <summary>Secret pairing z <see cref="InvisibleSiteKey"/> (Invisible widget).</summary>
  public string? InvisibleSecretKey { get; set; }

  [Required, Url]
  public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
}
