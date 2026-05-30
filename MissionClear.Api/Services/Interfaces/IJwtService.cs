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
