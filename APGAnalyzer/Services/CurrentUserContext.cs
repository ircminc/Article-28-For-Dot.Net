using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace APGAnalyzer.Services;

/// <summary>
/// Per-request context for "whose data are we showing right now?"
///
/// For analysts and viewers, the answer is always their own user ID.
/// Admins can override the scope through a session-cookie ("View as user")
/// dropdown — when set, every read query filters to that user. Admins
/// without an override see everything (the union across all users).
///
/// Writes ALWAYS use the real signed-in user — admins can't accidentally
/// upload claims into someone else's bucket while viewing-as.
/// </summary>
public interface ICurrentUserContext
{
    /// <summary>The actually-signed-in user's ID (drives writes). Never null
    /// in authorized controllers since we [Authorize] them.</summary>
    string SignedInUserId { get; }

    /// <summary>True if the signed-in user is in the admin role.</summary>
    bool IsAdmin { get; }

    /// <summary>True if the signed-in user is in the viewer role.</summary>
    bool IsViewer { get; }

    /// <summary>
    /// The user ID a query should filter by. NULL means "see everything"
    /// (admin/viewer in unscoped mode). For analysts this equals
    /// <see cref="SignedInUserId"/> always.
    /// </summary>
    string? EffectiveOwnerFilter { get; }

    /// <summary>The "View as" target if the admin/viewer has scoped to one user.</summary>
    string? ViewAsUserId { get; }
}

/// <summary>
/// Default implementation. Reads the override from the session cookie
/// "ViewAsUserId" — controllers set/clear it through ICurrentUserContextManager.
/// </summary>
public class CurrentUserContext : ICurrentUserContext
{
    public const string ViewAsSessionKey = "ViewAsUserId";

    private readonly IHttpContextAccessor _http;
    private readonly UserManager<IdentityUser> _users;
    private string? _signedInId;
    private bool _signedInIdLoaded;

    public CurrentUserContext(IHttpContextAccessor http, UserManager<IdentityUser> users)
    {
        _http = http;
        _users = users;
    }

    public string SignedInUserId
    {
        get
        {
            if (!_signedInIdLoaded)
            {
                var ctx = _http.HttpContext
                          ?? throw new InvalidOperationException("No HttpContext.");
                _signedInId = _users.GetUserId(ctx.User);
                _signedInIdLoaded = true;
            }
            return _signedInId
                   ?? throw new InvalidOperationException(
                       "CurrentUserContext used outside an authorized request.");
        }
    }

    public bool IsAdmin =>
        _http.HttpContext?.User?.IsInRole(RoleSeeder.AdminRole) == true;

    public bool IsViewer =>
        _http.HttpContext?.User?.IsInRole(RoleSeeder.ViewerRole) == true;

    public string? ViewAsUserId
    {
        get
        {
            // Only admin and viewer can scope; analysts always see their own.
            if (!IsAdmin && !IsViewer) return null;
            return _http.HttpContext?.Session?.GetString(ViewAsSessionKey);
        }
    }

    public string? EffectiveOwnerFilter
    {
        get
        {
            // Admins/viewers in unscoped mode → see everything (NULL = no filter).
            if (IsAdmin || IsViewer)
            {
                return ViewAsUserId;   // NULL when not scoped
            }
            // Analysts always scoped to themselves.
            return SignedInUserId;
        }
    }
}
