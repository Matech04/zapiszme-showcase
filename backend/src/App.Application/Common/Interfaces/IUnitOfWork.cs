public interface IUnitOfWork
{
  Task<int> SaveChangesAsync(CancellationToken ct = default);

  /// <summary>
  /// Wykonuje <paramref name="action"/> w jednej transakcji (z execution-strategy/retry dla
  /// providerów relacyjnych). Dla providerów nierelacyjnych (InMemory w testach) uruchamia akcję
  /// bez transakcji. Używane, gdy operacja wymaga kilku <see cref="SaveChangesAsync"/> w jednym
  /// atomowym kroku — np. zamiana terminów, gdzie pośredni stan narusza UNIQUE na slocie.
  /// </summary>
  Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);
}
