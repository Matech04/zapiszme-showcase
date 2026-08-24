using App.Application.Common.Interfaces;
using App.Domain.Aggregates.CustomerAggregate;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Repositories;

public class CustomerRepository : ICustomerRepository
{
  private readonly IApplicationDbContext _context;

  public CustomerRepository(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task AddAsync(Customer customer) => await _context.Customers.AddAsync(customer);
  // FirstOrDefaultAsync (nie FindAsync) — globalny query filter tenanta (+IsActive) działa jako
  // automatyczny backstop izolacji zamiast polegać wyłącznie na ręcznym checku w handlerze.
  public async Task<Customer?> GetByIdAsync(Guid id) =>
    await _context.Customers.FirstOrDefaultAsync(c => c.Id == id);


  public async Task<Customer?> GetByPhoneNumber(Guid tenantId, PhoneNumber phoneNumber, CancellationToken ct = default)
  {


    var customer = await _context.Customers
      .Where(c => c.TenantId == tenantId && c.PhoneNumber == phoneNumber)
      .FirstOrDefaultAsync(ct);

    return customer;
  }

  public async Task<Customer?> GetByEmail(Guid tenantId, string email, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(email)) return null;
    // Email jest zapisywany już znormalizowany do lowercase (Guard.NormalizeEmail), więc porównujemy
    // wprost — bez LOWER(email) po stronie SQL, który defeatował indeks (TenantId, Email).
    var normalized = email.Trim().ToLowerInvariant();
    var customer = await _context.Customers
      .Where(c => c.TenantId == tenantId && c.Email != string.Empty && c.Email == normalized)
      .FirstOrDefaultAsync(ct);
    return customer;
  }

  public void Update(Customer customer) => _context.Customers.Update(customer);
  public void Remove(Customer customer) => _context.Customers.Remove(customer);
}