using App.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace App.Infrastructure.Services;

public class EmployeeTenantResolver : IEmployeeTenantResolver
{
  private readonly string _connectionString;

  public EmployeeTenantResolver(IConfiguration configuration)
  {
    _connectionString = configuration.GetConnectionString("DefaultConnection")
      ?? throw new InvalidOperationException("Brak ConnectionStrings:DefaultConnection.");
  }

  public async Task<Guid?> GetTenantIdByUserIdAsync(Guid userId, CancellationToken ct = default)
  {
    await using var conn = new NpgsqlConnection(_connectionString);
    await conn.OpenAsync(ct);

    await using var cmd = new NpgsqlCommand(
      """
      SELECT tenant_id
      FROM "Employees"
      WHERE user_id = @user_id AND is_active = true
      LIMIT 1
      """,
      conn);

    cmd.Parameters.AddWithValue("user_id", userId);
    var scalar = await cmd.ExecuteScalarAsync(ct);
    if (scalar is null || scalar is DBNull)
    {
      return null;
    }

    return (Guid)scalar;
  }
}
