namespace MissionClear.Api.Dtos.Common;

public sealed record ApiErrorDto(string Error, string Message, string Timestamp)
{
    public static ApiErrorDto From(string code, string message) =>
        new(code, message, DateTime.UtcNow.ToString("O"));

    // Auth
    public static ApiErrorDto EmailAlreadyExists() =>
        From("EMAIL_ALREADY_EXISTS", "Este email já está cadastrado.");
    public static ApiErrorDto InvalidCredentials() =>
        From("INVALID_CREDENTIALS", "Email ou senha incorretos.");
    public static ApiErrorDto TokenExpired() =>
        From("TOKEN_EXPIRED", "Token de acesso expirado. Use o refresh token.");
    public static ApiErrorDto InvalidRefreshToken() =>
        From("INVALID_REFRESH_TOKEN", "Refresh token inválido ou revogado.");
    public static ApiErrorDto Unauthorized() =>
        From("UNAUTHORIZED", "Rota requer autenticação.");
    public static ApiErrorDto InvalidPasswordFormat() =>
        From("INVALID_PASSWORD_FORMAT", "Senha deve ter no mínimo 8 caracteres, 1 maiúscula e 1 número.");
    public static ApiErrorDto InvalidCurrentPassword() =>
        From("INVALID_CURRENT_PASSWORD", "Senha atual incorreta.");

    // Acesso
    public static ApiErrorDto Forbidden() =>
        From("FORBIDDEN", "Você não tem permissão para acessar este recurso.");

    // Not found
    public static ApiErrorDto DebrisNotFound(string id) =>
        From("DEBRIS_NOT_FOUND", $"Debris '{id}' não encontrado no cache.");
    public static ApiErrorDto MissionNotFound(string id) =>
        From("MISSION_NOT_FOUND", $"Missão '{id}' não encontrada.");
    public static ApiErrorDto SessionNotFound(string id) =>
        From("SESSION_NOT_FOUND", $"Sessão '{id}' expirada ou não encontrada.");

    // Conflito
    public static ApiErrorDto SessionAlreadyCompleted() =>
        From("SESSION_ALREADY_COMPLETED", "Esta sessão já foi finalizada.");

    // Validação orbital
    public static ApiErrorDto InvalidDestination(string id) =>
        From("INVALID_DESTINATION", $"Destino '{id}' não é suportado. Use ISS, LEO_GENERIC ou SSO.");
    public static ApiErrorDto TimeRangeExceeded() =>
        From("TIME_RANGE_EXCEEDED", "Período solicitado excede o limite de 48 horas.");
    public static ApiErrorDto InvalidTimeRange() =>
        From("INVALID_TIME_RANGE", "arrival_time deve ser posterior a departure_time.");
    public static ApiErrorDto MissingParameter(string param) =>
        From("MISSING_PARAMETER", $"Parâmetro obrigatório ausente: '{param}'.");
    public static ApiErrorDto InvalidDateFormat(string param) =>
        From("INVALID_DATE_FORMAT", $"Parâmetro '{param}' não está em formato ISO 8601 UTC.");

    // Sistema
    public static ApiErrorDto CacheNotReady() =>
        From("CACHE_NOT_READY", "Cache orbital ainda está inicializando. Tente novamente em alguns segundos.");
    public static ApiErrorDto InternalError() =>
        From("INTERNAL_ERROR", "Erro interno do servidor. Tente novamente.");
}
