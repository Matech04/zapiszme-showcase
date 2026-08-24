namespace App.Application.Common.Security;

/// <summary>
/// Kanoniczne nazwy ról Identity (odpowiadają <c>DbSeeder.IdentityRoles</c> i policy w <c>Program.cs</c>).
/// Trzymamy je w jednym miejscu, by nowe komendy/zapytania nie rozjeżdżały się na magic-stringach.
/// </summary>
public static class Roles
{
  public const string Admin = "Admin";
  public const string Owner = "Owner";
  public const string Manager = "Manager";
  public const string Employee = "Employee";
  public const string Kiosk = "Kiosk";

  /// <summary>Role, które właściciel może nadać/zmienić pracownikowi (bez Owner/Admin/Kiosk).</summary>
  public static readonly string[] AssignableStaffRoles = [Employee, Manager];

  private static readonly string[] Priority = [Admin, Owner, Manager, Employee, Kiosk];

  /// <summary>
  /// Podstawowa (najwyższa) rola do wyświetlenia, gdy konto ma ich kilka. Zwykle konto ma dokładnie
  /// jedną z ról salonowych, więc kolejność ma znaczenie tylko w rzadkich przypadkach nakładania.
  /// </summary>
  public static string? PrimaryRole(IEnumerable<string> roles)
  {
    var set = roles as ISet<string> ?? roles.ToHashSet();
    foreach (var role in Priority)
    {
      if (set.Contains(role))
      {
        return role;
      }
    }

    return set.Count > 0 ? System.Linq.Enumerable.First(set) : null;
  }
}
