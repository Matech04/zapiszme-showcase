using App.Domain.Exceptions;

namespace App.Domain.Aggregates.AppointmentAggregate;

public class AppointmentStatus
{
  public static readonly AppointmentStatus Pending = new AppointmentStatus(1, "Pending");
  public static readonly AppointmentStatus Booked = new AppointmentStatus(2, "Booked");
  public static readonly AppointmentStatus InProgress = new AppointmentStatus(3, "InProgress");
  public static readonly AppointmentStatus Completed = new AppointmentStatus(4, "Completed");
  public static readonly AppointmentStatus Canceled = new AppointmentStatus(5, "Canceled");
  /// <summary>Slot zajęty przez publiczną rezerwację — klient jeszcze nie zweryfikował OTP.</summary>
  public static readonly AppointmentStatus AwaitingOtp = new AppointmentStatus(6, "AwaitingOtp");

  public int Id { get; }
  public string Name { get; }

  /// <summary>Stany terminalne wizyty (nieodwracalne) — podgląd inspiracji nie jest już potrzebny.</summary>
  public bool IsTerminal => this == Completed || this == Canceled;

  private AppointmentStatus(int id, string name)
  {
    Id = id;
    Name = name;
  }

  public static List<AppointmentStatus> All = new(){
    Pending, Booked, InProgress, Completed, Canceled, AwaitingOtp
  };

  public static AppointmentStatus FromId(int id)
  {
    return All.FirstOrDefault(x => x.Id == id)
        ?? throw new InvalidAppointmentStatusException(id);
  }

  public static AppointmentStatus FromName(string name)
  {
    return All.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidAppointmentStatusException(name);
  }
}