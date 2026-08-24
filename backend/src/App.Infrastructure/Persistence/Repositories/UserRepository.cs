using App.Domain.Aggregates.UserAggregate;
using App.Application.Common.Interfaces;

namespace App.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
  private readonly IApplicationDbContext _context;

  public UserRepository(IApplicationDbContext context)
  {
    _context = context;
  }

  public async Task AddAsync(User user) => await _context.Users.AddAsync(user);
  public async Task<User?> GetByIdAsync(Guid id) => await _context.Users.FindAsync(id);
  public void Update(User user) => _context.Users.Update(user);
  public void Remove(User user) => _context.Users.Remove(user);
}