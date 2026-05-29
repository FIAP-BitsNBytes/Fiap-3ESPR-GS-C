var builder = DistributedApplication.CreateBuilder(args);

// Provisiona container MySQL automaticamente via Docker Desktop
// Connection string injetada via service discovery nos projetos que referenciam "missionclear"
var mysql = builder.AddMySql("mysql")
    .WithEnvironment("MYSQL_ROOT_PASSWORD", "MissionClear_Dev_2025!")
    .AddDatabase("missionclear");

// Api aguarda MySQL estar healthy antes de iniciar
var api = builder.AddProject("api", "../MissionClear.Api/MissionClear.Api.csproj")
    .WithReference(mysql)
    .WaitFor(mysql);

// Web MVC aguarda Api estar healthy antes de iniciar
builder.AddProject("web", "../MissionClear.Web/MissionClear.Web.csproj")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
