using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Dtos.Dashboard;
using MissionClear.Api.Dtos.Destination;
using MissionClear.Api.Dtos.History;
using MissionClear.Api.Dtos.Mission;
using MissionClear.Api.Dtos.Orbital;
using MissionClear.Api.Dtos.Status;
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models;
using FluentAssertions;
using Xunit;

namespace MissionClear.Tests.Models;

public class DtoCompileTests
{
    [Fact]
    public void DomainException_stores_error_code_and_http_status()
    {
        var ex = new DomainException("EMAIL_ALREADY_EXISTS", "Email já cadastrado.", 409);
        ex.ErrorCode.Should().Be("EMAIL_ALREADY_EXISTS");
        ex.HttpStatus.Should().Be(409);
        ex.Message.Should().Be("Email já cadastrado.");
    }

    [Fact]
    public void KnownDestinations_values_match_api_contract_section4()
    {
        KnownDestinations.ISS.Id.Should().Be("ISS");
        KnownDestinations.ISS.AltitudeKm.Should().Be(408);
        KnownDestinations.ISS.InclinationDeg.Should().Be(51.6);
        KnownDestinations.ISS.DeltaVKmS.Should().Be(9.40);
        KnownDestinations.ISS.MissionDurationHours.Should().Be(6.2);

        KnownDestinations.LeoGeneric.Id.Should().Be("LEO_GENERIC");
        KnownDestinations.LeoGeneric.AltitudeKm.Should().Be(400);
        KnownDestinations.LeoGeneric.InclinationDeg.Should().Be(28.5);
        KnownDestinations.LeoGeneric.DeltaVKmS.Should().Be(9.20);
        KnownDestinations.LeoGeneric.MissionDurationHours.Should().Be(5.8);

        KnownDestinations.Sso.Id.Should().Be("SSO");
        KnownDestinations.Sso.AltitudeKm.Should().Be(500);
        KnownDestinations.Sso.InclinationDeg.Should().Be(97.4);
        KnownDestinations.Sso.DeltaVKmS.Should().Be(10.10);
        KnownDestinations.Sso.MissionDurationHours.Should().Be(7.0);

        KnownDestinations.All.Should().HaveCount(3);
    }

    [Fact]
    public void KnownDestinations_FindById_is_case_insensitive()
    {
        KnownDestinations.FindById("iss").Should().NotBeNull();
        KnownDestinations.FindById("ISS").Should().NotBeNull();
        KnownDestinations.FindById("leo_generic").Should().NotBeNull();
        KnownDestinations.FindById("MARS").Should().BeNull();
    }

    [Fact]
    public void MissionSession_has_sess_prefix_and_30min_ttl()
    {
        var session = new MissionSession
        {
            Destination = "ISS",
            DepartureTime = DateTime.UtcNow,
            ArrivalTime = DateTime.UtcNow.AddHours(6),
            UserId = Guid.NewGuid()
        };
        session.SessionId.Should().StartWith("sess_");
        session.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromSeconds(5));
        session.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void ApiErrorDto_factory_methods_produce_correct_codes()
    {
        ApiErrorDto.EmailAlreadyExists().Error.Should().Be("EMAIL_ALREADY_EXISTS");
        ApiErrorDto.InvalidCredentials().Error.Should().Be("INVALID_CREDENTIALS");
        ApiErrorDto.TokenExpired().Error.Should().Be("TOKEN_EXPIRED");
        ApiErrorDto.InvalidRefreshToken().Error.Should().Be("INVALID_REFRESH_TOKEN");
        ApiErrorDto.Unauthorized().Error.Should().Be("UNAUTHORIZED");
        ApiErrorDto.Forbidden().Error.Should().Be("FORBIDDEN");
        ApiErrorDto.DebrisNotFound("X").Error.Should().Be("DEBRIS_NOT_FOUND");
        ApiErrorDto.MissionNotFound("X").Error.Should().Be("MISSION_NOT_FOUND");
        ApiErrorDto.SessionNotFound("X").Error.Should().Be("SESSION_NOT_FOUND");
        ApiErrorDto.SessionAlreadyCompleted().Error.Should().Be("SESSION_ALREADY_COMPLETED");
        ApiErrorDto.InvalidDestination("X").Error.Should().Be("INVALID_DESTINATION");
        ApiErrorDto.TimeRangeExceeded().Error.Should().Be("TIME_RANGE_EXCEEDED");
        ApiErrorDto.InvalidTimeRange().Error.Should().Be("INVALID_TIME_RANGE");
        ApiErrorDto.MissingParameter("p").Error.Should().Be("MISSING_PARAMETER");
        ApiErrorDto.InvalidDateFormat("p").Error.Should().Be("INVALID_DATE_FORMAT");
        ApiErrorDto.InvalidPasswordFormat().Error.Should().Be("INVALID_PASSWORD_FORMAT");
        ApiErrorDto.InvalidCurrentPassword().Error.Should().Be("INVALID_CURRENT_PASSWORD");
        ApiErrorDto.CacheNotReady().Error.Should().Be("CACHE_NOT_READY");
        ApiErrorDto.InternalError().Error.Should().Be("INTERNAL_ERROR");
    }

    [Fact]
    public void PaginationDto_computes_total_pages()
    {
        var p = PaginationDto.From(1, 20, 45);
        p.TotalPages.Should().Be(3);
        p.Total.Should().Be(45);
    }

    [Fact]
    public void All_dto_types_instantiate_without_error()
    {
        // Auth
        _ = new RegisterRequest("a@b.com", "Pass1234!", "Name");
        _ = new LoginRequest("a@b.com", "pass");
        _ = new RefreshRequest("tok");
        _ = new LogoutRequest("tok");
        _ = new UserInAuthResponse("usr_1", "e", "n", "Researcher", DateTime.UtcNow.ToString("O"));
        _ = new AuthResponse(new UserInAuthResponse("usr_1", "e", "n", "Researcher", "2025-01-01T00:00:00Z"), "at", "rt", 3600);
        _ = new RefreshTokenResponse("at", 3600);

        // User
        _ = new UserStatsDto(0, 0, 0, 0, 0, 0, 0, null, 0);
        _ = new UserProfileResponse("usr_1", "e", "n", "Researcher", "2025-01-01T00:00:00Z", new UserStatsDto(0,0,0,0,0,0,0,null,0));
        _ = new UpdateUserRequest(null, null, null);

        // Orbital
        _ = new DebrisDto("1","n","debris",0,0,400,7.5,"celestrak","2025-01-01T00:00:00Z");
        _ = new TleDto("ep", "l1", "l2");
        _ = new OrbitParamsDto(74, 0.004, 97, 800, 750);
        _ = new DebrisDetailDto("1","n","debris",0,0,400,7.5,"celestrak","2025-01-01T00:00:00Z", null, null);
        _ = new ByTypeDto(100, 50, 20);
        _ = new ByAltitudeBandDto(80, 60, 40);
        _ = new SourcesDto(200, 0);
        _ = new DebrisStatsDto(200, new ByTypeDto(0,0,0), new ByAltitudeBandDto(0,0,0), new SourcesDto(0,0), "2025-01-01T00:00:00Z");

        // Common
        _ = new ConjunctionDto("1","n",5.0,"2025-01-01T00:00:00Z","high");
        _ = new LaunchWindowDto("s","e",0.01,9.4,6.2,true,[]);
        _ = new LaunchWindowsResponse("ISS","s","e",48,41,[]);
        _ = new BestWindowDto(1,"s","e",0.01,9.4,6.2,[]);
        _ = new BestWindowsResponse("ISS","s","e",[]);

        // Status
        _ = new SourceStatusDto("ok","unavailable");
        _ = new StatusResponse("ready",100,90,null,null,0,new SourceStatusDto("ok","unavailable"));

        // Destination
        _ = new DestinationDto("ISS","ISS",408,51.6,"desc",9.4,6.2,"iss");
        _ = new DestinationsResponse([]);

        // Mission
        _ = new SimulateRequest("ISS", DateTime.UtcNow, DateTime.UtcNow.AddHours(6));
        _ = new ObstacleDto("1","n",5.0,"2025-01-01T00:00:00Z","high");
        _ = new SimulateResponse("ISS",DateTime.UtcNow,DateTime.UtcNow.AddHours(6),[],[],87,0.12,9.4);
        _ = new SessionRequest("ISS","s","e");
        _ = new SessionResponse("sess_1","ISS","s","e","/stream","exp");
        _ = new CompleteSessionRequest("success", false);
        _ = new CompleteSessionResponse("sess_1","success",87,0.12,9.4,2,3600.0,false,null);

        // History
        _ = new MissionSummaryDto("msn_1","ISS","ISS","success",87,0.12,9.4,2,"s","e","c");
        _ = new ScoreBreakdownDto(42,45,87);
        _ = new MissionDetailResponse("msn_1","ISS","ISS","success",87,0.12,9.4,"s","e","c",[],new ScoreBreakdownDto(42,45,87));
        _ = new MissionStatsResponse(12,9,2,1,0.75,97,23,81,112.8,18,"ISS",new Dictionary<string,int>{{"ISS",8}});

        // Dashboard
        _ = new LastMissionDto("ISS","success",87,"2025-01-01T00:00:00Z");
        _ = new UserDashboardDto("Name",12,97,null);
        _ = new OrbitalSummaryDto(1000,new ByTypeDto(0,0,0),new ByAltitudeBandDto(0,0,0),3,"2025-01-01T00:00:00Z");
        _ = new DashboardSummaryResponse(new OrbitalSummaryDto(0,new ByTypeDto(0,0,0),new ByAltitudeBandDto(0,0,0),0,"2025-01-01T00:00:00Z"),null);
        _ = new AlertDto("alrt_1","1","n","ISS",8.2,"2025-01-01T00:00:00Z","critical",238,"2025-01-01T00:00:00Z");
        _ = new AlertsResponse([],6,"2025-01-01T00:00:00Z");
    }

    [Fact]
    public void RegisterRequest_default_role_is_Researcher()
    {
        var req = new RegisterRequest("a@b.com", "Pass1234!", "Name");
        req.Role.Should().Be("Researcher");
    }

    [Fact]
    public void OrbitalObject_optional_tle_fields_are_null_by_default()
    {
        var obj = new OrbitalObject("1","ISS","satellite",0,0,408,7.66,"celestrak",DateTime.UtcNow);
        obj.TleLine1.Should().BeNull();
        obj.InclinationDeg.Should().BeNull();
    }
}
