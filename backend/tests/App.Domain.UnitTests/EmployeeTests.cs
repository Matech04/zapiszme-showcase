using App.Domain.Aggregates.EmployeeAggregate;
using App.Domain.Common;
using App.Domain.Exceptions;

namespace App.Domain.UnitTests;

public class EmployeeTests
{
  [Fact]
  public void Constructor_ShouldInitializeCorrectly()
  {
    // Arrange
    var tenantId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var firstName = "Jane";
    var lastName = "Smith";
    var email = "jane.smith@example.com";

    // Act
    var employee = new Employee(tenantId, userId, firstName, lastName, email);

    // Assert
    Assert.NotEqual(Guid.Empty, employee.Id);
    Assert.Equal(tenantId, employee.TenantId);
    Assert.Equal(userId, employee.UserId);
    Assert.Equal(firstName, employee.FirstName);
    Assert.Equal(lastName, employee.LastName);
    Assert.Equal(email, employee.Email);
    Assert.Empty(employee.Services);
  }

  [Fact]
  public void Constructor_WithNullUserId_ShouldInitializeCorrectly()
  {
    // Arrange
    var tenantId = Guid.NewGuid();
    var firstName = "Jane";
    var lastName = "Smith";
    var email = "jane.smith@example.com";

    // Act
    var employee = new Employee(tenantId, null, firstName, lastName, email);

    // Assert
    Assert.NotEqual(Guid.Empty, employee.Id);
    Assert.Equal(tenantId, employee.TenantId);
    Assert.Null(employee.UserId);
    Assert.Equal(firstName, employee.FirstName);
    Assert.Equal(lastName, employee.LastName);
    Assert.Equal(email, employee.Email);
  }

  [Fact]
  public void AddService_ShouldAddServiceToList()
  {
    // Arrange
    var tenantId = Guid.NewGuid();
    var employee = new Employee(tenantId, Guid.NewGuid(), "First", "Last", "test@test.com");
    var serviceId = Guid.NewGuid();
    var price = new Money(50.00m, "PLN");

    // Act
    employee.AssignService(tenantId, serviceId, 30, price);

    // Assert
    Assert.Single(employee.Services);
    var service = employee.Services.First();
    Assert.Equal(serviceId, service.ServiceId);
    Assert.Equal(30, service.CustomDuration);
    Assert.Equal(price, service.CustomPrice);
  }

  [Fact]
  public void AddDuplicateService_ShouldThrowException()
  {
    // Arrange
    var tenantId = Guid.NewGuid();
    var employee = new Employee(tenantId, Guid.NewGuid(), "First", "Last", "test@test.com");
    var serviceId = Guid.NewGuid();
    var price = new Money(50.00m, "PLN");
    employee.AssignService(tenantId, serviceId, 30, price);

    // Act & Assert
    Assert.Throws<AlreadyAssigned>(() => employee.AssignService(tenantId, serviceId, 60, new Money(100.00m, "PLN")));
  }

  [Fact]
  public void UpdateService_ShouldUpdateCustomValues()
  {
    // Arrange
    var tenantId = Guid.NewGuid();
    var employee = new Employee(tenantId, Guid.NewGuid(), "First", "Last", "test@test.com");
    var serviceId = Guid.NewGuid();
    employee.AssignService(tenantId, serviceId, 30, new Money(50.00m, "PLN"));

    // Act
    var newPrice = new Money(75.00m, "PLN");
    employee.UpdateService(serviceId, 45, newPrice);

    // Assert
    var service = employee.Services.First();
    Assert.Equal(45, service.CustomDuration);
    Assert.Equal(newPrice, service.CustomPrice);
  }

  [Fact]
  public void RemoveService_ShouldRemoveFromList()
  {
    // Arrange
    var tenantId = Guid.NewGuid();
    var employee = new Employee(tenantId, Guid.NewGuid(), "First", "Last", "test@test.com");
    var serviceId = Guid.NewGuid();
    employee.AssignService(tenantId, serviceId, 30, new Money(50.00m, "PLN"));

    // Act
    employee.UnassignService(serviceId);

    // Assert
    Assert.Empty(employee.Services);
  }

  [Fact]
  public void RemoveNonExistentService_ShouldThrowException()
  {
    // Arrange
    var employee = new Employee(Guid.NewGuid(), Guid.NewGuid(), "First", "Last", "test@test.com");

    // Act & Assert
    Assert.ThrowsAny<Exception>(() => employee.UnassignService(Guid.NewGuid()));
  }

  [Fact]
  public void SetWeeklySchedule_ShouldPopulateEmployeeSchedules()
  {
    // Arrange
    var employee = new Employee(Guid.NewGuid(), Guid.NewGuid(), "First", "Last", "test@test.com");
    var day = DayOfWeek.Monday;
    var ranges = new List<TimeRange> { new TimeRange(new TimeOnly(8, 0), new TimeOnly(12, 0)) };
    var schedule = new Dictionary<DayOfWeek, IReadOnlyCollection<TimeRange>>
    {
        { day, ranges }
    };

    // Act
    employee.SetWeeklySchedule(schedule);

    // Assert
    Assert.Single(employee.Schedules);
    var storedSchedule = employee.Schedules.First();
    Assert.Equal(1, storedSchedule.NumberOfCycles);
    Assert.Equal(DateOnly.MinValue, storedSchedule.ActiveRange.StartDate);
    Assert.Equal(DateOnly.MaxValue, storedSchedule.ActiveRange.EndDate);
    Assert.Single(storedSchedule.ScheduleDays);
    Assert.Equal((int)day, storedSchedule.ScheduleDays.First().CycleIndex);
    Assert.Single(storedSchedule.ScheduleDays.First().WorkRanges);
    Assert.Equal(ranges.First(), storedSchedule.ScheduleDays.First().WorkRanges.First());
  }
}
