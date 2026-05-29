# Evaluation Report — Mission Clear Plans Re-Audit (Post-Fix)
Date: 2026-05-28
Auditor: Senior Code Reviewer (independent re-audit)
Plans reviewed: plan-00 through plan-07 (8 files, plan-08-mvc-web not re-audited as no blockers were attributed to it)
Purpose: Verify resolution of the 13 BLOCKER issues and 10 MAJOR issues from the first evaluation.

---

## BLOCKER Verification Results

### B-01 — OrbitalObject uses LatitudeDeg/LongitudeDeg in plan-05

**Status: RESOLVED**

plan-02 Task 2.1 defines:
```csharp
public sealed record OrbitalObject(
    string Id, string Name, string Type,
    double Latitude, double Longitude, ...
```

plan-05 `ConjunctionDetector` implementation now reads:
```csharp
// OrbitalObject uses Latitude and Longitude (no "Deg" suffix)
var horizKm = OrbitalMath.HaversineKm(
    destLat, destLon,
    obj.Latitude, obj.Longitude, ...
```

plan-05 also corrects `MissionSseService` to use `obj.Latitude` and `obj.Longitude`. The Success
Criteria section in plan-05 explicitly states:
> "ConjunctionDetector reads obj.Latitude and obj.Longitude (NOT obj.LatitudeDeg/obj.LongitudeDeg)"
> "MissionSseService uses obj.Latitude/obj.Longitude for OrbitalObject (NOT obj.LatitudeDeg/obj.LongitudeDeg)"

---

### B-02 — MissionDestination missing LatitudeDeg/LongitudeDeg

**Status: RESOLVED**

plan-02 Task 2.2 now defines:
```csharp
public sealed record MissionDestination(
    string Id, string DisplayName,
    double AltitudeKm, double InclinationDeg,
    string Description, double DeltaVKmS, double MissionDurationHours, string Icon,
    double LatitudeDeg = 0.0,
    double LongitudeDeg = 0.0);
```

Both `LatitudeDeg` and `LongitudeDeg` are present with default values of 0.0. plan-05
`ConjunctionDetector` accesses `destination.LatitudeDeg` and `destination.LongitudeDeg`, which
now resolve. plan-05 Risks section confirms:
> "MissionDestination missing LatitudeDeg/LongitudeDeg — Phase 02 adds these properties defaulting
> to 0.0 for equatorial orbit assumption"

---

### B-03 — KnownDestinations.Get() missing in plan-02

**Status: RESOLVED**

plan-02 Task 2.2 now includes:
```csharp
public static MissionDestination? Get(string id) => FindById(id);
```

`Get` is an explicit alias for `FindById`. plan-05 `MissionSimulationService` calls
`KnownDestinations.Get(request.Destination)` which now resolves. The plan-05 Risks section
confirms: "KnownDestinations.Get(id) alias — Phase 02 adds Get as alias for FindById; both are
valid"

---

### B-04 — SimulateRequest type conflict (string vs DateTime fields)

**Status: RESOLVED**

A single canonical definition now exists. plan-02 Task 6.1 defines:
```csharp
public sealed record SimulateRequest(
    [Required] string Destination,
    DateTime DepartureUtc,
    DateTime ArrivalUtc);
```

plan-05 `MissionSimulationService.SimulateAsync` accesses `request.DepartureUtc` and
`request.ArrivalUtc`, consistent with the DateTime-typed definition. The compile-check test in
plan-02 Phase 9 instantiates:
```csharp
_ = new SimulateRequest("ISS", DateTime.UtcNow, DateTime.UtcNow.AddHours(6));
```
This is consistent with DateTime parameters. The string-field version is gone.

---

### B-05 — CompleteSessionRequest missing Status field

**Status: RESOLVED**

plan-02 Task 6.5 defines:
```csharp
public sealed record CompleteSessionRequest(
    [Required] string Status,
    bool SaveToHistory = false);
```

plan-05 `MissionSimulationService.CompleteSessionAsync` reads `request.Status` and the test:
```csharp
new CompleteSessionRequest(Status: "aborted", SaveToHistory: false)
```
Both compile against this definition. plan-05 Risks section confirms:
> "CompleteSessionRequest missing Status field — Canonical definition is
> (string Status, bool SaveToHistory = false) — always pass Status"

---

### B-06 — CompleteSessionResponse only 4 fields instead of 9

**Status: RESOLVED**

plan-02 Task 6.6 defines a 9-field record:
```csharp
public sealed record CompleteSessionResponse(
    string SessionId,
    string Status,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS,
    int ObstaclesEncountered,
    double DurationSeconds,
    bool SavedToHistory,
    string? MissionId);
```

plan-05 `MissionSimulationService.CompleteSessionAsync` returns all 9 fields:
```csharp
return new CompleteSessionResponse(
    SessionId: sessionId, Status: status, MissionScore: score,
    RiskScore: Math.Round(riskScore, 4), DeltaVKmS: destination.DeltaVKmS,
    ObstaclesEncountered: conjunctions.Count, DurationSeconds: duration,
    SavedToHistory: request.SaveToHistory && userId.HasValue, MissionId: missionId);
```
plan-05 Risks section confirms:
> "CompleteSessionResponse must have 9 fields — Canonical: SessionId, Status, MissionScore,
> RiskScore, DeltaVKmS, ObstaclesEncountered, DurationSeconds, SavedToHistory, MissionId"

---

### B-07 — SimulateResponse.Obstacles uses ConjunctionResult instead of ObstacleDto

**Status: RESOLVED**

plan-02 Task 6.2 defines:
```csharp
public sealed record ObstacleDto(
    string DebrisId, string DebrisName,
    double ClosestApproachKm, string TimeOfClosestApproach, string RiskLevel);

public sealed record SimulateResponse(
    string SessionId, string Destination,
    DateTime DepartureUtc, DateTime ArrivalUtc,
    IReadOnlyList<object> Trajectory,
    IReadOnlyList<ObstacleDto> Obstacles,
    int MissionScore, double RiskScore);
```

plan-05 `MissionSimulationService.SimulateAsync` maps domain results to `ObstacleDto` before
constructing the response:
```csharp
var obstaclesDto = conjunctions.Select(c => new ObstacleDto(
    DebrisId: c.ObjectId,
    ClosestApproachKm: c.ClosestApproachKm,
    TimeOfClosestApproach: c.TimeOfClosestApproach.ToString("O"),
    RiskLevel: c.RiskLevel.ToString().ToLowerInvariant()
)).ToList().AsReadOnly();
```

Note: there is a residual issue here — plan-05 references `c.ObjectId` and omits `DebrisName`,
while plan-02's `ObstacleDto` has a `DebrisName` parameter. This is a MAJOR-level concern but not
a blocker since `ConjunctionResult` vs `ObstacleDto` as the list element type is resolved — the
blocker about using `ConjunctionResult` directly is closed. The `c.ObjectId` vs `c.DebrisId`
field naming is a NEW issue found during this re-audit (see Additional Finding AF-01 below).

---

### B-08 — SaveMissionAsync uses SaveMissionRequest record instead of 11 params

**Status: RESOLVED**

plan-06 defines `IMissionHistoryService.SaveMissionAsync` with 11 positional parameters:
```csharp
Task<MissionSummaryDto> SaveMissionAsync(
    Guid userId, string sessionId, string status,
    double riskScore, double deltaV, int score, int obstacles,
    DateTime departure, DateTime arrival, string destination,
    IReadOnlyList<object> obstaclesData,
    CancellationToken ct = default);
```

plan-05 `MissionSimulationService.CompleteSessionAsync` calls it with 11 individual arguments:
```csharp
var summary = await _history.SaveMissionAsync(
    userId: userId!.Value, sessionId: sessionId, status: status,
    riskScore: riskScore, deltaV: destination.DeltaVKmS, score: score,
    obstacles: conjunctions.Count, departure: session.StartedAt, arrival: DateTime.UtcNow,
    destination: session.Destination,
    obstaclesData: conjunctions.Cast<object>().ToList().AsReadOnly(),
    ct: ct);
```

No `SaveMissionRequest` wrapper type is used. plan-05 Success Criteria confirms:
> "MissionSimulationService.CompleteSessionAsync calls SaveMissionAsync with 11 positional
> parameters (no wrapper type)"

However, note: `session.StartedAt` is referenced here but plan-02's `MissionSession` defines the
field as `DepartureTime` (not `StartedAt`). See Additional Finding AF-02.

---

### B-09 — MissionSession missing UserId and CreatedAtUtc

**Status: RESOLVED**

plan-02 Task 2.5 defines:
```csharp
public sealed class MissionSession
{
    public string SessionId { get; init; } = $"sess_{Guid.NewGuid():N}";
    public required string Destination { get; init; }
    public required DateTime DepartureTime { get; init; }
    public required DateTime ArrivalTime { get; init; }
    public DateTime ExpiresAt { get; init; } = DateTime.UtcNow.AddMinutes(30);
    public required Guid UserId { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    ...
}
```

Both `UserId` (as `required Guid`) and `CreatedAtUtc` are present. plan-05 `SessionStore` tests
use `UserId = null` but the field is now `required Guid` — see Additional Finding AF-03.

---

### B-10 — DomainException defined in both plan-02 and plan-04

**Status: RESOLVED**

plan-04 Task 4.0 now explicitly states:
> "IMPORTANT: DomainException is defined in plan-02 Task 1.1. Do NOT create this file here.
> Execute plan-02 Task 1.1 first, or verify MissionClear.Api/Exceptions/DomainException.cs
> already exists."

plan-04 is no longer a source of `DomainException` creation. plan-02 is the single authority.

---

### B-11 — OrbitalObject defined in both plan-02 and plan-03

**Status: RESOLVED**

plan-03 Task 3.1 now explicitly states:
> "IMPORTANT: OrbitalObject is defined in plan-02 Task 2.1. Do NOT create this file here.
> Execute plan-02 Task 2.1 first, or verify MissionClear.Api/Models/OrbitalObject.cs already
> exists before proceeding."

plan-03 lists the property names it expects to use (confirming `Latitude`/`Longitude` without
"Deg" suffix) but does not create the file. plan-02 is the single authority.

---

### B-12 — Aspire.Pomelo.EntityFrameworkCore.MySql version conflict

**Status: RESOLVED**

plan-00 Task 5 Step 1 now specifies:
```xml
<PackageReference Include="Aspire.Pomelo.EntityFrameworkCore.MySql" Version="9.1.0" />
<PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.2" />
```

plan-01 Task 1.1 instructs:
```
dotnet add package Aspire.Pomelo.EntityFrameworkCore.MySql --version 9.1.0
```

Both plans now reference version 9.1.0. The conflict between 9.1.0 and 8.2.2 is gone.

Note: The actual committed `MissionClear.Api.csproj` on disk still uses SQLite (`Microsoft.
EntityFrameworkCore.Sqlite Version="8.0.10"`) and contains neither Pomelo nor Aspire packages.
This means plan-01 has not yet been executed against the codebase, but the plan-to-plan version
conflict is resolved at the planning level.

---

### B-13 — DataAggregatorService internal field inaccessible from test assembly

**Status: RESOLVED**

plan-03 Task 3.3 Step 0 now explicitly prescribes:
> "Create MissionClear.Api/Properties/AssemblyInfo.cs:"
```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("MissionClear.Tests")]
```
> "This allows DataAggregatorServiceTests to access internal fields for white-box testing."

The commit in plan-03 Step 5 includes `MissionClear.Api/Properties/AssemblyInfo.cs`. The
checklist item confirms: `MissionClear.Api/Properties/AssemblyInfo.cs` created with
`[assembly: InternalsVisibleTo("MissionClear.Tests")]`.

---

## BLOCKER Summary

| # | Status |
|---|--------|
| B-01 | RESOLVED |
| B-02 | RESOLVED |
| B-03 | RESOLVED |
| B-04 | RESOLVED |
| B-05 | RESOLVED |
| B-06 | RESOLVED |
| B-07 | RESOLVED (with residual AF-01) |
| B-08 | RESOLVED (with residual AF-02) |
| B-09 | RESOLVED (with residual AF-03) |
| B-10 | RESOLVED |
| B-11 | RESOLVED |
| B-12 | RESOLVED |
| B-13 | RESOLVED |

**FINAL VERDICT: PASS — All 13 original BLOCKER issues are resolved at the plan level.**

---

## MAJOR Issues Verification (M-01 through M-10)

### M-01 — RoleClaimType mismatch between plan-00 and plan-04

**Status: STILL-OPEN (PARTIAL)**

plan-04 Task 4.5 uses `RoleClaimType = System.Security.Claims.ClaimTypes.Role` (URI form).
plan-00 Program.cs stub uses `RoleClaimType = "role"` (plain string). The two Program.cs versions
remain inconsistent. Workers applying plan-00 first, then plan-04, may end up with the plan-04
version (which is correct). However, the plan-00 csproj stub is authoritative for the initial
scaffolding and may be applied last, overwriting plan-04's fix. No single plan is explicitly
designated as the final Program.cs authority for this setting.

### M-02 — UserInAuthResponse.Id missing usr_ prefix

**Status: RESOLVED**

plan-04 `AuthService.BuildAuthResponseAsync` now formats:
```csharp
new UserInAuthResponse($"usr_{user.Id:N}", user.Email, user.DisplayName, user.Role,
    user.CreatedAt.ToString("O"))
```
The `usr_` prefix is applied.

### M-03 — UserEntity.CreatedAt passed as DateTime instead of string

**Status: RESOLVED**

plan-04 `AuthService.BuildAuthResponseAsync` now calls `user.CreatedAt.ToString("O")`, producing
a string for `UserInAuthResponse.CreatedAt`. The type mismatch is fixed.

### M-04 — LaunchWindow constructor named argument mismatch (StartUtc/EndUtc)

**Status: RESOLVED**

plan-05 `LaunchWindowCalculator` now uses:
```csharp
windows.Add(new LaunchWindow(
    Start:         current,
    End:           slotEnd, ...
```
The note in plan-05 Phase 3 explicitly confirms: "Use Start: and End: (not StartUtc:/EndUtc:).
These are the canonical parameter names as defined in Phase 02's LaunchWindow record."

### M-05 — RiskScoring.MaxRadiusKm not defined

**Status: RESOLVED (by redesign)**

plan-05 `ConjunctionDetector` no longer references `RiskScoring.MaxRadiusKm`. The 200 km filter
is implemented inline:
```csharp
if (distKm > 200) continue; // only process debris within 200 km
```
The constant is not needed because the filtering is done directly in `ConjunctionDetector`.

### M-06 — appsettings.json JWT Issuer/Audience conflict

**Status: RESOLVED**

plan-00 Task 5 Step 3 sets `"Issuer": "mission-clear-api"` and `"Audience":
"mission-clear-mobile"`. plan-04 Task 4.5 uses `"Issuer": "mission-clear-api"` and `"Audience":
"mission-clear-mobile"` in `appsettings.Development.json`. The values are now consistent.

### M-07 — OrbitalSettings.SessionTtlMinutes missing

**Status: STILL-OPEN**

plan-00 `appsettings.json` uses `Sessions:TtlMinutes` as the config key. plan-05 DI registration
reads `OrbitalSettings:SessionTtlMinutes`. The `OrbitalSettings` POCO (from plan-00 Configuration
context) does not define `SessionTtlMinutes`. The `GetValue<int>("OrbitalSettings:
SessionTtlMinutes", 30)` call will silently use the default 30, never binding from configuration.
plan-05 instructs adding `SessionTtlMinutes` to `appsettings.json` under `OrbitalSettings` but
does not update the `OrbitalSettings` POCO class itself.

### M-08 — OrbitalMath.HaversineKm 5-argument overload unverified

**Status: RESOLVED (by evidence in plan-03)**

plan-03 `OrbitalCache` tests and plan-05 `ConjunctionDetector` both call
`OrbitalMath.HaversineKm(lat1, lon1, lat2, lon2, radiusKm)` with 5 arguments. The constant
`OrbitalMath.EarthRadiusKm` is referenced. plan-03 names this constant explicitly in the
`Naming Alignment` section. The existing helpers from plan-00's scaffolding context include
`OrbitalMath`. Since the existing commit `7f144f1` (feat(helpers): add OrbitalMath, RiskScoring,
MissionScoring) is in the git history, workers need to verify the actual signature, but the plans
are internally consistent in assuming this overload exists.

### M-09 — DashboardService returns empty display_name

**Status: STILL-OPEN**

plan-06 `DashboardService.GetSummaryAsync` signature was updated to accept `displayName`:
```csharp
public async Task<DashboardSummaryResponse> GetSummaryAsync(
    Guid? userId, string? displayName = null, CancellationToken ct = default)
```
However, plan-07 controllers (not re-audited in full here) would need to extract the claim and
pass it. This is partially resolved at the service level but depends on plan-07 controller
implementation passing the display name from JWT claims.

### M-10 — IRefreshTokenRepository.CreateAsync return type incompatibility with Moq

**Status: STILL-OPEN**

plan-01 defines `IRefreshTokenRepository.CreateAsync` as `Task` (void). plan-04 test mock uses:
```csharp
_tokenRepo.Setup(r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), default))
    .Returns(Task.CompletedTask);
```
The fix uses `.Returns(Task.CompletedTask)` (not `.ReturnsAsync(...)`) which IS compatible with
a void-returning Task. This is actually correct Moq usage for `Task`-returning methods. Examining
plan-04's test code more carefully shows `Returns(Task.CompletedTask)`, not `ReturnsAsync`.
This issue is **RESOLVED** — the Moq setup is compatible.

**Updated M-10 Status: RESOLVED**

---

## MAJOR Summary

| # | Status |
|---|--------|
| M-01 | STILL-OPEN |
| M-02 | RESOLVED |
| M-03 | RESOLVED |
| M-04 | RESOLVED |
| M-05 | RESOLVED |
| M-06 | RESOLVED |
| M-07 | STILL-OPEN |
| M-08 | RESOLVED |
| M-09 | STILL-OPEN |
| M-10 | RESOLVED |

**7 of 10 MAJOR issues resolved. 3 remain open (M-01, M-07, M-09).**

---

## Additional Findings (NEW — discovered during re-audit)

### AF-01 — ConjunctionResult.ObjectId vs DebrisId field name mismatch (MAJOR)

plan-05 `MissionSimulationService.SimulateAsync` maps:
```csharp
var obstaclesDto = conjunctions.Select(c => new ObstacleDto(
    DebrisId: c.ObjectId, ...
```

But plan-02 `ConjunctionResult` is defined as:
```csharp
public sealed record ConjunctionResult(
    string DebrisId, string DebrisName,
    double ClosestApproachKm, DateTime TimeOfClosestApproach, RiskLevel RiskLevel);
```

The field is `DebrisId` on `ConjunctionResult`, not `ObjectId`. `c.ObjectId` will cause a compile
error. Additionally, `ObstacleDto` takes 5 arguments (DebrisId, DebrisName, ClosestApproachKm,
TimeOfClosestApproach, RiskLevel) but the call site in plan-05 only passes 4, omitting
`DebrisName`.

**Severity: BLOCKER (compile error)**

### AF-02 — MissionSession.StartedAt does not exist (MAJOR)

plan-05 `MissionSimulationService.CompleteSessionAsync` references `session.StartedAt`:
```csharp
departure: session.StartedAt,
```
But plan-02 `MissionSession` defines `DepartureTime`, not `StartedAt`. This is a compile error.
Likewise, `CreateSessionAsync` builds:
```csharp
var session = new MissionSession(
    Id: sessionId, Destination: destination.Id,
    StartedAt: DateTime.UtcNow, ExpiresAt: DateTime.UtcNow.AddMinutes(30),
    UserId: Guid.Empty, CreatedAtUtc: DateTime.UtcNow);
```
`MissionSession` in plan-02 is a class with `init`-only properties, not a positional record —
it does not have a primary constructor with parameters like `Id:`, `StartedAt:`, `Destination:`.
The entire `CreateSessionAsync` construction call is incompatible with plan-02's `MissionSession`
definition.

**Severity: BLOCKER (compile error)**

### AF-03 — SessionStore tests use UserId = null but MissionSession.UserId is required Guid (MAJOR)

plan-05 `SessionStoreTests` instantiates:
```csharp
private static MissionSession NewSession(string id = "sess_test") => new()
{
    SessionId    = id,
    UserId       = null,  // <-- compile error: Guid is non-nullable
    ...
```

plan-02 defines `UserId` as `required Guid` (non-nullable value type). Assigning `null` to a
non-nullable `Guid` is a compile error in a nullable-enabled project.

**Severity: BLOCKER (compile error)**

### AF-04 — SessionRequest DTO field name mismatch (MAJOR)

plan-02 Task 6.3 defines:
```csharp
public sealed record SessionRequest(
    [Required] string Destination,
    [Required] string DepartureTime,
    [Required] string ArrivalTime);
```

But plan-05 `MissionSimulationService.CreateSessionAsync` is called in tests with:
```csharp
new SessionRequest("ISS", DateTime.UtcNow, DateTime.UtcNow.AddHours(6))
```
(passing DateTime values for DepartureTime/ArrivalTime). And plan-05's own DTO note specifies:
```csharp
// SessionRequest.cs
public sealed record SessionRequest(string Destination, DateTime DepartureUtc, DateTime ArrivalUtc);
```

Two different `SessionRequest` definitions exist: plan-02 uses string fields named
`DepartureTime`/`ArrivalTime`, plan-05 uses DateTime fields named `DepartureUtc`/`ArrivalUtc`.
This same conflict as the original B-04 was applied to `SimulateRequest` but not to
`SessionRequest`.

**Severity: BLOCKER (compile error)**

---

## Updated Blocker Count

| Source | Count |
|--------|-------|
| Original 13 blockers | 13 RESOLVED |
| New blockers found (AF-01, AF-02, AF-03, AF-04) | 4 NEW OPEN |

---

## Final Verdict

**FAIL**

All 13 original blocker issues are resolved at the plan level. However, 4 new BLOCKER-severity
issues were introduced during the fix process (AF-01, AF-02, AF-03, AF-04). These all originate
from incompatibilities between plan-05's `MissionSimulationService` implementation and the models
defined in plan-02.

The fixes for B-07 and B-08 were partially correct but introduced a new surface of compile errors:
`c.ObjectId` vs `c.DebrisId`, `MissionSession` constructed as a positional record when it is a
class, `session.StartedAt` when the field is `DepartureTime`, and `SessionRequest` having
DateTime vs string fields.

Before implementation, plan-05 `MissionSimulationService.CreateSessionAsync` and the mapping code
in `SimulateAsync` must be updated to match plan-02's actual type definitions.

**Required fixes before implementation can proceed:**
1. AF-01: Replace `c.ObjectId` with `c.DebrisId`; add `DebrisName: c.DebrisName` to `ObstacleDto` constructor call in `SimulateAsync`
2. AF-02: Replace `session.StartedAt` with `session.DepartureTime`; rewrite `CreateSessionAsync` session construction using object initializer syntax matching plan-02's `MissionSession` class (not positional constructor)
3. AF-03: Change `UserId = null` to `UserId = Guid.NewGuid()` in `SessionStoreTests.NewSession`
4. AF-04: Align `SessionRequest` DTO — pick one canonical definition (recommend DateTime fields with `DepartureUtc`/`ArrivalUtc` to match `SimulateRequest`) and apply it consistently in plan-02 Task 6.3 and plan-05

---

## What Was Done Well

- The original 13 blockers were carefully and thoroughly addressed. The explicit notes, warning
  boxes, and risk table entries in the revised plans show that the author understood each issue.
- B-10 (DomainException duplication) and B-11 (OrbitalObject duplication) were fixed with the
  correct pattern: one plan is designated authority, the other references it explicitly.
- B-13 (`InternalsVisibleTo`) was correctly placed in plan-03 Task 3.3 Step 0 before the test
  code that requires it.
- The `KnownDestinations.Get()` alias (B-03) was added cleanly as a one-liner delegate.
- `CompleteSessionResponse` was expanded to all 9 fields (B-06) and `CompleteSessionRequest`
  gained its `Status` field (B-05) consistently across both plan-02 and plan-05.
