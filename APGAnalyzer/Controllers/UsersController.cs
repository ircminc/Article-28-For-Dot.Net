using APGAnalyzer.Models;
using APGAnalyzer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Controllers;

/// <summary>
/// Admin-only user management: list users, create new ones, change role,
/// reset password, lock/unlock, delete.
///
/// Non-admins (analyst, viewer) request password resets through the admin —
/// there's no self-service email reset flow yet.
/// </summary>
[Authorize(Roles = RoleSeeder.AdminRole)]
public class UsersController(
    UserManager<IdentityUser> userMgr,
    ILogger<UsersController> log) : Controller
{
    /// <summary>GET /Users — list every account with its role.</summary>
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var users = await userMgr.Users
            .OrderBy(u => u.UserName)
            .ToListAsync(ct);

        var rows = new List<UserListRow>(users.Count);
        var counts = new Dictionary<string, int>();
        foreach (var r in RoleSeeder.AllRoles) counts[r] = 0;

        var meId = userMgr.GetUserId(User);
        var nowUtc = DateTimeOffset.UtcNow;

        foreach (var u in users)
        {
            var roles = await userMgr.GetRolesAsync(u);
            var primaryRole = roles.FirstOrDefault() ?? "(none)";
            if (counts.ContainsKey(primaryRole)) counts[primaryRole]++;

            var lockEnd = u.LockoutEnd;
            var locked = lockEnd.HasValue && lockEnd.Value > nowUtc;

            rows.Add(new UserListRow
            {
                Id            = u.Id,
                Email         = u.UserName ?? u.Email ?? "(no email)",
                Role          = primaryRole,
                IsLockedOut   = locked,
                LockoutEnd    = lockEnd,
                IsCurrentUser = u.Id == meId,
            });
        }

        return View(new UserListViewModel { Rows = rows, RoleCounts = counts });
    }

    /// <summary>GET /Users/Create — admin-driven new-user form.</summary>
    public IActionResult Create() => View(new CreateUserViewModel());

    /// <summary>POST /Users/Create — provision the user and assign their role.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        if (!RoleSeeder.AllRoles.Contains(vm.Role))
        {
            ModelState.AddModelError(nameof(vm.Role), "Pick a valid role.");
            return View(vm);
        }

        var existing = await userMgr.FindByEmailAsync(vm.Email);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(vm.Email), "A user with that email already exists.");
            return View(vm);
        }

        var user = new IdentityUser
        {
            UserName = vm.Email,
            Email    = vm.Email,
            EmailConfirmed = true,   // admin-provisioned, skip the confirmation flow
        };

        var create = await userMgr.CreateAsync(user, vm.Password);
        if (!create.Succeeded)
        {
            foreach (var e in create.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }

        var addRole = await userMgr.AddToRoleAsync(user, vm.Role);
        if (!addRole.Succeeded)
        {
            log.LogError("Created user {Email} but failed to assign role {Role}: {Errors}",
                vm.Email, vm.Role, string.Join("; ", addRole.Errors.Select(e => e.Description)));
        }

        log.LogInformation("Admin {Admin} created user {Email} with role {Role}",
            User.Identity?.Name, vm.Email, vm.Role);

        TempData["UsersStatus"] = $"Created {vm.Email} as {vm.Role}.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>GET /Users/Edit/{id} — change role or toggle lock.</summary>
    public async Task<IActionResult> Edit(string id)
    {
        var u = await userMgr.FindByIdAsync(id);
        if (u is null) return NotFound();
        var roles = await userMgr.GetRolesAsync(u);

        return View(new EditUserViewModel
        {
            Id            = u.Id,
            Email         = u.UserName ?? u.Email ?? "",
            Role          = roles.FirstOrDefault() ?? RoleSeeder.ViewerRole,
            IsLockedOut   = u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow,
        });
    }

    /// <summary>POST /Users/Edit/{id}.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        var u = await userMgr.FindByIdAsync(vm.Id);
        if (u is null) return NotFound();

        // Don't let an admin demote / lock themselves out.
        var meId = userMgr.GetUserId(User);
        var isSelf = u.Id == meId;

        // ----- Role change -----
        if (!RoleSeeder.AllRoles.Contains(vm.Role))
        {
            ModelState.AddModelError(nameof(vm.Role), "Pick a valid role.");
            return View(vm);
        }

        var currentRoles = await userMgr.GetRolesAsync(u);
        var currentRole = currentRoles.FirstOrDefault();

        if (isSelf && currentRole == RoleSeeder.AdminRole && vm.Role != RoleSeeder.AdminRole)
        {
            // Block self-demotion only if no other admin exists.
            var otherAdmins = (await userMgr.GetUsersInRoleAsync(RoleSeeder.AdminRole))
                .Where(o => o.Id != meId).ToList();
            if (otherAdmins.Count == 0)
            {
                ModelState.AddModelError(nameof(vm.Role),
                    "You are the only admin — promote another user before demoting yourself.");
                return View(vm);
            }
        }

        if (currentRole != vm.Role)
        {
            if (currentRoles.Count > 0)
                await userMgr.RemoveFromRolesAsync(u, currentRoles);
            await userMgr.AddToRoleAsync(u, vm.Role);
            log.LogInformation("Admin {Admin} changed {Email} role: {Old} → {New}",
                User.Identity?.Name, u.UserName, currentRole, vm.Role);
        }

        // ----- Lockout toggle -----
        if (isSelf && vm.IsLockedOut)
        {
            ModelState.AddModelError(nameof(vm.IsLockedOut), "You can't lock yourself out.");
            return View(vm);
        }

        if (vm.IsLockedOut)
        {
            await userMgr.SetLockoutEnabledAsync(u, true);
            await userMgr.SetLockoutEndDateAsync(u, DateTimeOffset.UtcNow.AddYears(100));
        }
        else
        {
            await userMgr.SetLockoutEndDateAsync(u, null);
        }

        TempData["UsersStatus"] = $"Updated {u.UserName}.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>GET /Users/ResetPassword/{id}.</summary>
    public async Task<IActionResult> ResetPassword(string id)
    {
        var u = await userMgr.FindByIdAsync(id);
        if (u is null) return NotFound();
        return View(new ResetPasswordViewModel { Id = u.Id, Email = u.UserName ?? "" });
    }

    /// <summary>POST /Users/ResetPassword/{id}.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);

        var u = await userMgr.FindByIdAsync(vm.Id);
        if (u is null) return NotFound();

        // Issue a reset token to ourselves and immediately use it to set the new password.
        var token = await userMgr.GeneratePasswordResetTokenAsync(u);
        var result = await userMgr.ResetPasswordAsync(u, token, vm.NewPassword);

        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View(vm);
        }

        // Optional: bump the security stamp so any active sessions for that
        // user are invalidated on their next request.
        await userMgr.UpdateSecurityStampAsync(u);

        log.LogInformation("Admin {Admin} reset password for {Email}",
            User.Identity?.Name, u.UserName);
        TempData["UsersStatus"] = $"Password reset for {u.UserName}. Share the new password with them securely.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Users/Delete/{id} — delete the user account entirely.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var u = await userMgr.FindByIdAsync(id);
        if (u is null) return NotFound();

        var meId = userMgr.GetUserId(User);
        if (u.Id == meId)
        {
            TempData["UsersError"] = "You can't delete your own account.";
            return RedirectToAction(nameof(Index));
        }

        // Don't let the last admin be deleted.
        var roles = await userMgr.GetRolesAsync(u);
        if (roles.Contains(RoleSeeder.AdminRole))
        {
            var others = (await userMgr.GetUsersInRoleAsync(RoleSeeder.AdminRole))
                .Where(o => o.Id != u.Id).ToList();
            if (others.Count == 0)
            {
                TempData["UsersError"] = "Can't delete the only admin account.";
                return RedirectToAction(nameof(Index));
            }
        }

        var result = await userMgr.DeleteAsync(u);
        if (!result.Succeeded)
        {
            TempData["UsersError"] = string.Join("; ", result.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Index));
        }

        log.LogWarning("Admin {Admin} deleted user {Email}",
            User.Identity?.Name, u.UserName);
        TempData["UsersStatus"] = $"Deleted user {u.UserName}.";
        return RedirectToAction(nameof(Index));
    }
}
