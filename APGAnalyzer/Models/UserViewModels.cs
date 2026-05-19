using System.ComponentModel.DataAnnotations;

namespace APGAnalyzer.Models;

/// <summary>One row in the Users admin list.</summary>
public class UserListRow
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsLockedOut { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public bool IsCurrentUser { get; set; }   // for "you can't delete yourself" UI
}

/// <summary>Index page model — list + total counts per role.</summary>
public class UserListViewModel
{
    public List<UserListRow> Rows { get; set; } = new();
    public Dictionary<string, int> RoleCounts { get; set; } = new();
}

/// <summary>Form model for creating a new user.</summary>
public class CreateUserViewModel
{
    [Required, EmailAddress, Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), MinLength(6),
     Display(Name = "Temporary password")]
    public string Password { get; set; } = "";

    [Required, Display(Name = "Role")]
    public string Role { get; set; } = "viewer";
}

/// <summary>Form model for editing role / unlock / reset password.</summary>
public class EditUserViewModel
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";

    [Required, Display(Name = "Role")]
    public string Role { get; set; } = "";

    [Display(Name = "Locked out?")]
    public bool IsLockedOut { get; set; }
}

/// <summary>Form model for an admin-issued password reset.</summary>
public class ResetPasswordViewModel
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), MinLength(6),
     Display(Name = "New password")]
    public string NewPassword { get; set; } = "";
}
