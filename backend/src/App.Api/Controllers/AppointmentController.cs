using App.Application.Appointments.Commands;
using App.Application.Appointments.Commands.DeleteAppointment;
using App.Application.Appointments.Dtos;
using App.Application.Appointments.Queries.GetAppointmentById;
using App.Application.Appointments.Queries.GetAppointmentsByRange;
using App.Application.Appointments.Queries.GetTenantHasAppointments;
using App.Application.Appointments.Queries.GetCustomerAppointments;
using App.Application.Appointments.Queries.GetAvailableTimeSlots;
using App.Application.Appointments.Queries.GetAppointmentMonthAvailability;
using App.Application.Common.Interfaces;
using App.Domain.Aggregates.TenantAggregate;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using App.Application.Appointments.Commands.CreateAppointment;
using App.Application.Appointments.Commands.RescheduleAppointment;
using App.Application.Appointments.Commands.ChangeAppointmentServices;
using App.Application.Appointments.Commands.SwapAppointments;
using App.Application.Appointments.Queries.PreviewSwapAppointments;
using App.Application.Appointments.Commands.UpdateAppointmentStatus;
using App.Application.Appointments.Commands.SetFinalPrice;
using App.Application.Appointments.Commands.SetAppointmentDuration;
using Microsoft.AspNetCore.Authorization;
using App.Application.Common;

namespace App.Api.Controllers;

public record RescheduleAppointmentRequest(
  Guid EmployeeId,
  IReadOnlyList<Guid> ServiceIds,
  DateOnly Date,
  TimeOnly StartTime,
  // Zmiana terminu „poza grafikiem" — personel pomija godziny pracy/urlop (blokuje tylko kolizja).
  bool IgnoreSchedule = false,
  // Niestandardowy czas trwania wizyty (minuty). Null = zachowaj bieżący override.
  int? CustomDurationMinutes = null);

public record SwapAppointmentsRequest(
  Guid FirstAppointmentId,
  Guid SecondAppointmentId,
  bool HarmonizeToShorter);

public record SetFinalPriceRequest(
  decimal Amount,
  string Currency);

public record ChangeAppointmentServicesRequest(
  IReadOnlyList<Guid> ServiceIds);

/// <summary>Body zmiany czasu wizyty. <c>DurationMinutes = null</c> = powrót do czasu standardowego.</summary>
public record SetDurationRequest(int? DurationMinutes);

[Authorize(Policy = "GeneralAccess")]
public class AppointmentsController : ApiControllerBase
{
  [HttpPost]
  public async Task<ActionResult<Guid>> CreateAppointment(CreateAppointmentCommand command, CancellationToken ct)
  {
    // Autoryzacja żyje w handlerze (`IStaffAccessPolicy`) — kontroler tylko przekazuje komendę.
    var appointmentId = await Mediator.Send(command, ct);
    return Ok(appointmentId);
  }

  [HttpPatch("{id}/reschedule")]
  public async Task<ActionResult<Guid>> RescheduleAppointment(
    Guid id,
    [FromBody] RescheduleAppointmentRequest body,
    CancellationToken ct)
  {
    var appointmentId = await Mediator.Send(new RescheduleAppointmentCommand(
      id,
      body.EmployeeId,
      body.ServiceIds,
      body.Date,
      body.StartTime,
      IgnoreSchedule: body.IgnoreSchedule,
      CustomDurationMinutes: body.CustomDurationMinutes), ct);
    return Ok(appointmentId);
  }

  /// <summary>
  /// Zmienia skład usług wizyty zachowując pracownika, datę i godzinę. Termin się nie zmienia —
  /// przeliczany jest tylko czas trwania i cena. Gdy nowa usługa nie mieści się w slocie, handler
  /// rzuca <c>AppointmentSlotUnavailableException</c> (409) i UI proponuje „Zmień termin".
  /// </summary>
  [HttpPatch("{id}/services")]
  public async Task<ActionResult<Guid>> ChangeAppointmentServices(
    Guid id,
    [FromBody] ChangeAppointmentServicesRequest body,
    CancellationToken ct)
  {
    var appointmentId = await Mediator.Send(new ChangeAppointmentServicesCommand(id, body.ServiceIds), ct);
    return Ok(appointmentId);
  }

  /// <summary>
  /// Podgląd zamiany dwóch wizyt — czy mieści się „jak jest", a jeśli nie (różne długości),
  /// czy możliwe jest skrócenie dłuższej wizyty do usługi krótszej. Bez zapisu.
  /// </summary>
  [HttpGet("swap/preview")]
  public async Task<ActionResult<SwapPreviewDto>> PreviewSwap(
    [FromQuery] Guid firstId,
    [FromQuery] Guid secondId,
    CancellationToken ct)
  {
    return Ok(await Mediator.Send(new PreviewSwapAppointmentsQuery(firstId, secondId), ct));
  }

  [HttpPost("swap")]
  public async Task<ActionResult<SwapAppointmentsResult>> SwapAppointments(
    [FromBody] SwapAppointmentsRequest body,
    CancellationToken ct)
  {
    var result = await Mediator.Send(new SwapAppointmentsCommand(
      body.FirstAppointmentId,
      body.SecondAppointmentId,
      body.HarmonizeToShorter), ct);
    return Ok(result);
  }

  [HttpDelete("{id}")]
  [Authorize(Policy = "StaffManagement")]
  public async Task<ActionResult> DeleteAppointment(Guid id)
  {
    await Mediator.Send(new DeleteAppointmentCommand(id));
    return NoContent();
  }

  [HttpGet(Name = "GetAppointments")]
  public async Task<ActionResult<List<AppointmentPreviewDto>>> GetAppointmentsByRange(
    [FromQuery] DateOnly? startDate,
    [FromQuery] DateOnly? endDate,
    [FromQuery] Guid? employeeId,
    CancellationToken ct)
  {
    // Zakres odczytu rozstrzyga `IStaffAccessPolicy` w handlerze — kontroler tylko przekazuje filtr.
    var query = new GetAppointmentsByRangeQuery(
        startDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
        endDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
        employeeId);
    return Ok(await Mediator.Send(query, ct));
  }

  /// <summary>
  /// Czy salon ma już choć jedną realną wizytę (onboarding-checklist). Lekki EXISTS,
  /// bez limitu zakresu dat — w przeciwieństwie do GetAppointments (max 366 dni).
  /// </summary>
  [HttpGet("exists", Name = "GetTenantHasAppointments")]
  public async Task<ActionResult<bool>> HasAnyAppointment(CancellationToken ct)
  {
    return Ok(await Mediator.Send(new GetTenantHasAppointmentsQuery(), ct));
  }

  [HttpGet("{id}", Name = "GetAppointmentById")]
  public async Task<ActionResult<AppointmentDto>> GetAppointmentById(Guid id, CancellationToken ct)
  {
    var query = new GetAppointmentByIdQuery(id);
    return Ok(await Mediator.Send(query, ct));
  }

  [HttpGet("customer/{customerId}")]
  public async Task<ActionResult<List<AppointmentPreviewDto>>> GetCustomerAppointments(Guid customerId, CancellationToken ct)
  {
    var query = new GetCustomerAppointmentsQuery(customerId);
    return Ok(await Mediator.Send(query, ct));
  }

  [HttpGet("available-slots")]
  public async Task<ActionResult<List<AppointmentSlotDto>>> GetAvailableSlots([FromQuery] DateOnly date, [FromQuery] Guid employeeId, [FromQuery] Guid[] serviceIds)
  {
    var query = new GetAvailableTimeSlotsQuery(date, employeeId, serviceIds);
    return Ok(await Mediator.Send(query));
  }

  [HttpGet("month-availability", Name = "GetAppointmentMonthAvailability")]
  public async Task<ActionResult<List<AppointmentDayAvailabilityDto>>> GetMonthAvailability(
      [FromQuery] int year,
      [FromQuery] int month,
      [FromQuery] Guid employeeId,
      [FromQuery] Guid[] serviceIds,
      CancellationToken ct)
  {
    var query = new GetAppointmentMonthAvailabilityQuery(year, month, employeeId, serviceIds);
    return Ok(await Mediator.Send(query, ct));
  }

  //Nie poprawne jak dodam updateowanie wizyty
  [HttpPatch("{id}/status")]
  public async Task<ActionResult<Guid>> UpdateAppointmentStatus(Guid id, [FromBody] int statusId, CancellationToken ct)
  {
    return Ok(await Mediator.Send(new UpdateAppointmentStatusCommand(id, statusId), ct));
  }

  [HttpPatch("{id}/note")]
  public async Task<ActionResult<Guid>> UpdateAppointmentNote(Guid id, [FromBody] string note, CancellationToken ct)
  {
    return Ok(await Mediator.Send(new UpdateAppointmentNotesCommand(id, note), ct));
  }

  /// <summary>
  /// Ustawia (lub czyści) niestandardowy czas trwania wizyty — personel reguluje długość bloku
  /// per wizyta (jedne klientki krócej, inne dłużej). <c>DurationMinutes = null</c> = powrót do
  /// czasu standardowego. Nie zmienia terminu/składu; dłuższy blok, który nie mieści się w slocie,
  /// zwraca 409 (<c>appointment.slot_unavailable</c>).
  /// </summary>
  [HttpPatch("{id}/duration")]
  public async Task<ActionResult<Guid>> SetAppointmentDuration(Guid id, [FromBody] SetDurationRequest body, CancellationToken ct)
  {
    return Ok(await Mediator.Send(new SetAppointmentDurationCommand(id, body.DurationMinutes), ct));
  }

  /// <summary>
  /// Ustawia cenę końcową (faktycznie pobraną) wizyty — używane dla usług z widełkami,
  /// gdy pracownik decyduje o kwocie na miejscu.
  /// </summary>
  [HttpPatch("{id}/final-price")]
  public async Task<ActionResult<Guid>> SetFinalPrice(Guid id, [FromBody] SetFinalPriceRequest body, CancellationToken ct)
  {
    return Ok(await Mediator.Send(new SetFinalPriceCommand(id, body.Amount, body.Currency), ct));
  }


}
