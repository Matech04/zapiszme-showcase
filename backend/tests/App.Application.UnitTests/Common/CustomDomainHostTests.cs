using App.Application.Common;

namespace App.Application.UnitTests.Common;

public class CustomDomainHostTests
{
  [Theory]
  [InlineData("rezerwacja.salon-przyklad.pl", "salon-przyklad.pl")]
  [InlineData("api.salon-przyklad.pl", "salon-przyklad.pl")]
  [InlineData("api.example.co.uk", "example.co.uk")]
  [InlineData("REZERWACJA.Magdalena.PL", "magdalena.pl")]
  [InlineData("rezerwacja.salon-przyklad.pl:443", "salon-przyklad.pl")]
  [InlineData("  api.salon-przyklad.pl  ", "salon-przyklad.pl")]
  public void ToBaseDomain_StripsLeadingLabel_AndNormalizes(string host, string expected)
  {
    Assert.Equal(expected, CustomDomainHost.ToBaseDomain(host));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("salon-przyklad.pl")] // apex bez subdomeny → null (nie zdejmujemy do "pl")
  [InlineData("localhost")]
  public void ToBaseDomain_NoSubdomainOrInvalid_ReturnsNull(string? host)
  {
    Assert.Null(CustomDomainHost.ToBaseDomain(host));
  }
}
