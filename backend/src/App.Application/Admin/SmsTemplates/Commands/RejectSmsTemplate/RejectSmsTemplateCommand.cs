using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.SmsTemplates.Commands.RejectSmsTemplate;

/// <summary>
/// Admin (SystemAdmin) odrzuca oczekujący custom szablon SMS (z powodem). Poprzednia zatwierdzona
/// treść — jeśli była — pozostaje aktywna. Cross-tenant: NIE TenantHandler; jawny IgnoreQueryFilters.
/// </summary>
public record RejectSmsTemplateCommand(Guid TemplateId, string? Reason) : IRequest;

public class RejectSmsTemplateCommandHandler : IRequestHandler<RejectSmsTemplateCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly TimeProvider _timeProvider;

  public RejectSmsTemplateCommandHandler(IApplicationDbContext context, TimeProvider timeProvider)
  {
    _context = context;
    _timeProvider = timeProvider;
  }

  public async Task Handle(RejectSmsTemplateCommand request, CancellationToken ct)
  {
    var template = await _context.SmsTemplates
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct)
      ?? throw new NotFoundException("SmsTemplate", request.TemplateId);

    try
    {
      template.Reject(request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
    }
    catch (InvalidOperationException ex)
    {
      throw new SmsTemplateException(ex.Message, ErrorCodes.SmsTemplateNotPending);
    }

    await _context.SaveChangesAsync(ct);
  }
}
