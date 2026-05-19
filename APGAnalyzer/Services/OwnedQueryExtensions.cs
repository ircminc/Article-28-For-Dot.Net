using System.Linq.Expressions;

namespace APGAnalyzer.Services;

/// <summary>
/// Marker interface implemented by any entity that participates in
/// per-user data isolation. The concrete column lives on the entity
/// itself (annotated with [MaxLength(450)] to match AspNetUsers.Id);
/// the interface is just the contract the query helper relies on.
/// </summary>
public interface IOwnedByUser
{
    string? OwnerUserId { get; set; }
}

/// <summary>
/// Query-side helpers for per-user isolation. Every read site that touches
/// an <see cref="IOwnedByUser"/> entity should pipe through <c>OwnedBy(ctx)</c>
/// to inherit the right filter for the current user / view-as scope.
/// </summary>
public static class OwnedQueryExtensions
{
    /// <summary>
    /// Apply per-user isolation to <paramref name="query"/>.
    ///
    /// - Analyst:               filters to their own user id.
    /// - Admin without scope:   no filter (returns everything).
    /// - Admin with View-as:    filters to the chosen user.
    /// - Viewer without scope:  no filter (oversight role, sees everything).
    /// - Viewer with View-as:   filters to the chosen user.
    /// </summary>
    public static IQueryable<T> OwnedBy<T>(
        this IQueryable<T> query, ICurrentUserContext ctx) where T : class, IOwnedByUser
    {
        var owner = ctx.EffectiveOwnerFilter;
        if (owner is null) return query;       // unscoped admin/viewer — see everything
        return query.Where(e => e.OwnerUserId == owner);
    }
}
