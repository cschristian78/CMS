namespace ClientManagementSystem.Models;

public sealed class AuthCatUserDto
{
    public int UserID { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool MustSetPassword { get; set; }
}

public sealed class AuthLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? NewPassword { get; set; }
}

public sealed class AuthSessionDto
{
    public int UserID { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string UserType { get; set; } = "Basic";
    public bool MustSetPassword { get; set; }
}

public sealed class AuthLoginUserRow
{
    public int UserID { get; set; }
    public string UserFullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string LoginPw { get; set; } = "none";
    public string UserType { get; set; } = "Basic";
    public bool IsActive { get; set; } = true;
}
