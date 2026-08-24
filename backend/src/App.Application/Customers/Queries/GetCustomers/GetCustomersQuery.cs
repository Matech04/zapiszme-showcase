using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Customers.Dtos;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Customers.Queries.GetCustomers;

public record GetCustomersQuery : IRequest<List<CustomerDto>>;

internal class GetCustomersHandler : TenantHandler<GetCustomersQuery, List<CustomerDto>>
{
  private readonly IApplicationDbContext _context;

  // Twardy bezpiecznik na rozmiar wyniku — kontrakt (List) zostaje, ale zapytanie nie jest już
  // nieograniczone. Dla ICP (kosmetyka SOLO) 5000 klientów to z dużym zapasem realny sufit;
  // chroni pamięć/transfer przed patologicznym/abuzywnym wzrostem tabeli.
  private const int MaxCustomers = 5000;

  public GetCustomersHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<List<CustomerDto>> Handle(GetCustomersQuery request, CancellationToken ct)
  {
    return await _context.Customers
      .AsNoTracking()
      .Where(x => x.TenantId == TenantId)
      .OrderByDescending(x => x.CreatedAt)
      .Take(MaxCustomers)
      .Select(x => new CustomerDto(
        x.Id,
        x.FirstName,
        x.LastName,
        x.Email,
        x.PhoneNumber == null ? null : x.PhoneNumber.Value,
        x.CreatedAt,
        x.GeneralNotes,
        (int)x.Source,
        x.IsWhitelisted,
        x.InstagramNick))
      .ToListAsync(ct);
  }
}