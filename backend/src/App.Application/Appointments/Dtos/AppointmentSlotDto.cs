namespace App.Application.Appointments.Dtos;

public record AppointmentSlotDto(
  string slot,
  bool isPreferred = false
  );