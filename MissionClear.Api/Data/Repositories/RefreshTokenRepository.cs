using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public sealed class RefreshTokenRepository(AppDbContext context) : IRefreshTokenRepository
{
    public async Task<RefreshTokenEntity?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        return await context.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == token, ct);
    }

    public async Task AddAsync(RefreshTokenEntity token, CancellationToken ct = default)
    {
        await context.RefreshTokens.AddAsync(token, ct);
    }

    public async Task RevokeByTokenAsync(string token, CancellationToken ct = default)
    {
        var rt = await context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == token, ct);
        if (rt != null)
        {
            rt.IsRevoked = true;
            await context.SaveChangesAsync(ct);
        }
    }

    public async Task RevokeAllFromUserAsync(Guid userId, CancellationToken ct = default)
    {
        var tokens = await context.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync(ct);

        foreach (var t in tokens)
        {
            t.IsRevoked = true;
        }

        if (tokens.Any())
        {
            await context.SaveChangesAsync(ct);
        }
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await context.SaveChangesAsync(ct);
    }
}
