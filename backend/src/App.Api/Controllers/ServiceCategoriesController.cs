using App.Application.ServiceCategories.Commands.CreateServiceCategory;
using App.Application.ServiceCategories.Commands.DeleteServiceCategory;
using App.Application.ServiceCategories.Commands.ReorderCatalogSections;
using App.Application.ServiceCategories.Commands.ReorderServiceCategories;
using App.Application.ServiceCategories.Commands.UpdateServiceCategory;
using App.Application.ServiceCategories.Dtos;
using App.Application.ServiceCategories.Queries.GetServiceCategories;
using App.Application.ServiceCategories.Queries.GetServiceCategoryById;
using App.Application.ServiceCategories.Queries.GetUncategorizedOrder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Controllers;

[Authorize(Policy = "GeneralAccess")]
public class ServiceCategoriesController : ApiControllerBase
{
  [HttpGet]
  public async Task<ActionResult<List<ServiceCategoryDto>>> GetServiceCategories()
  {
    var result = await Mediator.Send(new GetServiceCategoriesQuery());
    return Ok(result);
  }

  [HttpGet("{id}")]
  public async Task<ActionResult<ServiceCategoryDto>> GetServiceCategory(Guid id)
  {
    var result = await Mediator.Send(new GetServiceCategoryByIdQuery(id));
    return Ok(result);
  }

  [Authorize(Policy = "StaffManagement")]
  [HttpPost]
  public async Task<ActionResult<Guid>> CreateServiceCategory(CreateServiceCategoryCommand command)
  {
    var categoryId = await Mediator.Send(command);
    return Ok(categoryId);
  }
  [Authorize(Policy = "StaffManagement")]
  [HttpPut("{id}")]
  public async Task<ActionResult> UpdateServiceCategory(Guid id, UpdateServiceCategoryCommand command)
  {
    if (id != command.Id)
    {
      return BadRequest();
    }

    await Mediator.Send(command);
    return NoContent();
  }
  [Authorize(Policy = "StaffManagement")]
  [HttpDelete("{id}")]
  public async Task<ActionResult> DeleteServiceCategory(Guid id)
  {
    await Mediator.Send(new DeleteServiceCategoryCommand(id));
    return NoContent();
  }

  [Authorize(Policy = "StaffManagement")]
  [HttpPut("reorder")]
  public async Task<ActionResult> ReorderServiceCategories(ReorderServiceCategoriesCommand command)
  {
    await Mediator.Send(command);
    return NoContent();
  }

  /// <summary>
  /// Unified-reorder: realne kategorie + wirtualna sekcja „Bez kategorii" (null) w jednej sekwencji.
  /// </summary>
  [Authorize(Policy = "StaffManagement")]
  [HttpPut("reorder-sections")]
  public async Task<ActionResult> ReorderCatalogSections(ReorderCatalogSectionsCommand command)
  {
    await Mediator.Send(command);
    return NoContent();
  }

  /// <summary>Pozycja wirtualnej sekcji „Bez kategorii" w katalogu (do unified-reorder w UI).</summary>
  [HttpGet("uncategorized-order")]
  public async Task<ActionResult<UncategorizedOrderDto>> GetUncategorizedOrder()
  {
    var result = await Mediator.Send(new GetUncategorizedOrderQuery());
    return Ok(result);
  }
}