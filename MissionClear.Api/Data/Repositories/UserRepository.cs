using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public sealed class UserRepository(AppDbContext context) : IUserRepository
{
    public async Task<UserEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Users.FindAsync([id], ct);
    }

    public async Task<UserEntity?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        return await context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    public async Task AddAsync(UserEntity user, CancellationToken ct = default)
    {
        await context.Users.AddAsync(user, ct);
    }

    public Task UpdateAsync(UserEntity user, CancellationToken ct = default)
    {
        context.Users.Update(user);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await context.SaveChangesAsync(ct);
    }
}
