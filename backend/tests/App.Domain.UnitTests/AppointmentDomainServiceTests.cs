using App.Domain.Aggregates.AppointmentAggregate;
using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Domain.Services;
using Moq;

namespace App.Domain.UnitTests;

public class AppointmentDomainServiceTests
{
  private readonly Mock<IAppointmentRepository> _repositoryMock;
  private readonly AppointmentService _sut;

  public AppointmentDomainServiceTests()
  {
    _repositoryMock = new Mock<IAppointmentRepository>();
    _sut = new AppointmentService(_repositoryMock.Object);
  }

  private Employee CreateEmployee()
  {
    return new Employee(Guid.NewGuid(), Guid.NewGuid(), "Bartek", "Barber", "bartek@barber.com");
  }

  // Metoda pomocnicza do łatwego ustawiania grafiku dla pojedynczego dnia w testach
  private void SetEmployeeSchedule(Employee employee, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
  {
    var schedule = new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>>
        {
            {
                dayOfWeek,
                new List<TimeRange> { new TimeRange(startTime, endTime) }
            }
        };

    employee.SetWeeklySchedule(schedule);
  }

  [Fact]
  public async Task IsAvailableAsync_WhenNoCollisionAndEmployeeAvailable_ShouldReturnTrue()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2); // Monday
    var startTime = new TimeOnly(10, 0);
    var endTime = new TimeOnly(11, 0);

    SetEmployeeSchedule(employee, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(16, 0));

    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, startTime, endTime, date, employee.TenantId, null))
        .ReturnsAsync(false);
    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, new TimeRange(startTime, endTime), date, employee.TenantId, (Guid?)null))
        .ReturnsAsync(false);

    // Act
    var result = await _sut.IsAvailableAsync(employee, startTime, endTime, date, employee.TenantId);

    // Assert
    Assert.True(result);
  }

  [Fact]
  public async Task IsAvailableAsync_WhenCollisionExists_ShouldReturnFalse()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2);
    var startTime = new TimeOnly(10, 0);
    var endTime = new TimeOnly(11, 0);

    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, startTime, endTime, date, employee.TenantId, null))
        .ReturnsAsync(true);
    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, new TimeRange(startTime, endTime), date, employee.TenantId, (Guid?)null))
        .ReturnsAsync(true);

    // Act
    var result = await _sut.IsAvailableAsync(employee, startTime, endTime, date, employee.TenantId);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task IsAvailableAsync_WhenEmployeeNotAvailable_ShouldReturnFalse()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2); // Monday
    var startTime = new TimeOnly(10, 0);
    var endTime = new TimeOnly(11, 0);

    // Brak ustawionego grafiku (SetEmployeeSchedule) dla poniedziałku, więc pracownik jest niedostępny
    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, startTime, endTime, date, employee.TenantId, null))
        .ReturnsAsync(false);
    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, new TimeRange(startTime, endTime), date, employee.TenantId, (Guid?)null))
        .ReturnsAsync(false);

    // Act
    var result = await _sut.IsAvailableAsync(employee, startTime, endTime, date, employee.TenantId);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task IsAvailableAsync_WhenEmployeeNotAvailableButIgnoreSchedule_ShouldReturnTrue()
  {
    // Arrange — brak grafiku na poniedziałek (pracownik poza godzinami pracy),
    // ale zapis „poza grafikiem" (ignoreSchedule) i brak kolizji ⇒ termin dostępny.
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2); // Monday — bez ustawionego grafiku
    var startTime = new TimeOnly(20, 0);
    var endTime = new TimeOnly(21, 0);

    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, new TimeRange(startTime, endTime), date, employee.TenantId, (Guid?)null))
        .ReturnsAsync(false);

    // Act
    var result = await _sut.IsAvailableAsync(employee, startTime, endTime, date, employee.TenantId, ignoreSchedule: true);

    // Assert
    Assert.True(result, "Poza grafikiem bez kolizji termin powinien być dostępny");
  }

  [Fact]
  public async Task IsAvailableAsync_WhenIgnoreScheduleButRealCollision_ShouldReturnFalse()
  {
    // Arrange — nawet z ignoreSchedule realna kolizja z inną wizytą musi blokować.
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2);
    var startTime = new TimeOnly(20, 0);
    var endTime = new TimeOnly(21, 0);

    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, new TimeRange(startTime, endTime), date, employee.TenantId, (Guid?)null))
        .ReturnsAsync(true);

    // Act
    var result = await _sut.IsAvailableAsync(employee, startTime, endTime, date, employee.TenantId, ignoreSchedule: true);

    // Assert
    Assert.False(result, "Kolizja z inną wizytą blokuje nawet zapis poza grafikiem");
  }

  [Fact]
  public void EmployeeAvailableSlotsList_ShouldReturnCorrectSlots_WhenCalledWithValidData()
  {
    // Arrange
    var employee = CreateEmployee();
    var serviceId = Guid.NewGuid();
    var serviceDuration = 30;

    // Praca 08:00 - 10:00
    var schedule = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(10, 0)) };

    // Rezerwacja 08:30 - 09:00
    var appointments = new List<TimeRange> { new TimeRange(new TimeOnly(8, 30), new TimeOnly(9, 0)) };

    // Act
    var result = _sut.EmployeeAvailableSlotsList(schedule, appointments, employee, serviceDuration, 15);

    // Assert
    // Spodziewane sloty 15-minutowe dla usługi 30-minutowej:
    // 08:00 - 08:30 (OK)
    // 08:15 - 08:45 (KOLIZJA z rezerwacją 08:30)
    // 08:30 - 09:00 (ZAJĘTE)
    // 08:45 - 09:15 (KOLIZJA z rezerwacją 09:00)
    // 09:00 - 09:30 (OK)
    // 09:15 - 09:45 (OK)
    // 09:30 - 10:00 (OK)

    Assert.Equal(4, result.Count);
    Assert.Contains(new TimeOnly(8, 0), result);
    Assert.Contains(new TimeOnly(9, 0), result);
    Assert.Contains(new TimeOnly(9, 15), result);
    Assert.Contains(new TimeOnly(9, 30), result);

    Assert.DoesNotContain(new TimeOnly(8, 15), result);
    Assert.DoesNotContain(new TimeOnly(8, 30), result);
    Assert.DoesNotContain(new TimeOnly(8, 45), result);
  }

  [Fact]
  public void EmployeeAvailableSlotsList_ShouldUseCustomServiceDuration_WhenEmployeeHasOverride()
  {
    // Arrange
    var employee = CreateEmployee();
    var serviceId = Guid.NewGuid();
    var defaultDuration = 30;
    var customDuration = 45; // Barber jest wolniejszy, potrzebuje 45 min

    // Przypisujemy usługę z customowym czasem
    employee.AssignService(employee.TenantId, serviceId, customDuration, new Money(100, "PLN"));

    // Praca 08:00 - 09:00 (tylko godzina)
    var schedule = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(9, 0)) };
    var appointments = new List<TimeRange>(); // brak wizyt

    // Act
    // Override per-pracownik rozwiązuje teraz CALLER (handler) i przekazuje gotowy czas do metody.
    var resolvedDuration = employee.ResolveServiceDurationMinutes(serviceId, defaultDuration);
    var result = _sut.EmployeeAvailableSlotsList(schedule, appointments, employee, resolvedDuration, 15);

    // Assert
    // Przy oknie 08:00 - 09:00 i usłudze 45 min:
    // 08:00 - 08:45 (OK)
    // 08:15 - 09:00 (OK)
    // 08:30 - 09:15 (ZA DŁUGO - koniec pracy o 09:00)

    Assert.Equal(2, result.Count);
    Assert.Contains(new TimeOnly(8, 0), result);
    Assert.Contains(new TimeOnly(8, 15), result);
    Assert.DoesNotContain(new TimeOnly(8, 30), result);
  }

  [Fact]
  public async Task IsAvailableAsync_WhenCollisionExistsButItIsIgnored_ShouldReturnTrue()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2);
    var startTime = new TimeOnly(10, 0);
    var endTime = new TimeOnly(11, 0);
    var ignoreId = Guid.NewGuid();

    SetEmployeeSchedule(employee, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(16, 0));

    // Repozytorium mówi: nie ma kolizji z INNYMI wizytami (z uwzględnieniem ignoreId)
    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, startTime, endTime, date, employee.TenantId, ignoreId))
        .ReturnsAsync(false);
    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, new TimeRange(startTime, endTime), date, employee.TenantId, ignoreId))
        .ReturnsAsync(false);

    // Act
    var result = await _sut.IsAvailableAsync(employee, startTime, endTime, date, employee.TenantId, ignoreId);

    // Assert
    Assert.True(result);
  }

  [Fact]
  public async Task IsAvailableAsync_WhenRealCollisionExistsWithOtherAppointment_ShouldReturnFalse()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2);
    var startTime = new TimeOnly(10, 0);
    var endTime = new TimeOnly(11, 0);
    var ignoreId = Guid.NewGuid();

    // Nie ma znaczenia, czy pracownik jest dostępny, jeśli repo zgłasza prawdziwą kolizję.
    // Dla pewności jednak ustawiamy go jako dostępnego.
    SetEmployeeSchedule(employee, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(16, 0));

    // Repozytorium mówi: TAK, jest kolizja z kimś innym niż ignoreId
    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, startTime, endTime, date, employee.TenantId, ignoreId))
        .ReturnsAsync(true);
    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, new TimeRange(startTime, endTime), date, employee.TenantId, ignoreId))
        .ReturnsAsync(true);

    // Act
    var result = await _sut.IsAvailableAsync(employee, startTime, endTime, date, employee.TenantId, ignoreId);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task IsAvailableAsync_WhenCancelledAppointmentExists_ShouldReturnTrue()
  {
    // Arrange
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2);
    var startTime = new TimeOnly(10, 0);
    var endTime = new TimeOnly(11, 0);

    SetEmployeeSchedule(employee, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(16, 0));

    // Repozytorium mówi: NIE MA kolizji, bo nasza logika w repo filtruje Canceled!
    // Symulujemy to, że repozytorium sprawdziło status i nie znalazło aktywnej wizyty
    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, startTime, endTime, date, employee.TenantId, null))
        .ReturnsAsync(false);
    _repositoryMock.Setup(x => x.HasCollisionAsync(employee.Id, new TimeRange(startTime, endTime), date, employee.TenantId, (Guid?)null))
        .ReturnsAsync(false);

    // Act
    var result = await _sut.IsAvailableAsync(employee, startTime, endTime, date, employee.TenantId);

    // Assert
    Assert.True(result, "Termin powinien być dostępny, jeśli jedyna wizyta w tym czasie jest anulowana");
  }

  [Fact]
  public async Task IsAvailableAsync_WithIgnoreSet_ForwardsAllIdsToRepository_AndReturnsTrueWhenFree()
  {
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2); // Monday
    var range = new TimeRange(new TimeOnly(10, 0), new TimeOnly(11, 0));
    SetEmployeeSchedule(employee, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(16, 0));

    var ignore = new[] { Guid.NewGuid(), Guid.NewGuid() };
    _repositoryMock
      .Setup(x => x.HasCollisionAsync(employee.Id, range, date, employee.TenantId, It.IsAny<IReadOnlyCollection<Guid>>()))
      .ReturnsAsync(false);

    var result = await _sut.IsAvailableAsync(employee, range, date, employee.TenantId, ignore);

    Assert.True(result);
    _repositoryMock.Verify(
      x => x.HasCollisionAsync(employee.Id, range, date, employee.TenantId,
        It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(ignore[0]) && ids.Contains(ignore[1]))),
      Times.Once);
  }

  [Fact]
  public async Task IsAvailableAsync_WithIgnoreSet_ReturnsFalse_WhenCollision()
  {
    var employee = CreateEmployee();
    var date = new DateOnly(2026, 2, 2);
    var range = new TimeRange(new TimeOnly(10, 0), new TimeOnly(11, 0));
    SetEmployeeSchedule(employee, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(16, 0));

    _repositoryMock
      .Setup(x => x.HasCollisionAsync(employee.Id, range, date, employee.TenantId, It.IsAny<IReadOnlyCollection<Guid>>()))
      .ReturnsAsync(true);

    var result = await _sut.IsAvailableAsync(employee, range, date, employee.TenantId, new[] { Guid.NewGuid() });

    Assert.False(result);
  }

  // --- Testy generowania slotów: zakotwiczenie do startu grafiku ---

  [Fact]
  public void EmployeeAvailableSlotsList_1700To1940_Step40min_ShouldStartAt1700()
  {
    // Arrange — dokładny przypadek zgłoszonego buga produkcyjnego:
    // grafik 17:00-19:40, krok 40 min. Błędna implementacja wyrównywała start
    // do siatki od północy: 1020 % 40 = 20, więc przesuwała o 20 min → 17:20.
    var employee = CreateEmployee();
    var serviceId = Guid.NewGuid();
    var schedule = new List<TimeRange> { new TimeRange(new TimeOnly(17, 0), new TimeOnly(19, 40)) };

    // Act
    var result = _sut.EmployeeAvailableSlotsList(schedule, [], employee, 40, 40);

    // Assert
    Assert.Equal(4, result.Count);
    Assert.Equal(new TimeOnly(17, 0), result[0]);
    Assert.Equal(new TimeOnly(17, 40), result[1]);
    Assert.Equal(new TimeOnly(18, 20), result[2]);
    Assert.Equal(new TimeOnly(19, 0), result[3]);
  }

  [Fact]
  public void EmployeeAvailableSlotsList_WhenStepDoesNotDivideMidnightOffset_ShouldAnchorToScheduleStart()
  {
    // Arrange — krok 40 min, start 9:00 (540 min od północy, 540 % 40 = 20 ≠ 0).
    // Stary kod dałby 9:20, 10:00, 10:20. Poprawny: 9:00, 9:40, 10:20.
    var employee = CreateEmployee();
    var serviceId = Guid.NewGuid();
    var schedule = new List<TimeRange> { new TimeRange(new TimeOnly(9, 0), new TimeOnly(11, 0)) };

    // Act
    var result = _sut.EmployeeAvailableSlotsList(schedule, [], employee, 40, 40);

    // Assert
    Assert.Equal(3, result.Count);
    Assert.Equal(new TimeOnly(9, 0), result[0]);
    Assert.Equal(new TimeOnly(9, 40), result[1]);
    Assert.Equal(new TimeOnly(10, 20), result[2]);
  }

  [Fact]
  public void EmployeeAvailableSlotsList_AfterBreakFromAppointment_ShouldStartFromBreakEnd()
  {
    // Arrange — grafik 8:00-12:00, wizyta 9:00-10:00 dzieli zakres na dwa.
    // Drugi zakres zaczyna się dokładnie o 10:00, sloty powinny startować od 10:00.
    var employee = CreateEmployee();
    var serviceId = Guid.NewGuid();
    var schedule = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)) };
    var appointments = new List<TimeRange> { new TimeRange(new TimeOnly(9, 0), new TimeOnly(10, 0)) };

    // Act — krok 60 min, usługa 60 min
    var result = _sut.EmployeeAvailableSlotsList(schedule, appointments, employee, 60, 60);

    // Assert: sloty 8:00, 10:00, 11:00 (9:00 zajęte)
    Assert.Contains(new TimeOnly(8, 0), result);
    Assert.Contains(new TimeOnly(10, 0), result);
    Assert.Contains(new TimeOnly(11, 0), result);
    Assert.DoesNotContain(new TimeOnly(9, 0), result);
  }

  [Fact]
  public void EmployeeAvailableSlotsList_StepThatDividesMidnight_ShouldNotBeAffectedByFix()
  {
    // Arrange — krok 15 min, start 8:00 (480 % 15 = 0): oba kody dawały to samo.
    // Test regresji — upewniamy się, że fix nie psuje tego przypadku.
    var employee = CreateEmployee();
    var serviceId = Guid.NewGuid();
    var schedule = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(9, 0)) };

    // Act
    var result = _sut.EmployeeAvailableSlotsList(schedule, [], employee, 15, 15);

    // Assert
    Assert.Equal(4, result.Count);
    Assert.Equal(new TimeOnly(8, 0), result[0]);
    Assert.Equal(new TimeOnly(8, 15), result[1]);
    Assert.Equal(new TimeOnly(8, 30), result[2]);
    Assert.Equal(new TimeOnly(8, 45), result[3]);
  }
}