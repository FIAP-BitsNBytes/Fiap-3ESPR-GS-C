using MissionClear.Api.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System.Net;

namespace MissionClear.Tests.Integration;

public sealed class TestWebApplicationFactory : WebApplicationFactory<ApiMarker>
{
    private readonly string _dbName = $"TestDb_{Guid.NewGuid()}";

    public TestWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__missionclear", "Server=localhost;Database=fake;");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Inject test configuration — must satisfy startup guard (Jwt:Secret >= 32 chars)
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:missionclear"]          = "Server=localhost;Database=fake;",
                ["Jwt:Secret"]                              = "test-secret-key-with-at-least-32-characters-long!!",
                ["Jwt:Issuer"]                              = "mission-clear-api-test",
                ["Jwt:Audience"]                            = "mission-clear-mobile-test",
                ["Jwt:AccessTokenMinutes"]                  = "15",
                ["Jwt:RefreshTokenDays"]                    = "7",
                ["KeepTrack:ApiKey"]                        = "",
                // Zero inter-catalog delay in tests: no point waiting 3s between failed fetches.
                ["ExternalApi:CelesTrakRequestDelaySeconds"] = "0",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace MySQL DbContext with InMemory for tests
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            // Remove TleIngestionService — it completes fast with the ImmediateFailHandler,
            // calls DB fallback, gets 0 objects, and clears the cache — racing with
            // tests that seed the cache directly via SeedOrbitalCache().
            var tleDescriptors = services
                .Where(d => d.ImplementationType?.FullName?.Contains("TleIngestionService") == true)
                .ToList();
            foreach (var d in tleDescriptors) services.Remove(d);

            // Replace the "celestrak" HttpClient handler chain with an immediate 503.
            // Avoids 60s resilience timeout × 8 catalogs (= 480s+ blocking) in tests.
            // DataAggregatorService catches the resulting HttpRequestException and skips the catalog.
            services.Configure<HttpClientFactoryOptions>("celestrak", opts =>
            {
                opts.HttpMessageHandlerBuilderActions.Clear();
                opts.HttpMessageHandlerBuilderActions.Add(b =>
                    b.PrimaryHandler = new ImmediateFailHandler());
            });

            // Workaround for .NET 10 TestServer PipeWriter.UnflushedBytes bug
            services.AddSingleton<IStartupFilter, BufferResponseStartupFilter>();
        });
    }

    /// <summary>
    /// Immediately returns 503 for all requests — used to replace the CelesTrak HttpClient
    /// in tests so catalog fetches fail fast instead of waiting 60s for TCP timeout.
    /// </summary>
    private sealed class ImmediateFailHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class BufferResponseStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    var originalBody = context.Response.Body;
                    using var memoryStream = new MemoryStream();
                    context.Response.Body = memoryStream;
                    
                    await nextMiddleware();
                    
                    memoryStream.Position = 0;
                    await memoryStream.CopyToAsync(originalBody);
                });
                next(app);
            };
        }
    }
}
