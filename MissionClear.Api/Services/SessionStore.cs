using System.Collections.Concurrent;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

/// <summary>
/// Thread-safe in-memory session store backed by ConcurrentDictionary.
/// TTL is enforced on reads and by explicit PurgeExpired calls.
/// Singleton lifetime — shared across all requests.
/// </summary>
public sealed class SessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, MissionSession> _sessions = new();
    private readonly int _ttlMinutes;
    private readonly Func<DateTime> _clock;

    /// <summary>Production constructor — reads TTL from OrbitalSettings.</summary>
    public SessionStore(int ttlMinutes = 30)
        : this(ttlMinutes, () => DateTime.UtcNow) { }

    /// <summary>Test constructor — injects a controllable clock.</summary>
    public SessionStore(int ttlMinutes, Func<DateTime> clock)
    {
        _ttlMinutes = ttlMinutes;
        _clock      = clock;
    }

    public void Set(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sessions[session.SessionId] = session;
    }

    public MissionSession? Get(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var s)) return null;
        if (_clock() >= s.ExpiresAt)
        {
            _sessions.TryRemove(sessionId, out _);
            return null;
        }
        return s;
    }

    public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public void PurgeExpired()
    {
        var now = _clock();
        foreach (var kv in _sessions)
        {
            if (now >= kv.Value.ExpiresAt)
                _sessions.TryRemove(kv.Key, out _);
        }
    }
}
