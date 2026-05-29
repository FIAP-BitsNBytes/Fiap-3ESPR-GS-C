using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;
using Xunit;

namespace MissionClear.Tests.Data;

public sealed class RefreshTokenRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly RefreshTokenRepository _sut;

    public RefreshTokenRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _sut = new RefreshTokenRepository(_context);
    }

    [Fact]
    public async Task GetByTokenAsync_WhenTokenExists_ReturnsToken()
    {
        // Arrange
        var rt = new RefreshTokenEntity { Token = "abc", UserId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddDays(1) };
        _context.RefreshTokens.Add(rt);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetByTokenAsync("abc");

        // Assert
        result.Should().NotBeNull();
        result!.Token.Should().Be("abc");
    }

    [Fact]
    public async Task GetByTokenAsync_WhenDoesNotExist_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByTokenAsync("missing");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_WhenCalled_AddsToContext()
    {
        // Arrange
        var rt = new RefreshTokenEntity { Token = "new", UserId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddDays(1) };

        // Act
        await _sut.AddAsync(rt);
        await _sut.SaveChangesAsync();

        // Assert
        var saved = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.Token == "new");
        saved.Should().NotBeNull();
    }

    [Fact]
    public async Task RevokeAllFromUserAsync_WhenCalled_RevokesOnlyUsersTokens()
    {
        // Arrange
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        _context.RefreshTokens.AddRange(
            new RefreshTokenEntity { Token = "t1", UserId = userId1, ExpiresAt = DateTime.UtcNow.AddDays(1), IsRevoked = false },
            new RefreshTokenEntity { Token = "t2", UserId = userId1, ExpiresAt = DateTime.UtcNow.AddDays(1), IsRevoked = false },
            new RefreshTokenEntity { Token = "t3", UserId = userId2, ExpiresAt = DateTime.UtcNow.AddDays(1), IsRevoked = false }
        );
        await _context.SaveChangesAsync();

        // Act
        await _sut.RevokeAllFromUserAsync(userId1);
        await _sut.SaveChangesAsync();

        // Assert
        var user1Tokens = await _context.RefreshTokens.Where(r => r.UserId == userId1).ToListAsync();
        var user2Tokens = await _context.RefreshTokens.Where(r => r.UserId == userId2).ToListAsync();

        user1Tokens.Should().AllSatisfy(t => t.IsRevoked.Should().BeTrue());
        user2Tokens.Should().AllSatisfy(t => t.IsRevoked.Should().BeFalse());
    }

    [Fact]
    public async Task RevokeAllFromUserAsync_WhenUserHasNoTokens_DoesNothing()
    {
        // Act
        var action = async () => await _sut.RevokeAllFromUserAsync(Guid.NewGuid());

        // Assert
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenCalled_PersistsChanges()
    {
        // Arrange
        var rt = new RefreshTokenEntity { Token = "save", UserId = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddDays(1) };
        await _sut.AddAsync(rt);

        // Act
        await _sut.SaveChangesAsync();

        // Assert
        _context.RefreshTokens.Any(r => r.Token == "save").Should().BeTrue();
    }
}
