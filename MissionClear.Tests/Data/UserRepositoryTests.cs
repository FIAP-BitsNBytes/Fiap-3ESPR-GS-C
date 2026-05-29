using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;
using Xunit;

namespace MissionClear.Tests.Data;

public sealed class UserRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly UserRepository _sut;

    public UserRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _sut = new UserRepository(_context);
    }

    [Fact]
    public async Task GetByIdAsync_WhenUserExists_ReturnsUser()
    {
        // Arrange
        var user = new UserEntity { Email = "test@test.com", DisplayName = "Test", PasswordHash = "hash" };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetByIdAsync(user.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Email.Should().Be(user.Email);
    }

    [Fact]
    public async Task GetByIdAsync_WhenUserDoesNotExist_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByEmailAsync_WhenUserExists_ReturnsUser()
    {
        // Arrange
        var user = new UserEntity { Email = "test@test.com", DisplayName = "Test", PasswordHash = "hash" };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetByEmailAsync(user.Email);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task GetByEmailAsync_WhenUserDoesNotExist_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByEmailAsync("nonexistent@test.com");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_WhenCalled_AddsToContext()
    {
        // Arrange
        var user = new UserEntity { Email = "new@test.com", DisplayName = "New", PasswordHash = "hash" };

        // Act
        await _sut.AddAsync(user);
        await _sut.SaveChangesAsync();

        // Assert
        var saved = await _context.Users.FindAsync(user.Id);
        saved.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_WhenCalled_UpdatesContext()
    {
        // Arrange
        var user = new UserEntity { Email = "old@test.com", DisplayName = "Old", PasswordHash = "hash" };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        user.DisplayName = "Updated";

        // Act
        await _sut.UpdateAsync(user);
        await _sut.SaveChangesAsync();

        // Assert
        var saved = await _context.Users.FindAsync(user.Id);
        saved!.DisplayName.Should().Be("Updated");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenCalled_PersistsChanges()
    {
        // Arrange
        var user = new UserEntity { Email = "save@test.com", DisplayName = "Save", PasswordHash = "hash" };
        await _sut.AddAsync(user);

        // Act
        await _sut.SaveChangesAsync();

        // Assert
        _context.Users.Any(u => u.Email == "save@test.com").Should().BeTrue();
    }
}
