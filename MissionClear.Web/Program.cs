using MissionClear.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Aspire: OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllersWithViews();

// Cookie auth será adicionado na Fase 8 (MVC Web)
// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)...

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultControllerRoute();
app.MapDefaultEndpoints(); // /health + /alive (dev only)

app.Run();
