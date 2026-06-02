using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Admin;
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[Authorize(Roles = "Administrator")]
public sealed class AdminController(
    IUserRepository userRepo,
    IMissionRepository missionRepo,
    IMissionHistoryService historyService) : BaseApiController
{
    // GET api/admin/users — todos os usuários + missão count (1 query só)
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(CancellationToken ct)
    {
        var usersTask  = userRepo.GetAllAsync(ct);
        var countsTask = missionRepo.GetMissionCountsPerUserAsync(ct);
        await Task.WhenAll(usersTask, countsTask);

        var users  = usersTask.Result;
        var counts = countsTask.Result;

        var dtos = users.Select(u => new AdminUserDto(
            $"usr_{u.Id:N}",
            u.Email,
            u.DisplayName,
            u.Role,
            u.CreatedAt.ToString("O"),
            counts.GetValueOrDefault(u.Id, 0))).ToList();

        return Ok(new { data = dtos, total = dtos.Count });
    }

    // PUT api/admin/users/{id}/role — promover/rebaixar role
    [HttpPut("users/{id}/role")]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] UpdateRoleRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(id.Replace("usr_", ""), out var guid))
            throw new DomainException("INVALID_ID", "Invalid user ID.", 400);

        if (request.Role is not ("Administrator" or "Researcher"))
            throw new DomainException("INVALID_ROLE", "Role must be 'Administrator' or 'Researcher'.", 400);

        if (guid == CurrentUserId)
            throw new DomainException("CANNOT_MODIFY_SELF", "Cannot change your own role.", 400);

        var user = await userRepo.GetByIdAsync(guid, ct)
            ?? throw new DomainException("USER_NOT_FOUND", "User not found.", 404);

        user.Role = request.Role;
        await userRepo.UpdateAsync(user, ct);
        await userRepo.SaveChangesAsync(ct);

        return Ok(new { id = $"usr_{user.Id:N}", role = user.Role });
    }

    // DELETE api/admin/users/{id} — remove usuário e suas missões (cascade)
    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id.Replace("usr_", ""), out var guid))
            throw new DomainException("INVALID_ID", "Invalid user ID.", 400);

        if (guid == CurrentUserId)
            throw new DomainException("CANNOT_DELETE_SELF", "Cannot delete your own account.", 400);

        var user = await userRepo.GetByIdAsync(guid, ct)
            ?? throw new DomainException("USER_NOT_FOUND", "User not found.", 404);

        userRepo.Delete(user);
        await userRepo.SaveChangesAsync(ct);
        return NoContent();
    }

    // GET api/admin/missions/{id} — detalhe sem verificação de ownership
    [HttpGet("missions/{id}")]
    public async Task<IActionResult> GetMission(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id.Replace("msn_", ""), out var guid))
            throw new DomainException("INVALID_ID", "Invalid mission ID.", 400);

        var result = await historyService.GetMissionDetailAdminAsync(guid, ct);
        return Ok(result);
    }

    // GET api/admin/missions — todas as missões de todos os usuários
    [HttpGet("missions")]
    public async Task<IActionResult> GetMissions(
        [FromQuery] int     page        = 1,
        [FromQuery] int     limit       = 20,
        [FromQuery] string? status      = null,
        [FromQuery] string? destination = null,
        CancellationToken   ct          = default)
    {
        limit = Math.Clamp(limit, 1, 50);
        var result = await missionRepo.GetAllPagedAsync(page, limit, status, destination, ct);

        var dtos = result.Items.Select(m => new AdminMissionDto(
            $"msn_{m.Id:N}",
            $"usr_{m.UserId:N}",
            m.User?.Email       ?? "—",
            m.User?.DisplayName ?? "—",
            m.Destination,
            KnownDestinations.FindById(m.Destination)?.DisplayName ?? m.Destination,
            m.Status,
            m.MissionScore,
            Math.Round(m.RiskScore, 4),
            m.DeltaVKmS,
            m.ObstaclesEncountered,
            m.CreatedAt.ToString("O"))).ToList();

        return Ok(new PagedResponse<AdminMissionDto>(
            dtos,
            PaginationDto.From(page, limit, result.TotalCount)));
    }
}

public sealed record UpdateRoleRequest(string Role);
