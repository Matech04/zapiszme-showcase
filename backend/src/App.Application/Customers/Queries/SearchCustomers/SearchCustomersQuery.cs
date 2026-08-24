using App.Application.Common;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Customers.Queries.SearchCustomersQuery;

public record SearchCustomersQuery(string searchTerm) : IRequest<List<SearchCustomerDto>>;

internal class SearchCustomersHandler : TenantHandler<SearchCustomersQuery, List<SearchCustomerDto>>
{
  private readonly IApplicationDbContext _context;

  public SearchCustomersHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<List<SearchCustomerDto>> Handle(SearchCustomersQuery request, CancellationToken ct)
  {

    if (string.IsNullOrWhiteSpace(request.searchTerm)) return new List<SearchCustomerDto>();

    var term = request.searchTerm.Trim();
    // Frazę telefonu sprowadzamy do samych cyfr i szukamy po PhoneNumberSearch (zwykły string),
    // bo LIKE po kolumnie PhoneNumber z value converterem nie tłumaczy się na SQL. Gdy fraza nie
    // zawiera cyfr, pomijamy warunek telefonu — inaczej Contains("") dopasowałby każdego klienta.
    var phoneDigits = PhoneNumber.DigitsOnly(term);

    var query = _context.Customers
      .AsNoTracking()
      .Where(x => x.TenantId == TenantId);

    query = phoneDigits.Length > 0
      ? query.Where(x => x.FirstName.Contains(term)
                      || x.LastName.Contains(term)
                      || (x.PhoneNumberSearch != null && x.PhoneNumberSearch.Contains(phoneDigits)))
      : query.Where(x => x.FirstName.Contains(term)
                      || x.LastName.Contains(term));

    var searchResult = await query
      // OrderBy MUSI poprzedzać Take: bez niego przy >10 trafieniach Postgres zwraca dowolną
      // dziesiątkę i ta sama fraza potrafi dać inne wyniki przy kolejnym wpisaniu.
      .OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ThenBy(x => x.Id)
      .Take(10)
      .Select(x => new SearchCustomerDto(x.FirstName, x.LastName, x.PhoneNumber == null ? null : x.PhoneNumber.Value, x.Id))
      .ToListAsync(ct);

    return searchResult;
  }
}