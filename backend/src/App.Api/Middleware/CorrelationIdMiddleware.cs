using Serilog.Context;

namespace App.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
  public const string HeaderName = "X-Correlation-ID";

  private readonly RequestDelegate _next;

  public CorrelationIdMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    var correlationId = ResolveCorrelationId(context);
    context.TraceIdentifier = correlationId;

    context.Response.OnStarting(() =>
    {
      context.Response.Headers[HeaderName] = correlationId;
      return Task.CompletedTask;
    });

    using (LogContext.PushProperty("CorrelationId", correlationId))
    using (LogContext.PushProperty("RequestId", context.TraceIdentifier))
    {
      await _next(context);
    }
  }

  private static string ResolveCorrelationId(HttpContext context)
  {
    var fromHeader = context.Request.Headers[HeaderName].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(fromHeader) && fromHeader.Length <= 128)
    {
      return fromHeader;
    }

    return context.TraceIdentifier;
  }
}
