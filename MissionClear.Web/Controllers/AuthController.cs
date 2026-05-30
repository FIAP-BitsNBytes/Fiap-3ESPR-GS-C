using System.Security.Claims;
using MissionClear.Web.Models;
using MissionClear.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Web.Controllers;

public sealed class AuthController(ApiClient apiClient) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl)
        => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var response = await apiClient.LoginAsync(model.Email, model.Password);
        if (response is null)
        {
            model.Error = "Email ou senha incorretos.";
            return View(model);
        }

        await SignInWithCookieAsync(response.User, response.AccessToken, response.RefreshToken);
        return Redirect(model.ReturnUrl ?? "/Dashboard");
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var response = await apiClient.RegisterAsync(model.Email, model.Password, model.DisplayName);
        if (response is null)
        {
            model.Error = "Erro ao criar conta. Verifique se o email já está cadastrado.";
            return View(model);
        }

        await SignInWithCookieAsync(response.User, response.AccessToken, response.RefreshToken);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    private async Task SignInWithCookieAsync(LoginUserDto user, string accessToken, string refreshToken)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role),        // "Researcher" ou "Administrator"
            new("access_token", accessToken),       // Repassado à API em chamadas subsequentes
            new("refresh_token", refreshToken),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });
    }
}
