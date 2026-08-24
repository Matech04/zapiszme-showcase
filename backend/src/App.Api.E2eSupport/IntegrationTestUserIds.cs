namespace App.Api.E2eSupport;

/// <summary>Stałe Identity user id używane w seedzie i nagłówkach testów integracyjnych.</summary>
public static class IntegrationTestUserIds
{
  /// <summary>Pracownik-właściciel powiązany z tenantem w seedzie panelu.</summary>
  public static readonly Guid SalonOwner = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

  /// <summary>Pracownik-właściciel powiązany z drugim tenantem w testach izolacji danych.</summary>
  public static readonly Guid SecondSalonOwner = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

  /// <summary>Użytkownik z rolą Admin (endpointy <c>Tenants</c> bez rekordu Employee).</summary>
  public static readonly Guid SystemAdmin = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
}
