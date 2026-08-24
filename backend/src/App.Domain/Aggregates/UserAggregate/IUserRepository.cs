namespace App.Domain.Aggregates.UserAggregate;

public interface IUserRepository
{
  Task AddAsync(User user);
  Task<User?> GetByIdAsync(Guid id);
  void Update(User user);
  void Remove(User user);
}