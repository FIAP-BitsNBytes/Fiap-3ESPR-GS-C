using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public interface IUserRepository
{
    Task<UserEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserEntity?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(UserEntity user, CancellationToken ct = default);
    Task UpdateAsync(UserEntity user, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
