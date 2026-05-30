using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MissionClear.Web.Services;

public sealed class ApiClient(HttpClient client, IHttpContextAccessor httpContextAccessor)
{
    private void AttachToken()
    {
        var token = httpContextAccessor.HttpContext?.User.FindFirst("access_token")?.Value;
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        else
            client.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        AttachToken();
        var response = await client.GetAsync(path);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<T>();
    }

    public async Task<(T? Data, string? Error, int StatusCode)> PostAsync<T>(string path, object body)
    {
        AttachToken();
        var response = await client.PostAsJsonAsync(path, body);
        var statusCode = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<T>(), null, statusCode);
        var errorBody = await response.Content.ReadAsStringAsync();
        return (default, errorBody, statusCode);
    }

    public async Task<bool> DeleteAsync(string path)
    {
        AttachToken();
        var response = await client.DeleteAsync(path);
        return response.IsSuccessStatusCode;
    }

    public async Task<LoginApiResponse?> LoginAsync(string email, string password)
    {
        var (data, _, _) = await PostAsync<LoginApiResponse>("/api/auth/login",
            new { email, password });
        return data;
    }

    public async Task<RegisterApiResponse?> RegisterAsync(string email, string password, string displayName)
    {
        var (data, _, _) = await PostAsync<RegisterApiResponse>("/api/auth/register",
            new { email, password, display_name = displayName });
        return data;
    }
}

// Minimal API response types — não compartilham os DTOs do Api project
// [JsonPropertyName] required: API returns snake_case but records use PascalCase
public sealed record LoginApiResponse(
    [property: JsonPropertyName("user")] LoginUserDto User,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public sealed record RegisterApiResponse(
    [property: JsonPropertyName("user")] LoginUserDto User,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public sealed record LoginUserDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("total_missions")] int? TotalMissions,
    [property: JsonPropertyName("best_score")] int? BestScore);
