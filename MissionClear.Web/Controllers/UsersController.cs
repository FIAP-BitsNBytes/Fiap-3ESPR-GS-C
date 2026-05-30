using MissionClear.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Web.Controllers;

[Authorize(Roles = "Administrator")]
public sealed class UsersController(ApiClient apiClient) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.Message = "Área administrativa de usuários.";
        return View();
    }
}
