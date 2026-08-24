using App.Application.Customers.Commands.AnonymizeCustomer;
using App.Application.Customers.Commands.CreateCustomer;
using App.Application.Customers.Commands.DeleteCustomer;
using App.Application.Customers.Commands.SetCustomerWhitelist;
using App.Application.Customers.Commands.UpdateCustomer;
using App.Application.Customers.Commands.UpdateCustomerNotes;
using App.Application.Customers.Dtos;
using App.Application.Customers.Queries.ExportCustomerData;
using App.Application.Customers.Queries.GetCustomerById;
using App.Application.Customers.Queries.GetCustomers;
using App.Application.Customers.Queries.SearchCustomersQuery;
using Microsoft.AspNetCore.Authorization; // <-- 1. Wymagany using
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[Authorize(Policy = "GeneralAccess")] // <-- 2. Wymusza ważny token JWT dla wszystkich metod w tej klasie
public class CustomersController : ApiControllerBase
{
  [HttpGet(Name = "GetCustomers")]
  public async Task<ActionResult<List<CustomerDto>>> GetCustomers()
  {
    var result = await Mediator.Send(new GetCustomersQuery());
    return Ok(result);
  }

  [HttpGet("{id}", Name = "GetCustomerById")]
  public async Task<ActionResult<CustomerDto>> GetCustomer(Guid id)
  {
    var result = await Mediator.Send(new GetCustomerByIdQuery(id));
    return Ok(result);
  }

  [HttpGet("search")]
  public async Task<ActionResult<List<SearchCustomerDto>>> SearchCustomers([FromQuery] string searchTerm)
  {
    var result = await Mediator.Send(new SearchCustomersQuery(searchTerm));
    return Ok(result);
  }

  [HttpPost]
  public async Task<ActionResult<Guid>> CreateCustomer(CreateCustomerCommand command)
  {
    var customerId = await Mediator.Send(command);
    return Ok(customerId);
  }

  [HttpPut("{id}")]
  public async Task<ActionResult> UpdateCustomer(Guid id, UpdateCustomerCommand command)
  {
    if (id != command.Id)
    {
      return BadRequest();
    }

    await Mediator.Send(command);
    return NoContent();
  }

  [HttpPatch("{id}/note")]
  public async Task<ActionResult<Guid>> UpdateCustomerNote(Guid id, [FromBody] string note, CancellationToken ct)
  {
    return Ok(await Mediator.Send(new UpdateCustomerNotesCommand(id, note), ct));
  }


  [HttpDelete("{id}")]
  [Authorize(Policy = "BusinessManagement")]
  public async Task<ActionResult> DeleteCustomer(Guid id)
  {
    await Mediator.Send(new DeleteCustomerCommand(id));
    return NoContent();
  }

  /// <summary>
  /// RODO art. 15/20 — eksport kompletu danych osobowych klienta + historii wizyt (JSON). Owner-only.
  /// Poza `GeneralAccess` celowo: zrzut pełnego PII nie może być dostępny szeregowemu pracownikowi
  /// ani współdzielonemu terminalowi „Recepcja" (rola Kiosk). Spójne z `AnonymizeCustomer` niżej.
  /// </summary>
  [HttpGet("{id}/export", Name = "ExportCustomerData")]
  [Authorize(Policy = "BusinessManagement")]
  public async Task<ActionResult<CustomerDataExportDto>> ExportCustomerData(Guid id)
  {
    var result = await Mediator.Send(new ExportCustomerDataQuery(id));
    return Ok(result);
  }

  /// <summary>
  /// RODO art. 17 — trwała anonimizacja danych osobowych klienta (nieodwracalna). Owner-only.
  /// Historia wizyt zostaje, ale bez danych identyfikujących.
  /// </summary>
  [HttpPost("{id}/anonymize")]
  [Authorize(Policy = "BusinessManagement")]
  public async Task<ActionResult> AnonymizeCustomer(Guid id)
  {
    await Mediator.Send(new AnonymizeCustomerCommand(id));
    return NoContent();
  }

  /// <summary>
  /// Whitelist zdejmuje z klienta wymóg zadatku i weryfikacji — to decyzja o pieniądzach salonu
  /// i o zabezpieczeniu anty-abuse, nie o obsłudze wizyty. Minimum Manager (StaffManagement).
  /// </summary>
  [HttpPut("{id}/whitelist")]
  [Authorize(Policy = "StaffManagement")]
  public async Task<ActionResult> SetWhitelist(Guid id, [FromBody] SetWhitelistRequest request)
  {
    await Mediator.Send(new SetCustomerWhitelistCommand(id, request.IsWhitelisted));
    return NoContent();
  }

  /// <inheritdoc cref="SetWhitelist"/>
  [HttpPost("whitelist/bulk")]
  [Authorize(Policy = "StaffManagement")]
  public async Task<ActionResult<BulkWhitelistResult>> BulkWhitelist([FromBody] BulkWhitelistRequest request)
  {
    var updated = await Mediator.Send(new BulkSetCustomerWhitelistCommand(request.CustomerIds, request.IsWhitelisted));
    return Ok(new BulkWhitelistResult(updated));
  }
}

public record SetWhitelistRequest(bool IsWhitelisted);
public record BulkWhitelistRequest(IReadOnlyCollection<Guid> CustomerIds, bool IsWhitelisted);
public record BulkWhitelistResult(int UpdatedCount);