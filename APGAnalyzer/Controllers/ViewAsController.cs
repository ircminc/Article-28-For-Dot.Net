using APGAnalyzer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace APGAnalyzer.Controllers;

/// <summary>
/// Admin / viewer-only "View as another user" scope switcher.
/// Stores the chosen user id in session; <see cref="CurrentUserContext"/>
/// reads it on every request to scope read queries.
/// </summary>
[Authorize(Roles = RoleSeeder.AdminRole + "," + RoleSeeder.ViewerRole)]
public class ViewAsController(UserManager<IdentityUser> userMgr) : Controller
{
    /// <summary>POST /ViewAs/Set — scope the session to a specific user id.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Set(string? userId, string? returnUrl)
    {
        // Empty / "all" → clear the scope (admin sees everything).
        if (string.IsNullOrWhiteSpace(userId) || userId == "all")
        {
            HttpContext.Session.Remove(CurrentUserContext.ViewAsSessionKey);
        }
        else
        {
            // Validate the target user exists before stashing the id.
            var target = await userMgr.FindByIdAsync(userId);
            if (target is not null)
            {
                HttpContext.Session.SetString(
                    CurrentUserContext.ViewAsSessionKey, target.Id);
            }
        }

        // Bounce back to where they came from, or home.
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }
}
