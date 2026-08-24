using Microsoft.AspNetCore.Http;

namespace App.Application.Booking;

internal static class BookingClientIp
{
  public static string? From(HttpContext? httpContext)
  {
    if (httpContext is null)
    {
      return null;
    }

    return httpContext.Connection.RemoteIpAddress?.ToString();
  }
}
