using App.Application.Common;
using App.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

public class CurrentTenantService : ICurrentTenantService
{
  private readonly IHttpContextAccessor _httpContextAccessor;

  public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
  {
    _httpContextAccessor = httpContextAccessor;
  }

  public Guid? TenantId
  {
    get
    {
      var http = _httpContextAccessor.HttpContext;
      if (http == null)
      {
        return null;
      }

      if (http.Items.TryGetValue(TenantResolutionHttpContextKeys.ResolvedTenantId, out var resolved)
          && resolved is Guid tenantId)
      {
        return tenantId;
      }

      return null;
    }
  }
}
