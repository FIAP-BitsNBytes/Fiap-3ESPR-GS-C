using MissionClear.ServiceDefaults;
using MissionClear.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();

// Service discovery do Aspire resolve "http://api" quando rodando via AppHost.
// Fora do Aspire, ApiBaseUrl no appsettings.Development.json aponta para localhost.
builder.Services.AddHttpClient<ApiClient>(client =>
{
    var baseAddress = builder.Configuration["ApiBaseUrl"] ?? "http://api";
    client.BaseAddress = new Uri(baseAddress);
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultControllerRoute();
app.MapDefaultEndpoints();

app.Run();

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
