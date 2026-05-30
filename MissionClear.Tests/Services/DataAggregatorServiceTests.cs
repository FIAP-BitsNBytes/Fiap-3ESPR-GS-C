using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using MissionClear.Tests.Helpers;
using Moq;
using System.Net;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class DataAggregatorServiceTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    private static IOptions<ExternalApiSettings> DefaultSettings() =>
        Options.Create(new ExternalApiSettings
        {
            CelesTrakCatalogs            = [new("https://celestrak.test/gp.php", "stations")],
            CelesTrakRequestDelaySeconds = 0,
            KeepTrackBaseUrl             = "https://keeptrack.test/api",
            KeepTrackApiKey              = "test-key",
            KeepTrackTimeoutSeconds      = 5,
        });

    // Minimal valid 3-line TLE format (as returned by FORMAT=tle).
    private const string OneTleRecord =
        "ISS (ZARYA)\r\n" +
        "1 25544U 98067A   25001.00000000  .00000000  00000-0  00000-0 0  9999\r\n" +
        "2 25544  51.6400 000.0000 0001000 000.0000 000.0000 15.50000000    0\r\n";

    private const string TwoTleRecords =
        "ISS (ZARYA)\r\n" +
        "1 25544U 98067A   25001.00000000  .00000000  00000-0  00000-0 0  9999\r\n" +
        "2 25544  51.6400 000.0000 0001000 000.0000 000.0000 15.50000000    0\r\n" +
        "CSS (TIANHE)\r\n" +
        "1 48274U 21035A   25001.00000000  .00000000  00000-0  00000-0 0  9999\r\n" +
        "2 48274  41.4700 000.0000 0001000 000.0000 000.0000 15.60000000    0\r\n";

    private static DataAggregatorService CreateSut(
        HttpMessageHandler celestrakHandler,
        HttpMessageHandler? keeptrackHandler = null,
        IOptions<ExternalApiSettings>? settings = null)
    {
        var opts = settings ?? DefaultSettings();

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient("celestrak"))
            .Returns(new HttpClient(celestrakHandler));
        factoryMock
            .Setup(f => f.CreateClient("keeptrack"))
            .Returns(new HttpClient(keeptrackHandler
                ?? MockHttpMessageHandler.Status(HttpStatusCode.NotFound)));

        var cacheMock = new Mock<IOrbitalCache>();
        cacheMock.Setup(c => c.Update(It.IsAny<IReadOnlyList<MissionClear.Api.Models.OrbitalObject>>()));
        cacheMock.Setup(c => c.GetAll()).Returns([]);

        var scopeMock        = new Mock<IServiceScope>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(s => s.CreateScope()).Returns(scopeMock.Object);

        var sut = new DataAggregatorService(
            factoryMock.Object,
            cacheMock.Object,
            opts,
            NullLogger<DataAggregatorService>.Instance,
            scopeFactoryMock.Object);

        sut._capturedUpdates = [];
        return sut;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchAndMergeAsync_ParsesValidTleResponse_AndCallsUpdate()
    {
        var sut = CreateSut(MockHttpMessageHandler.PlainText(OneTleRecord));

        await sut.FetchAndMergeAsync();

        sut._capturedUpdates.Should().HaveCount(1);
        var objects = sut._capturedUpdates![0];
        objects.Should().HaveCount(1);
        objects[0].Id.Should().Be("25544");
        objects[0].Name.Should().Be("ISS (ZARYA)");
        objects[0].Source.Should().Be("celestrak-stations");
        objects[0].TleLine1.Should().StartWith("1 25544");
        objects[0].TleLine2.Should().StartWith("2 25544");
    }

    [Fact]
    public async Task FetchAndMergeAsync_ParsesTwoTleRecords()
    {
        var sut = CreateSut(MockHttpMessageHandler.PlainText(TwoTleRecords));

        await sut.FetchAndMergeAsync();

        sut._capturedUpdates![0].Should().HaveCount(2);
        sut._capturedUpdates[0].Select(o => o.Id).Should().Contain(["25544", "48274"]);
    }

    [Fact]
    public async Task FetchAndMergeAsync_CelesTrakFails_ThrowsOrFallsBack()
    {
        var sut = CreateSut(MockHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable));

        // All catalogs fail → DB fallback → scope mock has no AppDbContext → throws
        var act = () => sut.FetchAndMergeAsync();

        await act.Should().ThrowAsync<Exception>();
        sut._capturedUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAndMergeAsync_KeepTrackFailure_DoesNotThrow()
    {
        var sut = CreateSut(
            celestrakHandler: MockHttpMessageHandler.PlainText(OneTleRecord),
            keeptrackHandler: MockHttpMessageHandler.Throws(new HttpRequestException("KeepTrack down")));

        var act2 = () => sut.FetchAndMergeAsync();
        await act2.Should().NotThrowAsync();

        sut._capturedUpdates![0].Should().HaveCount(1);
        sut._capturedUpdates[0][0].TleLine1.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FetchAndMergeAsync_DeduplicatesMerge_CelesTrakWins()
    {
        // Same NORAD ID from both sources — CelesTrak takes priority.
        const string celestrakTle =
            "CELESTRAK_OBJ\r\n" +
            "1 00042U 00000A   25001.00000000  .00000000  00000-0  00000-0 0  9999\r\n" +
            "2 00042  51.0000 000.0000 0001000 000.0000 000.0000 15.50000000    0\r\n";

        const string keeptrackTle =
            "KEEPTRACK_OBJ\r\n" +
            "1 00042U 00000A   25001.00000000  .00000000  00000-0  00000-0 0  9999\r\n" +
            "2 00042  51.0000 000.0000 0001000 000.0000 000.0000 15.50000000    0\r\n";

        var sut = CreateSut(
            celestrakHandler: MockHttpMessageHandler.PlainText(celestrakTle),
            keeptrackHandler: MockHttpMessageHandler.PlainText(keeptrackTle));

        await sut.FetchAndMergeAsync();

        var merged = sut._capturedUpdates![0];
        merged.Should().HaveCount(1);
        merged[0].Source.Should().Be("celestrak-stations");
        merged[0].Name.Should().Be("CELESTRAK_OBJ");
    }
}
