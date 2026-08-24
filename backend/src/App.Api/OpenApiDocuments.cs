namespace App.Api;

/// <summary>Nazwy dokumentów OpenAPI / NSwag oraz grup ApiExplorer.</summary>
public static class OpenApiDocuments
{
  public const string FullApi = "v1";

  /// <summary>Tylko kontrolery z <c>Controllers/Booking</c> (klient Astro / Svelte).</summary>
  public const string BookingOnly = "v1-booking";

  public const string BookingApiGroupName = "Booking";
}
