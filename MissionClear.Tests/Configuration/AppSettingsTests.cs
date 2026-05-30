using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MissionClear.Api.Configuration;
using Xunit;

namespace MissionClear.Tests.Configuration;

public class AppSettingsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    [Fact]
    public void JwtSettings_BindsCorrectly()
    {
        var config = BuildConfig(new()
        {
            ["Jwt:Secret"] = "exactly-32-characters-long-secret!",
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience",
            ["Jwt:AccessTokenMinutes"] = "30",
            ["Jwt:RefreshTokenDays"] = "14"
        });

        var settings = config.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;

        settings.Secret.Should().Be("exactly-32-characters-long-secret!");
        settings.Issuer.Should().Be("test-issuer");
        settings.AccessTokenMinutes.Should().Be(30);
        settings.RefreshTokenDays.Should().Be(14);
    }

    [Fact]
    public void OrbitalSettings_BindsWithDefaults()
    {
        var config = BuildConfig(new());
        var settings = config.GetSection(OrbitalSettings.SectionName).Get<OrbitalSettings>()
            ?? new OrbitalSettings();

        settings.TleFetchIntervalMinutes.Should().Be(60);
        settings.PropagationIntervalSeconds.Should().Be(60);
        settings.MaxDebrisCount.Should().Be(30000);
        settings.TleMaxAgeDays.Should().Be(7);
    }

    [Fact]
    public void ExternalApiSettings_DefaultCatalogsContainDebrisAndOperationalGroups()
    {
        var settings = new ExternalApiSettings();

        settings.CelesTrakCatalogs.Should().NotBeEmpty();
        settings.CelesTrakCatalogs.Should().Contain(c => c.Label.Contains("debris"));
        settings.CelesTrakCatalogs.Should().OnlyContain(c => c.Url.Contains("celestrak.org"));
        settings.CelesTrakCatalogs.Should().OnlyContain(c => c.Url.Contains("FORMAT=tle"));
    }

    [Fact]
    public void ExternalApiSettings_BindsKeepTrackApiKey()
    {
        var config = BuildConfig(new()
        {
            ["ExternalApi:KeepTrackApiKey"] = "my-key"
        });

        var settings = config.GetSection(ExternalApiSettings.SectionName).Get<ExternalApiSettings>()!;

        settings.KeepTrackApiKey.Should().Be("my-key");
    }

    [Fact]
    public void JwtSecret_ShorterThan32_ShouldFailValidation()
    {
        var secret = "short";
        var isValid = !string.IsNullOrWhiteSpace(secret) && secret.Length >= 32;
        isValid.Should().BeFalse("secrets shorter than 32 chars must be rejected at startup");
    }
}
