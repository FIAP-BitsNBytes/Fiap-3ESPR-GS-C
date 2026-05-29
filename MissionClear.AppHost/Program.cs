var builder = DistributedApplication.CreateBuilder(args);

// Api
var api = builder.AddProject("api", "../MissionClear.Api/MissionClear.Api.csproj");

// Web MVC
builder.AddProject("web", "../MissionClear.Web/MissionClear.Web.csproj")
    .WithReference(api);

builder.Build().Run();
