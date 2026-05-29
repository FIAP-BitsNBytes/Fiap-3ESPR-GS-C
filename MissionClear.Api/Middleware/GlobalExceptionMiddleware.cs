using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Exceptions;
using System.Text.Json;

namespace MissionClear.Api.Middleware;

public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException ex)
        {
            logger.LogWarning("Domain error {Code} (HTTP {Status}): {Message}",
                ex.ErrorCode, ex.HttpStatus, ex.Message);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode    = ex.HttpStatus;
                context.Response.ContentType   = "application/json";
                var error = new ApiErrorDto(
                    ex.ErrorCode,
                    ex.Message,
                    DateTime.UtcNow.ToString("O"));
                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(error, JsonOptions));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected unhandled exception");

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode    = 500;
                context.Response.ContentType   = "application/json";
                // Never expose ex.Message or stack trace in production response
                var error = new ApiErrorDto(
                    "INTERNAL_ERROR",
                    "An unexpected error occurred.",
                    DateTime.UtcNow.ToString("O"));
                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(error, JsonOptions));
            }
        }
    }
}