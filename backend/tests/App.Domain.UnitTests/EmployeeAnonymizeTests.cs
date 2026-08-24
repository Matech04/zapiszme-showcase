using App.Domain.Aggregates.EmployeeAggregate;

namespace App.Domain.UnitTests;

public class EmployeeAnonymizeTests
{
  [Fact]
  public void Anonymize_clears_pii_and_deactivates()
  {
    var employee = new Employee(Guid.NewGuid(), userId: null, "Magdalena", "Nowak", "magda@salon.pl");
    employee.Update("Magdalena", "Nowak", "Stylizacja paznokci");

    employee.Anonymize();

    Assert.Equal("Pracownik", employee.FirstName);
    Assert.Equal("usunięty", employee.LastName);
    Assert.Equal(string.Empty, employee.Email);
    Assert.Null(employee.Specialization);
    Assert.False(employee.IsActive);
  }
}
