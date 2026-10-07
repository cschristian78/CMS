namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tProjectNote</c>.</summary>
public sealed class ProjectNote
{
    public int NoteID { get; set; }
    public int ProjectID { get; set; }
    public int UserID { get; set; }
    public string? UserFullName { get; set; }
    public DateTime NoteDate { get; set; }
    public string NoteType { get; set; } = "General";
    public string NoteText { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public int? CalendarItemID { get; set; }
    public string? CalendarItemTitle { get; set; }
    /// <summary>When set, this note belongs to Project Data → File Notes for that file.</summary>
    public int? ProjectFileID { get; set; }
    public string? ProjectFileName { get; set; }
    /// <summary>Comma-separated user IDs mentioned in a file note (e.g. "12,34").</summary>
    public string? MentionedUserIDs { get; set; }
    /// <summary>Request-only: notify CAT-department members of the project user group.</summary>
    public bool NotifyCatTeam { get; set; }
    /// <summary>Request-only: notify all members of the project user group.</summary>
    public bool NotifyAll { get; set; }
    /// <summary>Request-only: mentioned user IDs (merged into <see cref="MentionedUserIDs"/> on create).</summary>
    public int[]? MentionedUserIdList { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}

/// <summary>File-note kinds stored in <see cref="ProjectNote.NoteType"/> when <see cref="ProjectNote.ProjectFileID"/> is set.</summary>
public static class FileNoteKinds
{
    public const string Comment = "FileComment";
    public const string ApprovalRequest = "FileApprovalRequest";
    public const string Approved = "FileApproved";
    public const string ChangesRequested = "FileChangesRequested";

    /// <summary>Legacy value written before kinds were introduced.</summary>
    public const string LegacyFile = "File";

    public static bool IsFileNoteType(string? noteType)
    {
        var t = (noteType ?? string.Empty).Trim();
        return t.Equals(LegacyFile, StringComparison.OrdinalIgnoreCase)
            || t.Equals(Comment, StringComparison.OrdinalIgnoreCase)
            || t.Equals(ApprovalRequest, StringComparison.OrdinalIgnoreCase)
            || t.Equals(Approved, StringComparison.OrdinalIgnoreCase)
            || t.Equals(ChangesRequested, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? noteType)
    {
        var t = (noteType ?? string.Empty).Trim();
        if (t.Equals(ApprovalRequest, StringComparison.OrdinalIgnoreCase))
        {
            return ApprovalRequest;
        }

        if (t.Equals(Approved, StringComparison.OrdinalIgnoreCase))
        {
            return Approved;
        }

        if (t.Equals(ChangesRequested, StringComparison.OrdinalIgnoreCase))
        {
            return ChangesRequested;
        }

        // Legacy "File" and anything else on a file note → comment.
        return Comment;
    }

    public static string? ApprovalStatusForKind(string kind) => kind switch
    {
        ApprovalRequest => FileApprovalStatuses.PendingReview,
        Approved => FileApprovalStatuses.Approved,
        ChangesRequested => FileApprovalStatuses.ChangesRequested,
        _ => null,
    };

    public static string DisplayLabel(string? noteType) => Normalize(noteType) switch
    {
        ApprovalRequest => "Approval request",
        Approved => "Approved",
        ChangesRequested => "Changes requested",
        _ => "Comment",
    };
}

public static class FileApprovalStatuses
{
    public const string None = "None";
    public const string PendingReview = "PendingReview";
    public const string Approved = "Approved";
    public const string ChangesRequested = "ChangesRequested";

    public static string Normalize(string? status)
    {
        var s = (status ?? string.Empty).Trim();
        if (s.Equals(PendingReview, StringComparison.OrdinalIgnoreCase))
        {
            return PendingReview;
        }

        if (s.Equals(Approved, StringComparison.OrdinalIgnoreCase))
        {
            return Approved;
        }

        if (s.Equals(ChangesRequested, StringComparison.OrdinalIgnoreCase))
        {
            return ChangesRequested;
        }

        return None;
    }

    public static string DisplayLabel(string? status) => Normalize(status) switch
    {
        PendingReview => "Pending review",
        Approved => "Approved",
        ChangesRequested => "Changes requested",
        _ => "No status",
    };
}
