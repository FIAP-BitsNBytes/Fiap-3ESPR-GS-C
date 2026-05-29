using MissionClear.Api.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MissionClear.Tests.Integration;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
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
                ["ConnectionStrings:missionclear"] = "Server=localhost;Database=fake;",
                ["Jwt:Secret"]             = "test-secret-key-with-at-least-32-characters-long!!",
                ["Jwt:Issuer"]             = "mission-clear-api-test",
                ["Jwt:Audience"]           = "mission-clear-mobile-test",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"]   = "7",
                ["KeepTrack:ApiKey"]       = "",
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

            // Workaround for .NET 10 TestServer PipeWriter.UnflushedBytes bug
            services.AddSingleton<IStartupFilter, BufferResponseStartupFilter>();
        });
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