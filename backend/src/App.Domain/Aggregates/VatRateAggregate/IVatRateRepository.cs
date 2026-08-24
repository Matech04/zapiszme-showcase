namespace App.Domain.Aggregates.VatRateAggregate;

public interface IVatRateRepository
{
  Task AddAsync(VatRate vatRate);
  Task<VatRate?> GetByIdAsync(Guid id);
  void Update(VatRate vatRate);
  void Remove(VatRate vatRate);

  /// <summary>
  /// Wyłącza <see cref="VatRate.IsDefault"/> u wszystkich stawek tenanta oprócz wskazanej (np. przed ustawieniem nowej domyślnej).
  /// </summary>
  Task ClearDefaultForTenantExceptAsync(Guid tenantId, Guid exceptVatRateId, CancellationToken ct = default);
}