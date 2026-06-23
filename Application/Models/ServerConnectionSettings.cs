using System.ComponentModel.DataAnnotations;

namespace ClientManagementSystem.Models;

public sealed class ServerConnectionSettings
{
    [Required]
    [Display(Name = "Server")]
    public string Server { get; set; } = "vmus-sql-099";

    [Required]
    [Display(Name = "Database")]
    public string Database { get; set; } = "BMS_CMS";

    [Display(Name = "Username")]
    public string? UserName { get; set; }

    [Display(Name = "Password")]
    public string? Password { get; set; }

    [Display(Name = "Use Windows Integrated Security")]
    public bool UseIntegratedSecurity { get; set; } = true;

    public bool HasSqlCredentials => !string.IsNullOrWhiteSpace(UserName) && !string.IsNullOrWhiteSpace(Password);
}

public sealed class ConnectionTestResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;

    public static ConnectionTestResult Success(string message = "Connection succeeded.") => new()
    {
        Succeeded = true,
        Message = message
    };

    public static ConnectionTestResult Failure(string message) => new()
    {
        Succeeded = false,
        Message = message
    };
}
