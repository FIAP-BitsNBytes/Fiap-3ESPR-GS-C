using Microsoft.AspNetCore.Http;

namespace MissionClear.Api.Services.Interfaces;

public interface IMissionSseService
{
    Task StreamAsync(string sessionId, HttpResponse response, CancellationToken ct);
}
