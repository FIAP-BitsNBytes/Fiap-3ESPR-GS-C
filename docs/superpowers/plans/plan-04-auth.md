# Plan 04 — Authentication + Roles (JWT Bearer com Claims, BCrypt, Repository Pattern)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans

**Execution order:** After plan-00 + plan-01 + plan-02. Parallel with plan-03.
**Estimated time:** 90 minutes.
**Goal:** Implementar autenticação JWT Bearer completa com claim de `role`, hash BCrypt, e serviços AuthService/UserService que dependem de IUserRepository e IRefreshTokenRepository — nunca de AppDbContext diretamente.
**Dependencies:** plan-00 (scaffolding), plan-01 (Entities: UserEntity com campo Role, RefreshTokenEntity, MissionEntity; Repositories: IUserRepository, IRefreshTokenRepository, IMissionRepository; AppDbContext), plan-02 (DTOs de Auth/User, DomainException).
**Unlocks:** plan-07-controllers.md

---

## Separação de Responsabilidades

```
Services/Interfaces/
  IJwtService.cs              ← contrato público: GenerateAccessToken, GenerateRefreshToken, ValidateAccessToken
  IAuthService.cs             ← contrato: Register/Login/Refresh/Logout
  IUserService.cs             ← contrato: GetProfile/UpdateProfile

Services/
  JwtService.cs               ← implementa IJwtService; gera claim Role; refresh token via RandomNumberGenerator
  AuthService.cs              ← implementa IAuthService; depende de IUserRepository + IRefreshTokenRepository + IJwtService
  UserService.cs              ← implementa IUserService; depende de IUserRepository + IMissionRepository

Exceptions/
  DomainException.cs          ← erro de domínio com ErrorCode + HttpStatus
```

**Regra inviolável:** `AuthService` e `UserService` NUNCA acessam `AppDbContext` diretamente — sempre via repositório.

---

## Constraints de Segurança (não-negociáveis)

| Requisito | Detalhe |
|---|---|
| Hash de senhas | `BCrypt.Net.BCrypt.HashPassword(password)` no registro |
| Verificação de senhas | `BCrypt.Net.BCrypt.Verify(password, hash)` no login |
| Regex de senha | `^(?=.*[A-Z])(?=.*\d).{8,}$` (mín. 8 chars, 1 maiúscula, 1 dígito) |
| Refresh token | `RandomNumberGenerator.GetBytes(32)` → `Convert.ToHexString(bytes).ToLowerInvariant()` → 64 chars |
| Role no JWT | `ClaimTypes.Role` incluso em `GenerateAccessToken` |
| Role padrão | "Researcher" (registro público) |
| Logout | Marca `IsRevoked = true` — nunca deleta (trilha de auditoria) |

---

## Códigos de Erro DomainException

| Código | Status HTTP | Situação |
|---|---|---|
| `EMAIL_ALREADY_EXISTS` | 409 | Email já cadastrado no registro |
| `INVALID_PASSWORD_FORMAT` | 400 | Senha não atende ao regex |
| `INVALID_CREDENTIALS` | 401 | Email não encontrado ou senha errada no login |
| `INVALID_REFRESH_TOKEN` | 401 | Token não encontrado, expirado ou revogado |
| `INVALID_CURRENT_PASSWORD` | 401 | Senha atual incorreta no UpdateProfile |
| `USER_NOT_FOUND` | 404 | Usuário não encontrado no GetProfile/UpdateProfile |

---

## Task 4.0 — DomainException

**⚠️ IMPORTANT:** `DomainException` is defined in **plan-02 Task 1.1**. Do NOT create this file here.

Execute plan-02 Task 1.1 first, or verify `MissionClear.Api/Exceptions/DomainException.cs` already exists with:
- `string ErrorCode` property
- `int HttpStatus` property
- `DomainException(string errorCode, string message, int httpStatus)` constructor

Use `ex.HttpStatus` (not `ex.StatusCode`) in GlobalExceptionMiddleware.

---

## Task 4.1 — Interfaces de Serviço

**Files:**
- Create: `MissionClear.Api/Services/Interfaces/IJwtService.cs`
- Create: `MissionClear.Api/Services/Interfaces/IAuthService.cs`
- Create: `MissionClear.Api/Services/Interfaces/IUserService.cs`

### IJwtService.cs

```csharp
using MissionClear.Api.Entities;

namespace MissionClear.Api.Services.Interfaces;

public interface IJwtService
{
    /// <summary>Gera um access token JWT com claims: Sub, Email, display_name, ClaimTypes.Role, Jti.</summary>
    string GenerateAccessToken(UserEntity user);

    /// <summary>Gera um refresh token opaco: RandomNumberGenerator.GetBytes(32) → hex lowercase → 64 chars.</summary>
    string GenerateRefreshToken();

    /// <summary>Valida o token e retorna o userId (Guid) extraído do claim Sub. Retorna null se inválido.</summary>
    Guid? ValidateAccessToken(string token);
}
```

### IAuthService.cs

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

### IUserService.cs

```csharp
using MissionClear.Api.Dtos.User;

namespace MissionClear.Api.Services.Interfaces;

public interface IUserService
{
    Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateUserRequest request, CancellationToken ct = default);
}
```

**Commit:**
```powershell
git add MissionClear.Api/Services/Interfaces/
git commit -m "feat(auth): interfaces IJwtService, IAuthService, IUserService"
```

---

## Task 4.2 — JwtService

**Files:**
- Create: `MissionClear.Api/Services/JwtService.cs`
- Create: `MissionClear.Tests/Services/JwtServiceTests.cs`

### Step 1: Testes (RED)

`MissionClear.Tests/Services/JwtServiceTests.cs`

```csharp
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
```

### Step 2: Rodar testes (devem FALHAR)

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "JwtServiceTests" -v normal
```

Resultado esperado: **FAIL** — `JwtService` não existe ainda.

### Step 3: Implementação (GREEN)

`MissionClear.Api/Services/JwtService.cs`

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MissionClear.Api.Configuration;
using MissionClear.Api.Entities;
using MissionClear.Api.Services.Interfaces;

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
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _settings.Issuer,
                ValidAudience = _settings.Audience,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.Zero
            }, out _);

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(sub, out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }
}
```

### Step 4: Rodar testes (devem PASSAR)

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "JwtServiceTests" -v normal
```

### Step 5: Commit

```powershell
git add MissionClear.Api/Services/JwtService.cs MissionClear.Tests/Services/JwtServiceTests.cs
git commit -m "feat(auth): JwtService com role claim, refresh token RandomNumberGenerator 64-char hex"
```

---

## Task 4.3 — AuthService

**Files:**
- Create: `MissionClear.Api/Services/AuthService.cs`
- Create: `MissionClear.Tests/Services/AuthServiceTests.cs`

**Pré-requisito:** `IUserRepository`, `IRefreshTokenRepository` existentes de plan-01.

### Step 1: Testes (RED)

`MissionClear.Tests/Services/AuthServiceTests.cs`

```csharp
using FluentAssertions;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using Moq;
using Xunit;

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

        _service = new AuthService(
            _userRepo.Object,
            _tokenRepo.Object,
            _jwt.Object,
            Options.Create(new JwtSettings
            {
                Secret = "test-secret-key-must-be-32-chars-minimum!!",
                Issuer = "TestIssuer",
                Audience = "TestAudience",
                AccessTokenMinutes = 60,
                RefreshTokenDays = 7
            }));
    }

    // ── RegisterAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_CreatesUser_WithResearcherRole_ByDefault()
    {
        _userRepo.Setup(r => r.EmailExistsAsync("new@test.com", default)).ReturnsAsync(false);
        _userRepo.Setup(r => r.CreateAsync(It.IsAny<UserEntity>(), default))
            .ReturnsAsync((UserEntity u, CancellationToken _) => u);

        var result = await _service.RegisterAsync(
            new RegisterRequest("new@test.com", "Pass@word1", "New User"), default);

        result.User.Role.Should().Be("Researcher");
        result.AccessToken.Should().Be("access-token");

        _userRepo.Verify(r => r.CreateAsync(
            It.Is<UserEntity>(u =>
                u.Role == "Researcher" &&
                !string.IsNullOrEmpty(u.PasswordHash) &&
                u.PasswordHash != "Pass@word1"),
            default));
    }

    // NOTE: RegisterAsync_CreatesAdministrator_WhenRoleProvided removed — public registration
    // always creates Researcher (Fix N-03). Role promotion happens via a separate admin endpoint only.
    // RegisterRequest no longer accepts a Role field.

    [Fact]
    public async Task RegisterAsync_Throws_EMAIL_ALREADY_EXISTS_WhenEmailDuplicated()
    {
        _userRepo.Setup(r => r.EmailExistsAsync("dup@test.com", default)).ReturnsAsync(true);

        var act = () => _service.RegisterAsync(
            new RegisterRequest("dup@test.com", "Pass@word1", "Dup"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "EMAIL_ALREADY_EXISTS" && e.HttpStatus == 409);
    }

    [Theory]
    [InlineData("short1A")]       // menos de 8 chars
    [InlineData("nouppercase1")]  // sem maiúscula
    [InlineData("NoDigitsHere")]  // sem dígito
    public async Task RegisterAsync_Throws_INVALID_PASSWORD_FORMAT_WhenPasswordWeak(string weak)
    {
        _userRepo.Setup(r => r.EmailExistsAsync(It.IsAny<string>(), default)).ReturnsAsync(false);

        var act = () => _service.RegisterAsync(
            new RegisterRequest("a@test.com", weak, "User"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_PASSWORD_FORMAT" && e.HttpStatus == 400);
    }

    // ── LoginAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_ReturnsAuthResponse_WithRole()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Correct@1");
        _userRepo.Setup(r => r.FindByEmailAsync("u@test.com", default))
            .ReturnsAsync(new UserEntity
            {
                Id = Guid.NewGuid(),
                Email = "u@test.com",
                DisplayName = "U",
                PasswordHash = hash,
                Role = "Researcher"
            });

        var result = await _service.LoginAsync(new LoginRequest("u@test.com", "Correct@1"), default);

        result.User.Role.Should().Be("Researcher");
        result.AccessToken.Should().Be("access-token");
    }

    [Fact]
    public async Task LoginAsync_Throws_INVALID_CREDENTIALS_WhenUserNotFound()
    {
        _userRepo.Setup(r => r.FindByEmailAsync("ghost@test.com", default))
            .ReturnsAsync((UserEntity?)null);

        var act = () => _service.LoginAsync(new LoginRequest("ghost@test.com", "Pass@1"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CREDENTIALS" && e.HttpStatus == 401);
    }

    [Fact]
    public async Task LoginAsync_Throws_INVALID_CREDENTIALS_WhenPasswordWrong()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Correct@1");
        _userRepo.Setup(r => r.FindByEmailAsync("u@test.com", default))
            .ReturnsAsync(new UserEntity
            {
                Id = Guid.NewGuid(),
                Email = "u@test.com",
                DisplayName = "U",
                PasswordHash = hash,
                Role = "Researcher"
            });

        var act = () => _service.LoginAsync(new LoginRequest("u@test.com", "Wrong@1"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CREDENTIALS" && e.HttpStatus == 401);
    }

    // ── RefreshAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_ReturnsNewAccessToken_WhenTokenValid()
    {
        var userId = Guid.NewGuid();
        var existingToken = new RefreshTokenEntity
        {
            Token = "valid-token",
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsRevoked = false
        };
        _tokenRepo.Setup(r => r.FindActiveByTokenAsync("valid-token", default))
            .ReturnsAsync(existingToken);
        _userRepo.Setup(r => r.FindByIdAsync(userId, default))
            .ReturnsAsync(new UserEntity
            {
                Id = userId, Email = "u@test.com",
                DisplayName = "U", PasswordHash = "h", Role = "Researcher"
            });
        _tokenRepo.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), default))
            .Returns(Task.CompletedTask);

        var result = await _service.RefreshAsync(new RefreshRequest("valid-token"), default);

        result.AccessToken.Should().Be("access-token");
        _tokenRepo.Verify(r => r.RevokeByTokenAsync("valid-token", default));
    }

    [Fact]
    public async Task RefreshAsync_Throws_INVALID_REFRESH_TOKEN_WhenTokenNotFound()
    {
        _tokenRepo.Setup(r => r.FindActiveByTokenAsync("bad-token", default))
            .ReturnsAsync((RefreshTokenEntity?)null);

        var act = () => _service.RefreshAsync(new RefreshRequest("bad-token"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_REFRESH_TOKEN" && e.HttpStatus == 401);
    }

    // ── LogoutAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LogoutAsync_RevokesToken()
    {
        await _service.LogoutAsync(new LogoutRequest("some-token"), default);

        _tokenRepo.Verify(r => r.RevokeByTokenAsync("some-token", default));
    }
}
```

### Step 2: Rodar testes (devem FALHAR)

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "AuthServiceTests" -v normal
```

### Step 3: Implementação (GREEN)

`MissionClear.Api/Services/AuthService.cs`

```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class AuthService(
    IUserRepository userRepo,
    IRefreshTokenRepository tokenRepo,
    IJwtService jwtService,
    IOptions<JwtSettings> jwtOptions) : IAuthService
{
    private static readonly Regex PasswordRegex =
        new(@"^(?=.*[A-Z])(?=.*\d).{8,}$", RegexOptions.Compiled);

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (!PasswordRegex.IsMatch(request.Password))
            throw new DomainException("INVALID_PASSWORD_FORMAT",
                "Password must be at least 8 characters with 1 uppercase and 1 digit.", 400);

        if (await userRepo.EmailExistsAsync(request.Email, ct))
            throw new DomainException("EMAIL_ALREADY_EXISTS", "Email already registered.", 409);

        var user = new UserEntity
        {
            Email = request.Email.Trim().ToLowerInvariant(),
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = "Researcher"  // Public registration always creates Researcher. Role promotion via admin endpoint only.
        };

        await userRepo.CreateAsync(user, ct);
        return await BuildAuthResponseAsync(user, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userRepo.FindByEmailAsync(request.Email.Trim().ToLowerInvariant(), ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new DomainException("INVALID_CREDENTIALS", "Email or password incorrect.", 401);

        return await BuildAuthResponseAsync(user, ct);
    }

    public async Task<RefreshTokenResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var existing = await tokenRepo.FindActiveByTokenAsync(request.RefreshToken, ct)
            ?? throw new DomainException("INVALID_REFRESH_TOKEN", "Token invalid or expired.", 401);

        var user = await userRepo.FindByIdAsync(existing.UserId, ct)
            ?? throw new DomainException("INVALID_REFRESH_TOKEN", "User not found.", 401);

        await tokenRepo.RevokeByTokenAsync(request.RefreshToken, ct);

        var newRefreshToken = new RefreshTokenEntity
        {
            UserId = user.Id,
            Token = jwtService.GenerateRefreshToken(),
            ExpiresAt = DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays)
        };
        await tokenRepo.CreateAsync(newRefreshToken, ct);

        return new RefreshTokenResponse(
            jwtService.GenerateAccessToken(user),
            jwtOptions.Value.AccessTokenMinutes * 60);
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct = default) =>
        await tokenRepo.RevokeByTokenAsync(request.RefreshToken, ct);

    private async Task<AuthResponse> BuildAuthResponseAsync(UserEntity user, CancellationToken ct)
    {
        var refreshToken = new RefreshTokenEntity
        {
            UserId = user.Id,
            Token = jwtService.GenerateRefreshToken(),
            ExpiresAt = DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays)
        };
        await tokenRepo.CreateAsync(refreshToken, ct);

        return new AuthResponse(
            new UserInAuthResponse($"usr_{user.Id:N}", user.Email, user.DisplayName, user.Role, user.CreatedAt.ToString("O")),
            jwtService.GenerateAccessToken(user),
            refreshToken.Token,
            jwtOptions.Value.AccessTokenMinutes * 60);
    }
}
```

### Step 4: Rodar testes (devem PASSAR)

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "AuthServiceTests" -v normal
```

### Step 5: Commit

```powershell
git add MissionClear.Api/Services/AuthService.cs MissionClear.Tests/Services/AuthServiceTests.cs
git commit -m "feat(auth): AuthService register/login/refresh/logout com BCrypt e Repository"
```

---

## Task 4.4 — UserService

**Files:**
- Create: `MissionClear.Api/Services/UserService.cs`
- Create: `MissionClear.Tests/Services/UserServiceTests.cs`

### Step 1: Testes (RED)

`MissionClear.Tests/Services/UserServiceTests.cs`

```csharp
using FluentAssertions;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using Moq;
using Xunit;

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

    private static UserEntity MakeUser(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Email = "u@test.com",
        DisplayName = "Test User",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Current@1"),
        Role = "Researcher"
    };

    private static MissionStatsProjection EmptyStats() =>
        new(0, 0, 0, 0, 0, 0, 0.0, 0.0, 0, null, []);

    // ── GetProfileAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetProfileAsync_ReturnsProfile_WithStats()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);

        _userRepo.Setup(r => r.FindByIdAsync(userId, default)).ReturnsAsync(user);
        _missionRepo.Setup(r => r.GetStatsByUserIdAsync(userId, default))
            .ReturnsAsync(new MissionStatsProjection(5, 3, 1, 1, 95, 40, 72.0, 47.0, 8, "ISS",
                new Dictionary<string, int> { { "ISS", 3 } }));

        var result = await _service.GetProfileAsync(userId, default);

        result.Role.Should().Be("Researcher");
        result.Stats.TotalMissions.Should().Be(5);
        result.Stats.FavoriteDestination.Should().Be("ISS");
    }

    [Fact]
    public async Task GetProfileAsync_Throws_USER_NOT_FOUND_WhenUserMissing()
    {
        _userRepo.Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((UserEntity?)null);

        var act = () => _service.GetProfileAsync(Guid.NewGuid(), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "USER_NOT_FOUND" && e.HttpStatus == 404);
    }

    // ── UpdateProfileAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProfileAsync_Throws_INVALID_CURRENT_PASSWORD_WhenCurrentPasswordWrong()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);

        _userRepo.Setup(r => r.FindByIdAsync(userId, default)).ReturnsAsync(user);
        _missionRepo.Setup(r => r.GetStatsByUserIdAsync(userId, default))
            .ReturnsAsync(EmptyStats());

        var act = () => _service.UpdateProfileAsync(userId,
            new UpdateUserRequest(null, "NewPass@1", "WrongCurrent@1"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CURRENT_PASSWORD" && e.HttpStatus == 401);
    }
}
```

### Step 2: Rodar testes (devem FALHAR)

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "UserServiceTests" -v normal
```

### Step 3: Implementação (GREEN)

`MissionClear.Api/Services/UserService.cs`

```csharp
using System.Text.RegularExpressions;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class UserService(IUserRepository userRepo, IMissionRepository missionRepo) : IUserService
{
    private static readonly Regex PasswordRegex =
        new(@"^(?=.*[A-Z])(?=.*\d).{8,}$", RegexOptions.Compiled);

    public async Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userRepo.FindByIdAsync(userId, ct)
            ?? throw new DomainException("USER_NOT_FOUND", "User not found.", 404);

        return await BuildProfileAsync(user, ct);
    }

    public async Task<UserProfileResponse> UpdateProfileAsync(
        Guid userId, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await userRepo.FindByIdAsync(userId, ct)
            ?? throw new DomainException("USER_NOT_FOUND", "User not found.", 404);

        if (request.Password is not null)
        {
            if (request.CurrentPassword is null ||
                !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                throw new DomainException("INVALID_CURRENT_PASSWORD", "Current password is incorrect.", 401);

            if (!PasswordRegex.IsMatch(request.Password))
                throw new DomainException("INVALID_PASSWORD_FORMAT",
                    "Password must be at least 8 characters with 1 uppercase and 1 digit.", 400);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        if (request.DisplayName is not null)
            user.DisplayName = request.DisplayName.Trim();

        await userRepo.UpdateAsync(user, ct);
        return await BuildProfileAsync(user, ct);
    }

    private async Task<UserProfileResponse> BuildProfileAsync(UserEntity user, CancellationToken ct)
    {
        var stats = await missionRepo.GetStatsByUserIdAsync(user.Id, ct);
        var successRate = stats.Total == 0 ? 0.0 : Math.Round((double)stats.Successful / stats.Total, 2);

        return new UserProfileResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Role,
            user.CreatedAt,
            new UserStatsDto(
                stats.Total,
                stats.Successful,
                stats.Failed,
                stats.Aborted,
                successRate,
                stats.BestScore,
                stats.Total == 0 ? 0 : (int)Math.Round(stats.AverageScore),
                stats.FavoriteDestination,
                Math.Round(stats.TotalDeltaV, 2)));
    }
}
```

### Step 4: Rodar testes (devem PASSAR)

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "UserServiceTests" -v normal
```

### Step 5: Commit

```powershell
git add MissionClear.Api/Services/UserService.cs MissionClear.Tests/Services/UserServiceTests.cs
git commit -m "feat(user): UserService GetProfile/UpdateProfile via IUserRepository + IMissionRepository"
```

---

## Task 4.5 — DI + Middleware JWT Bearer no Program.cs

### appsettings.Development.json (bloco Jwt)

```json
{
  "Jwt": {
    "Secret": "dev-only-secret-key-with-at-least-32-characters-long!!",
    "Issuer": "mission-clear-api",
    "Audience": "mission-clear-mobile",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 7
  }
}
```

> **Atenção:** `Jwt__Secret` deve ser sobrescrito via variável de ambiente em produção. Nunca commitar segredos reais.

### Program.cs — seção de DI e middleware

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MissionClear.Api.Configuration;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using System.Text;

// ── JWT setup ───────────────────────────────────────────────────────────────
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Seção Jwt ausente em appsettings.");

if (string.IsNullOrWhiteSpace(jwtSettings.Secret) || jwtSettings.Secret.Length < 32)
    throw new InvalidOperationException(
        "Jwt.Secret deve ter pelo menos 32 caracteres. Configure via env var Jwt__Secret.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "display_name",
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdministrator",
        policy => policy.RequireRole("Administrator"));
});

// ── Serviços de Auth e User ──────────────────────────────────────────────────
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();

// ── Pipeline (antes de MapControllers) ──────────────────────────────────────
app.UseAuthentication();
app.UseAuthorization();
```

### Step: Commit

```powershell
git add .
git commit -m "feat(auth): DI JwtService/AuthService/UserService + middleware JWT Bearer com RoleClaimType"
```

---

## Task 4.6 — Verificação Final

```powershell
# Rodar todos os testes de auth e user
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "JwtService|AuthService|UserService" -v normal

# Build completo sem warnings
dotnet build MissionClear.sln
```

---

## Success Criteria

- [ ] `IJwtService`, `IAuthService`, `IUserService` criados em `Services/Interfaces/`
- [ ] `JwtService.GenerateAccessToken` inclui claim `ClaimTypes.Role` com valor de `user.Role`
- [ ] `JwtService.GenerateRefreshToken` retorna string hexadecimal de 64 caracteres
- [ ] `JwtService.ValidateAccessToken` retorna `Guid?` userId do claim Sub
- [ ] Token para "Administrator" difere de token para "Researcher" (role claim distinto)
- [ ] `AuthService.RegisterAsync`: valida regex senha → verifica email único → BCrypt hash → cria usuário → retorna AuthResponse com role
- [ ] `AuthService.RegisterAsync`: rejeita email duplicado com `EMAIL_ALREADY_EXISTS` (409)
- [ ] `AuthService.RegisterAsync`: rejeita senha fraca com `INVALID_PASSWORD_FORMAT` (400)
- [ ] `AuthService.LoginAsync`: retorna AuthResponse com role; rejeita com `INVALID_CREDENTIALS` (401) para email OU senha errados (sem vazamento de existência)
- [ ] `AuthService.RefreshAsync`: revoga token antigo, cria novo, retorna novo access token; rejeita inválido com `INVALID_REFRESH_TOKEN` (401)
- [ ] `AuthService.LogoutAsync`: chama `RevokeByTokenAsync` (sem deletar)
- [ ] `UserService.GetProfileAsync`: retorna perfil com stats e role; rejeita com `USER_NOT_FOUND` (404)
- [ ] `UserService.UpdateProfileAsync`: exige current_password correto; rejeita com `INVALID_CURRENT_PASSWORD` (401)
- [ ] Senha plaintext nunca armazenada — apenas hash BCrypt
- [ ] AuthService e UserService injetam via interface (não implementação concreta)
- [ ] Middleware JWT Bearer ativo com `RoleClaimType = ClaimTypes.Role`
- [ ] `UseAuthentication()` antes de `UseAuthorization()` no pipeline
- [ ] `dotnet test` 100% verde nos três serviços (≥ 13 testes: 5 JwtService + 8 AuthService + 3 UserService)

## Risks & Mitigations

| Risco | Mitigação |
|---|---|
| `Jwt.Secret` fraco em produção | Validação no startup (≥ 32 chars); env var `Jwt__Secret` obrigatória |
| BCrypt workFactor lento em CI | Workfactor default (~10-12) aceitável; testes usam BCrypt diretamente |
| Refresh token vazado | Tokens têm `ExpiresAt` e `IsRevoked`; logout revoga imediatamente |
| Email case-sensitive duplicado | Normalizado para `ToLowerInvariant()` antes de gravar/buscar |
| `UserEntity.Id` como Guid vs string | `Id` é `Guid` no reboot — `ValidateAccessToken` retorna `Guid?` |
| AuthService acessa DbContext | Proibido: sempre via `IUserRepository` e `IRefreshTokenRepository` |
