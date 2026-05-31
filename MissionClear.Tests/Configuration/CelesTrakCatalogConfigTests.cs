using FluentAssertions;
using MissionClear.Api.Configuration;
using Xunit;

namespace MissionClear.Tests.Configuration;

public sealed class CelesTrakCatalogConfigTests
{
    private static ExternalApiSettings DefaultSettings() => new();

    [Theory]
    [InlineData("stations",           "Estações espaciais (ISS, CSS)")]
    [InlineData("recent",             "Objetos lançados nos últimos 30 dias")]
    [InlineData("fengyun-debris",     "Destroços FY-1C (colisão 2007)")]
    [InlineData("cosmos-debris",      "Destroços Cosmos 2251 (colisão 2009)")]
    [InlineData("iridium-debris",     "Destroços Iridium 33 (colisão 2009)")]
    [InlineData("active",             "Satélites ativos em LEO")]
    [InlineData("cosmos-1408-debris", "Destroços ASAT russo 2021")]
    [InlineData("breeze-m-debris",    "Destroços Breeze-M")]
    public void DefaultCatalogs_ContainsLabel(string label, string reason)
    {
        var settings = DefaultSettings();
        var labels   = settings.CelesTrakCatalogs.Select(c => c.Label).ToList();

        labels.Should().Contain(label, reason);
    }

    [Fact]
    public void DefaultCatalogs_HasAtLeast8Catalogs()
    {
        var settings = DefaultSettings();
        settings.CelesTrakCatalogs.Should().HaveCountGreaterThanOrEqualTo(8,
            "precisamos de cobertura ampla para chegar a ~18k objetos");
    }

    [Fact]
    public void DefaultCatalogs_AllUrlsContainFormatTle()
    {
        var settings = DefaultSettings();
        foreach (var catalog in settings.CelesTrakCatalogs)
        {
            catalog.Url.Should().Contain("FORMAT=tle",
                $"catálogo '{catalog.Label}' deve usar FORMAT=tle para o parser TLE de texto");
        }
    }

    [Fact]
    public void DefaultCatalogs_AllUrlsPointToCelesTrak()
    {
        var settings = DefaultSettings();
        foreach (var catalog in settings.CelesTrakCatalogs)
        {
            catalog.Url.Should().StartWith("https://celestrak.org/",
                $"catálogo '{catalog.Label}' deve usar HTTPS celestrak.org");
        }
    }

    [Fact]
    public void DefaultRequestDelay_IsPositive()
    {
        var settings = DefaultSettings();
        settings.CelesTrakRequestDelaySeconds.Should().BeGreaterThan(0,
            "delay 0 pode causar rate-limiting ou ban do IP do CelesTrak");
    }
}
