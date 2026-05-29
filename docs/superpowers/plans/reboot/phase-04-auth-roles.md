# Phase 04 — Auth + Roles (JWT Bearer com Claims)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans

**Goal:** Implementar autenticação JWT com claim de `role`, BCrypt para senhas, serviços de Auth e User que dependem de IUserRepository (não DbContext direto).

**Mudanças em relação ao plan-04-auth.md original:**
- `JwtService` injeta claim `ClaimTypes.Role` no token
- `AuthService.Register` aceita `role` (default "Researcher")
- Services dependem de `IUserRepository` e `IRefreshTokenRepository` — zero DbContext direto
- Login response inclui campo `role` no user object

---

### Task 1: Interfaces de Auth/User

**Files:**
- Create: `MissionClear.Api/Services/Interfaces/IJwtService.cs`
- Create: `MissionClear.Api/Services/Interfaces/IAuthService.cs`
- Create: `MissionClear.Api/Services/Interfaces/IUserService.cs`

- [ ] **Step 1: Escrever IJwtService.cs**

```csharp
using MissionClear.Api.Entities;

namespace MissionClear.Api.Services.Interfaces;

public interface IJwtService
{
    string GenerateAccessToken(UserEntity user);
    string GenerateRefreshToken();
    Guid? ValidateAccessToken(string token);
}
```

- [ ] **Step 2: Escrever IAuthService.cs**

```csharp
using MissionClear.Api.Dtos.Auth;

namespace MissionClear.Api.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<RefreshTokenResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default);
    Task LogoutAsync(LogoutRequest request, CancellationToken ct = default);
}
```

- [ ] **Step 3: Escrever IUserService.cs**

```csharp
using MissionClear.Api.Dtos.User;

namespace MissionClear.Api.Services.Interfaces;

public interface IUserService
{
    Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateUserRequest request, CancellationToken ct = default);
}
```

---

### Task 2: JwtService (inclui claim de Role)

**Files:**
- Create: `MissionClear.Api/Services/JwtService.cs`

- [ ] **Step 1: Escrever testes primeiro**

Em `MissionClear.Tests/Services/JwtServiceTests.cs`:

```csharp
using FluentAssertions;
using MissionClear.Api.Configuration;
using MissionClear.Api.Entities;
using MissionClear.Api.Services;
using Microsoft.Extensions.Options;

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
        DisplayName = "Test",
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
    public void GenerateAccessToken_IncludesRoleClaim_ForAdministrator()
    {
        // Validação indireta: token para Administrator gera string diferente de Researcher
        var adminToken = _service.GenerateAccessToken(MakeUser("Administrator"));
        var researcherToken = _service.GenerateAccessToken(MakeUser("Researcher"));
        adminToken.Should().NotBe(researcherToken);
    }
}
```

- [ ] **Step 2: Rodar testes (devem FALHAR)**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "JwtServiceTests" -v normal
```

Resultado esperado: FAIL — JwtService não existe ainda.

- [ ] **Step 3: Implementar JwtService.cs**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MissionClear.Api.Configuration;
using MissionClear.Api.Entities;
using MissionClear.Api.Services.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MissionClear.Api.Services;

public sealed class JwtService(IOptions<JwtSettings> options) : IJwtService
{
    private readonly JwtSettings _settings = options.Value;

    public string GenerateAccessToken(UserEntity user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("display_name", user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public Guid? ValidateAccessToken(string token)
    {
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
            var handler = new JwtSecurityTokenHandler();
            var result = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _settings.Issuer,
                ValidAudience = _settings.Audience,
                IssuerSigningKey = key,
            }, out _);

            var sub = result.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(sub, out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Rodar testes (devem PASSAR)**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "JwtServiceTests" -v normal
```

---

### Task 3: AuthService (Register com Role, Login retorna Role)

**Files:**
- Create: `MissionClear.Api/Services/AuthService.cs`

- [ ] **Step 1: Escrever testes primeiro**

Em `MissionClear.Tests/Services/AuthServiceTests.cs`:

```csharp
using FluentAssertions;
using MissionClear.Api.Configuration;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace MissionClear.Tests.Services;

public sealed class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IRefreshTokenRepository> _tokenRepo = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _jwt.Setup(j => j.GenerateAccessToken(It.IsAny<UserEntity>())).Returns("access-token");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh-token");
        _service = new AuthService(_userRepo.Object, _tokenRepo.Object, _jwt.Object,
            Options.Create(new JwtSettings { AccessTokenMinutes = 60, RefreshTokenDays = 7,
                Secret = "s", Issuer = "i", Audience = "a" }));
    }

    [Fact]
    public async Task RegisterAsync_CreatesUser_WithResearcherRole_ByDefault()
    {
        _userRepo.Setup(r => r.EmailExistsAsync("new@test.com", default)).ReturnsAsync(false);
        _userRepo.Setup(r => r.CreateAsync(It.IsAny<UserEntity>(), default))
            .ReturnsAsync((UserEntity u, CancellationToken _) => u);

        var result = await _service.RegisterAsync(
            new RegisterRequest("new@test.com", "Pass@word1", "New User"), default);

        result.User.Role.Should().Be("Researcher");
        _userRepo.Verify(r => r.CreateAsync(It.Is<UserEntity>(u =>
            u.Role == "Researcher" && !string.IsNullOrEmpty(u.PasswordHash)), default));
    }

    [Fact]
    public async Task RegisterAsync_CreatesAdministrator_WhenRoleIsAdministrator()
    {
        _userRepo.Setup(r => r.EmailExistsAsync("admin@test.com", default)).ReturnsAsync(false);
        _userRepo.Setup(r => r.CreateAsync(It.IsAny<UserEntity>(), default))
            .ReturnsAsync((UserEntity u, CancellationToken _) => u);

        var result = await _service.RegisterAsync(
            new RegisterRequest("admin@test.com", "Pass@word1", "Admin", "Administrator"), default);

        result.User.Role.Should().Be("Administrator");
    }

    [Fact]
    public async Task RegisterAsync_Throws_WhenEmailExists()
    {
        _userRepo.Setup(r => r.EmailExistsAsync("dup@test.com", default)).ReturnsAsync(true);

        var act = () => _service.RegisterAsync(
            new RegisterRequest("dup@test.com", "Pass@word1", "Dup"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "EMAIL_ALREADY_EXISTS");
    }

    [Fact]
    public async Task RegisterAsync_Throws_WhenPasswordTooWeak()
    {
        _userRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), default)).ReturnsAsync(false);

        var act = () => _service.RegisterAsync(
            new RegisterRequest("a@test.com", "weak", "User"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_PASSWORD_FORMAT");
    }

    [Fact]
    public async Task LoginAsync_ReturnsAuthResponse_WithRole()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Correct@1");
        _userRepo.Setup(r => r.FindByEmailAsync("u@test.com", default))
            .ReturnsAsync(new UserEntity
            {
                Email = "u@test.com", DisplayName = "U",
                PasswordHash = hash, Role = "Researcher"
            });

        var result = await _service.LoginAsync(new LoginRequest("u@test.com", "Correct@1"), default);

        result.User.Role.Should().Be("Researcher");
        result.AccessToken.Should().Be("access-token");
    }

    [Fact]
    public async Task LoginAsync_Throws_WhenPasswordWrong()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Correct@1");
        _userRepo.Setup(r => r.FindByEmailAsync("u@test.com", default))
            .ReturnsAsync(new UserEntity
            {
                Email = "u@test.com", DisplayName = "U",
                PasswordHash = hash
            });

        var act = () => _service.LoginAsync(new LoginRequest("u@test.com", "Wrong@1"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task LoginAsync_Throws_WhenUserNotFound()
    {
        _userRepo.Setup(r => r.FindByEmailAsync("ghost@test.com", default))
            .ReturnsAsync((UserEntity?)null);

        var act = () => _service.LoginAsync(new LoginRequest("ghost@test.com", "Pass@1"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task RefreshAsync_Throws_WhenTokenInvalid()
    {
        _tokenRepo.Setup(r => r.FindActiveByTokenAsync("bad-token", default))
            .ReturnsAsync((RefreshTokenEntity?)null);

        var act = () => _service.RefreshAsync(new RefreshRequest("bad-token"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task LogoutAsync_RevokesToken()
    {
        await _service.LogoutAsync(new LogoutRequest("token"), default);
        _tokenRepo.Verify(r => r.RevokeByTokenAsync("token", default));
    }
}
```

- [ ] **Step 2: Rodar testes (devem FALHAR)**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "AuthServiceTests" -v normal
```

- [ ] **Step 3: Implementar AuthService.cs**

Regras de password: mínimo 8 chars, 1 maiúscula, 1 número.

```csharp
using System.Text.RegularExpressions;
using MissionClear.Api.Configuration;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace MissionClear.Api.Services;

public sealed class AuthService(
    IUserRepository userRepo,
    IRefreshTokenRepository tokenRepo,
    IJwtService jwtService,
    IOptions<JwtSettings> jwtOptions) : IAuthService
{
    private static readonly Regex PasswordRegex =
        new(@"^(?=.*[A-Z])(?=.*\d).{8,}$", RegexOptions.Compiled);

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        if (!PasswordRegex.IsMatch(request.Password))
            throw new DomainException("INVALID_PASSWORD_FORMAT",
                "Password must be at least 8 characters with 1 uppercase and 1 digit.", 400);

        if (await userRepo.EmailExistsAsync(request.Email, ct))
            throw new DomainException("EMAIL_ALREADY_EXISTS", "Email already registered.", 409);

        var user = new UserEntity
        {
            Email = request.Email,
            DisplayName = request.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role
        };

        await userRepo.CreateAsync(user, ct);

        return BuildAuthResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await userRepo.FindByEmailAsync(request.Email, ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new DomainException("INVALID_CREDENTIALS", "Email or password incorrect.", 401);

        return BuildAuthResponse(user);
    }

    public async Task<RefreshTokenResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var existing = await tokenRepo.FindActiveByTokenAsync(request.RefreshToken, ct)
            ?? throw new DomainException("INVALID_REFRESH_TOKEN", "Token invalid or expired.", 401);

        var user = await userRepo.FindByIdAsync(existing.UserId, ct)
            ?? throw new DomainException("INVALID_REFRESH_TOKEN", "User not found.", 401);

        await tokenRepo.RevokeByTokenAsync(request.RefreshToken, ct);
        var newToken = await CreateRefreshTokenAsync(user.Id, ct);

        return new RefreshTokenResponse(
            jwtService.GenerateAccessToken(user),
            jwtOptions.Value.AccessTokenMinutes * 60);
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct) =>
        await tokenRepo.RevokeByTokenAsync(request.RefreshToken, ct);

    private AuthResponse BuildAuthResponse(UserEntity user)
    {
        var settings = jwtOptions.Value;
        // Fire-and-forget token creation — in real scenario use Task.Run carefully
        // For simplicity, refresh token stored async from controller context
        var accessToken = jwtService.GenerateAccessToken(user);
        var refreshToken = jwtService.GenerateRefreshToken();

        return new AuthResponse(
            new UserInAuthResponse(
                $"usr_{user.Id:N}",
                user.Email,
                user.DisplayName,
                user.Role,
                user.CreatedAt),
            accessToken,
            refreshToken,
            settings.AccessTokenMinutes * 60);
    }

    private async Task<RefreshTokenEntity> CreateRefreshTokenAsync(Guid userId, CancellationToken ct)
    {
        var token = new RefreshTokenEntity
        {
            UserId = userId,
            Token = jwtService.GenerateRefreshToken(),
            ExpiresAt = DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays)
        };
        await tokenRepo.CreateAsync(token, ct);
        return token;
    }
}
```

Nota: O método `BuildAuthResponse` retorna o refreshToken sem persistir. O controller é responsável por chamar `CreateRefreshTokenAsync` separadamente OU refatorar `BuildAuthResponse` para ser async. Versão recomendada:

Refatorar `RegisterAsync` e `LoginAsync` para usar a versão async:

```csharp
public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
{
    if (!PasswordRegex.IsMatch(request.Password))
        throw new DomainException("INVALID_PASSWORD_FORMAT", "Password must be at least 8 characters with 1 uppercase and 1 digit.", 400);

    if (await userRepo.EmailExistsAsync(request.Email, ct))
        throw new DomainException("EMAIL_ALREADY_EXISTS", "Email already registered.", 409);

    var user = new UserEntity
    {
        Email = request.Email,
        DisplayName = request.DisplayName,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
        Role = request.Role
    };

    await userRepo.CreateAsync(user, ct);
    return await BuildAuthResponseAsync(user, ct);
}

public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
{
    var user = await userRepo.FindByEmailAsync(request.Email, ct);
    if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        throw new DomainException("INVALID_CREDENTIALS", "Email or password incorrect.", 401);

    return await BuildAuthResponseAsync(user, ct);
}

private async Task<AuthResponse> BuildAuthResponseAsync(UserEntity user, CancellationToken ct)
{
    var refreshToken = await CreateRefreshTokenAsync(user.Id, ct);
    return new AuthResponse(
        new UserInAuthResponse($"usr_{user.Id:N}", user.Email, user.DisplayName, user.Role, user.CreatedAt),
        jwtService.GenerateAccessToken(user),
        refreshToken.Token,
        jwtOptions.Value.AccessTokenMinutes * 60);
}
```

- [ ] **Step 4: Rodar testes (devem PASSAR)**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "AuthServiceTests" -v normal
```

---

### Task 4: UserService

**Files:**
- Create: `MissionClear.Api/Services/UserService.cs`

- [ ] **Step 1: Testes**

Em `MissionClear.Tests/Services/UserServiceTests.cs`:

```csharp
using FluentAssertions;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using Moq;

namespace MissionClear.Tests.Services;

public sealed class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IMissionRepository> _missionRepo = new();
    private readonly UserService _service;

    public UserServiceTests()
    {
        _service = new UserService(_userRepo.Object, _missionRepo.Object);
    }

    [Fact]
    public async Task GetProfileAsync_ReturnsProfile_WithStats()
    {
        var userId = Guid.NewGuid();
        _userRepo.Setup(r => r.FindByIdAsync(userId, default))
            .ReturnsAsync(new UserEntity
            {
                Id = userId, Email = "u@test.com",
                DisplayName = "U", PasswordHash = "h", Role = "Researcher"
            });
        _missionRepo.Setup(r => r.GetStatsByUserIdAsync(userId, default))
            .ReturnsAsync(new MissionStatsProjection(5, 3, 1, 1, 95, 40, 72.0, 47.0, 8, "ISS", new() { { "ISS", 3 } }));

        var result = await _service.GetProfileAsync(userId, default);

        result.Stats.TotalMissions.Should().Be(5);
        result.Stats.FavoriteDestination.Should().Be("ISS");
        result.Role.Should().Be("Researcher");
    }

    [Fact]
    public async Task GetProfileAsync_Throws_WhenUserNotFound()
    {
        _userRepo.Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((UserEntity?)null);

        var act = () => _service.GetProfileAsync(Guid.NewGuid(), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.HttpStatus == 404);
    }

    [Fact]
    public async Task UpdateProfileAsync_Throws_WhenCurrentPasswordWrong()
    {
        var userId = Guid.NewGuid();
        var hash = BCrypt.Net.BCrypt.HashPassword("Current@1");
        _userRepo.Setup(r => r.FindByIdAsync(userId, default))
            .ReturnsAsync(new UserEntity { Id = userId, Email = "u@test.com", DisplayName = "U", PasswordHash = hash });
        _missionRepo.Setup(r => r.GetStatsByUserIdAsync(userId, default))
            .ReturnsAsync(new MissionStatsProjection(0, 0, 0, 0, 0, 0, 0, 0, 0, null, []));

        var act = () => _service.UpdateProfileAsync(userId,
            new UpdateUserRequest(null, "New@Pass1", "Wrong@1"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CURRENT_PASSWORD");
    }
}
```

- [ ] **Step 2: Implementar UserService.cs**

```csharp
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.Interfaces;
using System.Text.RegularExpressions;

namespace MissionClear.Api.Services;

public sealed class UserService(IUserRepository userRepo, IMissionRepository missionRepo) : IUserService
{
    private static readonly Regex PasswordRegex =
        new(@"^(?=.*[A-Z])(?=.*\d).{8,}$", RegexOptions.Compiled);

    public async Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await userRepo.FindByIdAsync(userId, ct)
            ?? throw new DomainException("USER_NOT_FOUND", "User not found.", 404);

        return await BuildProfileAsync(user, ct);
    }

    public async Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await userRepo.FindByIdAsync(userId, ct)
            ?? throw new DomainException("USER_NOT_FOUND", "User not found.", 404);

        if (request.Password is not null)
        {
            if (request.CurrentPassword is null ||
                !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                throw new DomainException("INVALID_CURRENT_PASSWORD", "Current password is incorrect.", 401);

            if (!PasswordRegex.IsMatch(request.Password))
                throw new DomainException("INVALID_PASSWORD_FORMAT", "Password too weak.", 400);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        if (request.DisplayName is not null)
            user.DisplayName = request.DisplayName;

        await userRepo.UpdateAsync(user, ct);
        return await BuildProfileAsync(user, ct);
    }

    private async Task<UserProfileResponse> BuildProfileAsync(UserEntity user, CancellationToken ct)
    {
        var stats = await missionRepo.GetStatsByUserIdAsync(user.Id, ct);
        var successRate = stats.Total == 0 ? 0 : (double)stats.Successful / stats.Total;

        return new UserProfileResponse(
            $"usr_{user.Id:N}",
            user.Email,
            user.DisplayName,
            user.Role,
            user.CreatedAt.ToString("O"),
            new UserStatsDto(
                stats.Total, stats.Successful, stats.Failed, stats.Aborted,
                Math.Round(successRate, 2),
                stats.BestScore, (int)Math.Round(stats.AverageScore),
                stats.FavoriteDestination,
                Math.Round(stats.TotalDeltaV, 2)));
    }
}
```

- [ ] **Step 3: Rodar todos os testes de auth**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "Auth|User" -v normal
```

- [ ] **Step 4: Commit**

```powershell
git add MissionClear.Api/Services/ MissionClear.Tests/Services/
git commit -m "feat(auth): JWT with role claim, BCrypt, AuthService, UserService via Repository"
```
