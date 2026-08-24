using App.Application.Common.Interfaces;
using App.Domain.Aggregates.VatRateAggregate;
using Microsoft.EntityFrameworkCore;

public class VatRateRepository : IVatRateRepository
{
  IApplicationDbContext _context;

  public VatRateRepository(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task AddAsync(VatRate vatRate) => await _context.VatRates.AddAsync(vatRate);
  // FirstOrDefaultAsync (nie FindAsync) — globalny query filter tenanta jako backstop izolacji.
  public async Task<VatRate?> GetByIdAsync(Guid id) =>
    await _context.VatRates.FirstOrDefaultAsync(v => v.Id == id);
  public void Update(VatRate vatRate) => _context.VatRates.Update(vatRate);
  public void Remove(VatRate vatRate) => vatRate.Deactivate();

  public async Task ClearDefaultForTenantExceptAsync(Guid tenantId, Guid exceptVatRateId, CancellationToken ct = default)
  {
    var others = await _context.VatRates
      .Where(v => v.TenantId == tenantId && v.IsDefault && v.Id != exceptVatRateId)
      .ToListAsync(ct);
    foreach (var v in others)
    {
      v.UnDefault();
    }
  }
}