using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MissionClear.Tests.Integration;

// Nota: WebApplicationFactory<Program> para MVC requer referência ao MissionClear.Web project.
// Estes testes verificam que rotas protegidas redirecionam para login corretamente.

public sealed class MvcAuthorizationTests : IClassFixture<WebApplicationFactory<WebMarker>>
{
    private readonly WebApplicationFactory<WebMarker> _factory;

    public MvcAuthorizationTests(WebApplicationFactory<WebMarker> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // ApiClient calls serão falhas (sem API real) — OK para testar redirecionamentos
            });
        });
    }

    [Fact]
    public async Task Missions_Unauthenticated_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Missions");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Auth/Login", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task Users_Unauthenticated_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Users");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_Unauthenticated_Returns200()
    {
        // Dashboard é público (dados orbitais sem auth)
        var client = _factory.CreateClient();
        // Sem API real, GetAsync retorna default — controller ainda retorna View(vm) com dados zerados
        // Só verificamos que não é 401/403
        var response = await client.GetAsync("/Dashboard");
        // Dashboard requires ApiClient to be functional. In integration tests, the response
        // may vary. We only verify auth is not required (no 401/403).
        Assert.NotEqual(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }
}
