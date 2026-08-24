using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using MediatR;

namespace App.Application.Common;

public abstract class TenantHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
  protected readonly ICurrentTenantService CurrentTenantService;
  protected Guid TenantId => CurrentTenantService.TenantId ?? throw new NoTenantHeader();

  protected TenantHandler(ICurrentTenantService currentTenantService)
  {
    CurrentTenantService = currentTenantService;
  }

  public abstract Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

public abstract class TenantHandler<TRequest> : IRequestHandler<TRequest>
    where TRequest : IRequest
{
  protected readonly ICurrentTenantService CurrentTenantService;
  protected Guid TenantId => CurrentTenantService.TenantId ?? throw new NoTenantHeader();

  protected TenantHandler(ICurrentTenantService currentTenantService)
  {
    CurrentTenantService = currentTenantService;
  }

  public abstract Task Handle(TRequest request, CancellationToken ct);
}