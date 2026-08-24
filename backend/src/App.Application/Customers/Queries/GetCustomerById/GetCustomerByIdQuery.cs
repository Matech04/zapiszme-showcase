using App.Application.Common;
using App.Application.Common.Interfaces;
using App.Application.Customers.Dtos;
using App.Domain.Aggregates.CustomerAggregate;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Customers.Queries.GetCustomerById;

public record GetCustomerByIdQuery(Guid Id) : IRequest<CustomerDto>;

internal class GetCustomerByIdHandler : TenantHandler<GetCustomerByIdQuery, CustomerDto>
{
  private readonly IApplicationDbContext _context;

  public GetCustomerByIdHandler(IApplicationDbContext context, ICurrentTenantService currentTenantService)
      : base(currentTenantService)
  {
    _context = context;
  }

  public override async Task<CustomerDto> Handle(GetCustomerByIdQuery request, CancellationToken ct)
  {
    var customer = await _context.Customers
      .AsNoTracking()
      .Where(x => x.TenantId == TenantId && x.Id == request.Id)
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
      .FirstOrDefaultAsync(ct);

    return customer ?? throw new NotFoundException(nameof(Customer), request.Id);
  }
}