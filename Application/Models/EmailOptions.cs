namespace ClientManagementSystem.Models;

/// <summary>SMTP settings for outbound CMS notifications.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>When false, notification code logs and skips send (calendar save still succeeds).</summary>
    public bool Enabled { get; set; }

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;

    /// <summary>Optional SMTP username. Leave blank for relay without auth.</summary>
    public string? Username { get; set; }

    /// <summary>Optional SMTP password.</summary>
    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = "Client Management System";

    /// <summary>CC on analytics calendar owner assignment notifications.</summary>
    public string CalendarCcAddress { get; set; } = "Scott.Christian@bmsgroup.com";

    /// <summary>Public site root used in notification links (no trailing slash).</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}
