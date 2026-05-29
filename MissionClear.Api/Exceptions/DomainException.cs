namespace MissionClear.Api.Exceptions;

/// <summary>
/// Exceção de domínio com código de erro e HTTP status.
/// Capturada pelo GlobalExceptionMiddleware e serializada como ApiErrorDto.
/// </summary>
public sealed class DomainException(string errorCode, string message, int httpStatus = 400)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int HttpStatus { get; } = httpStatus;
}
