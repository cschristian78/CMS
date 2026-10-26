namespace ClientManagementSystem.Models;

/// <summary>Project phase stored on <c>dbo.tProject.WorkStatus</c>.
/// The green icon is shown only for <see cref="InProgress"/>.</summary>
public static class ProjectWorkStatus
{
    public const string Planned = "Planned";
    public const string InProgress = "In Progress";
    public const string Closed = "Closed";

    /// <summary>Blank <c>WorkStatus</c> reads as Planned. The legacy online bit is not a status.</summary>
    public static string Sql(string alias) =>
        $"ISNULL(NULLIF(LTRIM(RTRIM({alias}.WorkStatus)), N''), N'{Planned}')";

    public static bool IsInProgress(string? status) =>
        string.Equals(NormalizeOrNull(status), InProgress, StringComparison.Ordinal);

    /// <summary>Returns a canonical status, or null when <paramref name="status"/> is blank or unrecognized.
    /// The legacy word Online maps to In Progress.</summary>
    public static string? NormalizeOrNull(string? status)
    {
        var value = (status ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Equals(Planned, StringComparison.OrdinalIgnoreCase))
        {
            return Planned;
        }

        if (value.Equals(InProgress, StringComparison.OrdinalIgnoreCase)
            || value.Equals("InProgress", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Online", StringComparison.OrdinalIgnoreCase))
        {
            return InProgress;
        }

        if (value.Equals(Closed, StringComparison.OrdinalIgnoreCase))
        {
            return Closed;
        }

        return null;
    }

    /// <summary>Canonical status. Blank or unrecognized values are Planned.</summary>
    public static string Normalize(string? status) =>
        NormalizeOrNull(status) ?? Planned;
}
