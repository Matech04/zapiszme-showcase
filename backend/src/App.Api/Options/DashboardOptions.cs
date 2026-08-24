using System.ComponentModel.DataAnnotations;

namespace App.Api.Options;

public sealed class DashboardOptions
{
  public const string SectionName = "Dashboard";

  [Required, Url]
  public string BaseUrl { get; set; } = string.Empty;
}
