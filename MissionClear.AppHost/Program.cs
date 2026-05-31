using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.MissionClear_Api>("api")
    .WithHttpEndpoint(port: 5273, name: "http");

builder.AddProject<Projects.MissionClear_Web>("web")
    .WithReference(api)
    .WaitFor(api);

builder.Eventing.Subscribe<ResourceEndpointsAllocatedEvent>((evt, ct) =>
{
    if (evt.Resource is not IResourceWithEndpoints res) return Task.CompletedTask;

    var logger = evt.Services.GetRequiredService<ILoggerFactory>()
                             .CreateLogger("MissionClear.AppHost");
    foreach (var endpoint in res.GetEndpoints())
    {
        logger.LogInformation("  {Resource,-6} → {Url}", res.Name, endpoint.Url);
    }
    return Task.CompletedTask;
});

builder.Build().Run();
