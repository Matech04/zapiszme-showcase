namespace App.Application.Booking.BookingEmployees.Dtos;

/// <param name="HasUpcomingSchedule">Czy pracownik ma JAKIKOLWIEK dzień roboczy w oknie rezerwacji
/// (dziś → koniec miesiąca +3, tyle co pozwala przeglądać front). False = nie pracuje/urlop na całe
/// okno → publiczny kreator blokuje jego kafelek. To grafik, nie wolne sloty: bez wybranej usługi nie
/// znamy czasu trwania, więc „zajętości" na tym etapie policzyć się nie da.</param>
public record BookingEmployeeDto(Guid Id, string FirstName, string LastName, bool HasUpcomingSchedule);
