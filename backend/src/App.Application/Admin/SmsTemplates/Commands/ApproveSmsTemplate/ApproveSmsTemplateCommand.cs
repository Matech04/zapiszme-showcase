using App.Application.Common.Interfaces;
using App.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Admin.SmsTemplates.Commands.ApproveSmsTemplate;

/// <summary>
/// Admin (SystemAdmin) zatwierdza oczekujący custom szablon SMS — staje się aktywny przy wysyłce.
/// Cross-tenant: NIE TenantHandler; jawny <c>IgnoreQueryFilters</c> (admin moderuje ponad tenantami,
/// nie ma kontekstu <c>ICurrentTenantService</c> → guard write'u jest fail-open dla admina).
/// </summary>
public record ApproveSmsTemplateCommand(Guid TemplateId) : IRequest;

public class ApproveSmsTemplateCommandHandler : IRequestHandler<ApproveSmsTemplateCommand>
{
  private readonly IApplicationDbContext _context;
  private readonly TimeProvider _timeProvider;

  public ApproveSmsTemplateCommandHandler(IApplicationDbContext context, TimeProvider timeProvider)
  {
    _context = context;
    _timeProvider = timeProvider;
  }

  public async Task Handle(ApproveSmsTemplateCommand request, CancellationToken ct)
  {
    var template = await _context.SmsTemplates
      .IgnoreQueryFilters()
      .FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct)
      ?? throw new NotFoundException("SmsTemplate", request.TemplateId);

    try
    {
      template.Approve(_timeProvider.GetUtcNow().UtcDateTime);
    }
    catch (InvalidOperationException ex)
    {
      throw new SmsTemplateException(ex.Message, ErrorCodes.SmsTemplateNotPending);
    }

    await _context.SaveChangesAsync(ct);
  }
}
