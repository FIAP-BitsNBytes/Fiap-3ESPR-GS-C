using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshTokenEntity?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task AddAsync(RefreshTokenEntity token, CancellationToken ct = default);
    Task RevokeByTokenAsync(string token, CancellationToken ct = default);
    Task RevokeAllFromUserAsync(Guid userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
