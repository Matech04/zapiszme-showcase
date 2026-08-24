using App.Application.Common;
using App.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Feedback.Commands;

public record SubmitFeedbackCommand(
  string Kind,
  string Title,
  string Description,
  string? PageUrl,
  string? UserEmail
) : IRequest;

internal sealed class SubmitFeedbackCommandHandler
    : TenantHandler<SubmitFeedbackCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly IFeedbackSender _sender;
  private readonly ILogger<SubmitFeedbackCommandHandler> _logger;

  public SubmitFeedbackCommandHandler(
    IApplicationDbContext context,
    IFeedbackSender sender,
    ICurrentTenantService currentTenantService,
    ILogger<SubmitFeedbackCommandHandler> logger)
    : base(currentTenantService)
  {
    _context = context;
    _sender = sender;
    _logger = logger;
  }

  public override async Task Handle(SubmitFeedbackCommand request, CancellationToken ct)
  {
    var tenant = await _context.Tenants
      .AsNoTracking()
      .FirstOrDefaultAsync(t => t.Id == TenantId, ct);

    var salonName = tenant?.Name ?? "Nieznany salon";

    // Audit log NIEZALEŻNY od działania mail-sendera. Jeśli ACS pada / brak konfiguracji
    // recipienta, feedback wciąż jest widoczny w sinku (Seq / App Insights).
    _logger.LogInformation(
      "FeedbackSubmitted TenantId={TenantId} Kind={Kind} Title={Title} UserEmail={UserEmail} Category={Category}",
      TenantId, request.Kind, request.Title, request.UserEmail, "audit");

    await _sender.SendAsync(
      request.Kind,
      request.Title,
      request.Description,
      request.PageUrl,
      request.UserEmail,
      salonName,
      ct);
  }
}
