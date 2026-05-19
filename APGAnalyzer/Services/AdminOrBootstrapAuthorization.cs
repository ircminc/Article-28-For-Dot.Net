using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Services;

/// <summary>
/// Authorization policy used to gate self-service registration. The policy
/// succeeds when EITHER:
///   * The current user is in the "admin" role, OR
///   * There are zero users in the database (bootstrap mode for the very
///     first deploy, where no admin exists yet to invite anyone).
///
/// This solves the chicken-and-egg problem on a fresh deployment:
/// without bootstrap mode the Register page would be admin-gated forever,
/// and no admin could ever be created. Once the first user registers, the
/// RoleSeeder auto-promotes them to admin on the next app start, and the
/// "no users" branch becomes unreachable — the page is admin-only again.
/// </summary>
public class AdminOrBootstrapRequirement : IAuthorizationRequirement { }

public class AdminOrBootstrapHandler(
    UserManager<IdentityUser> userMgr,
    ILogger<AdminOrBootstrapHandler> log)
    : AuthorizationHandler<AdminOrBootstrapRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminOrBootstrapRequirement requirement)
    {
        // Path 1 — already an admin
        if (context.User.IsInRole(RoleSeeder.AdminRole))
        {
            context.Succeed(requirement);
            return;
        }

        // Path 2 — fresh DB, no users yet → allow this one through so the
        // first registration succeeds. RoleSeeder will promote them on the
        // next app start. After that, this branch is unreachable.
        var anyUser = await userMgr.Users.AnyAsync();
        if (!anyUser)
        {
            log.LogInformation(
                "AdminOrBootstrap: no users exist yet, allowing access to Register "
              + "page for bootstrap. The first registered user will be auto-promoted "
              + "to admin on the next app start.");
            context.Succeed(requirement);
            return;
        }

        // Otherwise: not admin and users already exist → fail (default).
    }
}
