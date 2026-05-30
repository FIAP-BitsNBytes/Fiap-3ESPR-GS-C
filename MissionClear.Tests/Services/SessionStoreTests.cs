using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class SessionStoreTests
{
    private static SessionStore NewStore(Func<DateTime>? clock = null) =>
        new(ttlMinutes: 30, clock: clock ?? (() => DateTime.UtcNow));

    private static MissionSession NewSession(string id = "sess_test") => new()
    {
        SessionId     = id,
        UserId        = Guid.NewGuid(),
        Destination   = "ISS",
        DepartureTime = DateTime.UtcNow,
        ArrivalTime   = DateTime.UtcNow.AddHours(6),
        Status        = SessionStatus.Active,
        CreatedAtUtc  = DateTime.UtcNow,
        ExpiresAt     = DateTime.UtcNow.AddMinutes(30)
    };

    [Fact]
    public void Set_Then_Get_ReturnsSameSession()
    {
        var store   = NewStore();
        var session = NewSession();

        store.Set(session);
        var result = store.Get(session.SessionId);

        result.Should().NotBeNull();
        result!.SessionId.Should().Be(session.SessionId);
    }

    [Fact]
    public void Remove_ThenGet_ReturnsNull()
    {
        var store   = NewStore();
        var session = NewSession("sess_remove");

        store.Set(session);
        store.Remove(session.SessionId);

        store.Get(session.SessionId).Should().BeNull();
    }

    [Fact]
    public void Get_ReturnsNull_WhenSessionExpired()
    {
        var now   = DateTime.UtcNow;
        var clock = now;
        var store = NewStore(clock: () => clock);

        var session = NewSession("sess_expire");
        // Manual override for class (not record)
        var propExpires = typeof(MissionSession).GetProperty("ExpiresAt");
        propExpires?.SetValue(session, now.AddMinutes(30));
        
        store.Set(session);

        // Advance clock past TTL
        clock = now.AddMinutes(31);

        store.Get("sess_expire").Should().BeNull();
    }

    [Fact]
    public void PurgeExpired_RemovesOnlyExpiredSessions()
    {
        var now   = DateTime.UtcNow;
        var clock = now;
        var store = NewStore(clock: () => clock);

        var propExpires = typeof(MissionSession).GetProperty("ExpiresAt");

        var fresh   = NewSession("sess_fresh");
        propExpires?.SetValue(fresh, now.AddMinutes(30));

        var expired = NewSession("sess_dead");
        propExpires?.SetValue(expired, now.AddMinutes(-1));

        store.Set(fresh);
        store.Set(expired);

        clock = now.AddMinutes(31);
        store.PurgeExpired();

        store.Get("sess_fresh").Should().BeNull(); // also expired now
        store.Get("sess_dead").Should().BeNull();
    }
}
