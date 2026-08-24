using App.Domain.Aggregates.CustomerAggregate;

namespace App.Domain.Aggregates.CustomerAggregate;

public interface ICustomerRepository
{
  Task AddAsync(Customer customer);
  Task<Customer?> GetByIdAsync(Guid id);
  /// <summary>
  /// Szuka aktywnego klienta tenanta po numerze (porównanie po cyfrach).
  /// </summary>
  Task<Customer?> GetByPhoneNumber(Guid tenantId, PhoneNumber phoneNumber, CancellationToken ct = default);
  /// <summary>
  /// Szuka aktywnego klienta tenanta po e-mailu (porównanie po znormalizowanej formie lower).
  /// </summary>
  Task<Customer?> GetByEmail(Guid tenantId, string email, CancellationToken ct = default);
  void Update(Customer customer);
  void Remove(Customer customer);
}