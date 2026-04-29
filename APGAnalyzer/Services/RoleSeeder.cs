using Microsoft.AspNetCore.Identity;

namespace APGAnalyzer.Services;

/// <summary>
/// Ensures the "admin" Identity role exists, and grants it to the FIRST
/// registered user so the existing admin@test.com account doesn't lose
/// access when role-based authorization gets turned on for the Settings
/// controller.
///
/// Runs once on application startup.
/// </summary>
public static class RoleSeeder
{
    public const string AdminRole = "admin";
    public const string AnalystRole = "analyst";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RoleSeeder");

        // 1. Make sure the roles exist.
        foreach (var role in new[] { AdminRole, AnalystRole })
        {
            if (!await roleMgr.RoleExistsAsync(role))
            {
                await roleMgr.CreateAsync(new IdentityRole(role));
                log.LogInformation("Created role '{Role}'", role);
            }
        }

        // 2. If no user has the admin role yet, grant it to the lowest-Id user.
        //    This catches the bootstrapping case where someone registered
        //    BEFORE role-based gating was added.
        var anyAdmin = await userMgr.GetUsersInRoleAsync(AdminRole);
        if (anyAdmin.Count > 0) return;

        var firstUser = userMgr.Users
            .OrderBy(u => u.Id)
            .FirstOrDefault();

        if (firstUser is null) return;   // no users yet — they'll register and we'll catch them on next startup

        var addResult = await userMgr.AddToRoleAsync(firstUser, AdminRole);
        if (addResult.Succeeded)
            log.LogWarning(
                "Auto-promoted first registered user '{Email}' to admin (no admin existed yet)",
                firstUser.UserName);
        else
            log.LogError(
                "Failed to grant admin to '{Email}': {Errors}",
                firstUser.UserName,
                string.Join("; ", addResult.Errors.Select(e => e.Description)));
    }
}
