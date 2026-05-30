using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

public interface ISessionStore
{
    void Set(MissionSession session);
    MissionSession? Get(string sessionId);
    void Remove(string sessionId);
    void PurgeExpired();
}
