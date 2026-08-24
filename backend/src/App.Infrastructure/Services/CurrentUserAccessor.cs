using App.Application.Common;
using App.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace App.Infrastructure.Services;

public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
  private readonly IHttpContextAccessor _httpContextAccessor;

  public CurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
  {
    _httpContextAccessor = httpContextAccessor;
  }

  public Guid? UserId
  {
    get
    {
      var value = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
      return Guid.TryParse(value, out var id) ? id : null;
    }
  }

  public Guid? CallerEmployeeId
  {
    get
    {
      var http = _httpContextAccessor.HttpContext;
      if (http?.Items.TryGetValue(TenantResolutionHttpContextKeys.ResolvedCallerEmployeeId, out var v) == true
          && v is Guid id)
      {
        return id;
      }

      return null;
    }
  }

  public bool CanManageOtherEmployees
  {
    get
    {
      var user = _httpContextAccessor.HttpContext?.User;
      if (user?.Identity?.IsAuthenticated != true)
      {
        return false;
      }

      // Admin (support impersonation) działa jako Owner w docelowym tenancie.
      return user.IsInRole("Owner") || user.IsInRole("Manager") || user.IsInRole("Admin");
    }
  }

  public bool IsSystemAdmin =>
    _httpContextAccessor.HttpContext?.User.IsInRole("Admin") == true;

  public bool IsDeskAccount =>
    _httpContextAccessor.HttpContext?.User.IsInRole("Kiosk") == true;

  // Marker sesji wsparcia ustawia ImpersonationMiddleware (tylko dla admina z ważnym cookie).
  public bool IsSupportSession =>
    _httpContextAccessor.HttpContext?.Items.ContainsKey(ImpersonationHttpContextKeys.SessionId) == true;
}
