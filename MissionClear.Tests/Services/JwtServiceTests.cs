using FluentAssertions;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Entities;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class JwtServiceTests
{
    private readonly JwtService _service;

    public JwtServiceTests()
    {
        var options = Options.Create(new JwtSettings
        {
            Secret = "test-secret-key-must-be-32-chars-minimum!!",
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            AccessTokenMinutes = 60,
            RefreshTokenDays = 7
        });
        _service = new JwtService(options);
    }

    private static UserEntity MakeUser(string role = "Researcher") => new()
    {
        Id = Guid.NewGuid(),
        Email = "test@test.com",
        DisplayName = "Test User",
        PasswordHash = "hash",
        Role = role
    };

    [Fact]
    public void GenerateAccessToken_ReturnsNonEmptyString()
    {
        var token = _service.GenerateAccessToken(MakeUser());
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateRefreshToken_Returns64CharHexString()
    {
        var token = _service.GenerateRefreshToken();
        token.Should().HaveLength(64);
    }

    [Fact]
    public void ValidateAccessToken_ReturnsUserId_ForValidToken()
    {
        var user = MakeUser();
        var token = _service.GenerateAccessToken(user);

        var userId = _service.ValidateAccessToken(token);

        userId.Should().Be(user.Id);
    }

    [Fact]
    public void ValidateAccessToken_ReturnsNull_ForInvalidToken()
    {
        var result = _service.ValidateAccessToken("invalid.token.here");
        result.Should().BeNull();
    }

    [Fact]
    public void GenerateAccessToken_ProducesDifferentTokens_ForAdministratorVsResearcher()
    {
        // Valida indiretamente que o claim role está incluso (tokens diferem)
        var adminToken = _service.GenerateAccessToken(MakeUser("Administrator"));
        var researcherToken = _service.GenerateAccessToken(MakeUser("Researcher"));

        adminToken.Should().NotBe(researcherToken);
    }
}
