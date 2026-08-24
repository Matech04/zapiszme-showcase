using App.Api.Authentication;

namespace App.Api.E2eSupport;

public static class AuthenticatedTestClient
{
  public static HttpClient CreateOwnerClient(this BookingApiApplicationFactory factory)
  {
    return factory.CreateAuthenticatedClient(IntegrationTestUserIds.SalonOwner, "Owner");
  }

  public static HttpClient CreateAdminClient(this BookingApiApplicationFactory factory)
  {
    return factory.CreateAuthenticatedClient(IntegrationTestUserIds.SystemAdmin, "Admin");
  }

  public static HttpClient CreateManagerClient(this BookingApiApplicationFactory factory)
  {
    return factory.CreateAuthenticatedClient(IntegrationTestUserIds.SalonOwner, "Manager");
  }

  public static HttpClient CreateEmployeeClient(this BookingApiApplicationFactory factory)
  {
    return factory.CreateAuthenticatedClient(IntegrationTestUserIds.SalonOwner, "Employee");
  }

  /// <summary>
  /// Klient „Recepcja" (kiosk) scoped do salonu seeda. Używa tego samego user id co owner (żeby
  /// tenant się rozwiązał przez powiązanego pracownika), ale z rolą Kiosk — symuluje sesję recepcji.
  /// </summary>
  public static HttpClient CreateKioskClient(this BookingApiApplicationFactory factory)
  {
    return factory.CreateAuthenticatedClient(IntegrationTestUserIds.SalonOwner, "Kiosk");
  }

  private static HttpClient CreateAuthenticatedClient(
      this BookingApiApplicationFactory factory,
      Guid userId,
      string roles)
  {
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.TryAddWithoutValidation(
        IntegrationTestAuthHeaders.UserId,
        userId.ToString());
    client.DefaultRequestHeaders.TryAddWithoutValidation(IntegrationTestAuthHeaders.Roles, roles);
    return client;
  }
}
