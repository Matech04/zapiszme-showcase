namespace App.Domain.Aggregates.PromoCodeAggregate;

/// <summary>Grupa tenantów, dla której kod jest dostępny.</summary>
public enum PromoCodeAppliesTo
{
  NewTenantsOnly = 0,
  ExistingTenantsOnly = 1,
  Both = 2,
}
