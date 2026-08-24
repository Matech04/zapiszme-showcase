using App.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Controllers;

/// <summary>
/// Rozwiązuje skrócone linki <c>/p/{code}</c> i przekierowuje (302) na docelowy URL (np. Stripe
/// Checkout). Anonimowy, bez kontekstu tenanta. W produkcji <c>zapisz.me/p/*</c> jest routowane
/// przez Caddy na backend (patrz caddyfile). GET → poza ochroną antiforgery i CORS (nawigacja).
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("p")]
public class ShortLinkController : ControllerBase
{
  private readonly IApplicationDbContext _context;

  public ShortLinkController(IApplicationDbContext context)
  {
    _context = context;
  }

  [HttpGet("{code}")]
  public async Task<IActionResult> Resolve([FromRoute] string code, CancellationToken ct)
  {
    var link = await _context.ShortLinks
      .AsNoTracking()
      .FirstOrDefaultAsync(l => l.Code == code, ct);

    if (link is null || link.IsExpired(DateTime.UtcNow))
    {
      return new ContentResult
      {
        StatusCode = StatusCodes.Status404NotFound,
        ContentType = "text/html; charset=utf-8",
        Content = """
          <!doctype html><html lang="pl"><head><meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Link nieważny</title></head>
          <body style="font-family:system-ui;display:flex;min-height:100vh;align-items:center;justify-content:center;margin:0;padding:24px;text-align:center">
          <div><h1>Link nieważny lub wygasł</h1>
          <p>Poproś salon o nowy link do płatności.</p></div></body></html>
          """,
      };
    }

    return Redirect(link.TargetUrl);
  }
}
