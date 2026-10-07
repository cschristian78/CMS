using System.Net;
using System.Text;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClientManagementSystem.Services;

public interface IFileNoteNotificationService
{
    Task TryNotifyAsync(
        Project project,
        ProjectFile file,
        ProjectNote note,
        string authorName,
        bool notifyCatTeam,
        bool notifyAll,
        IReadOnlyList<int> mentionedUserIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Emails CAT team, full user group, and/or @mentioned users about a new file note.</summary>
public sealed class FileNoteNotificationService : IFileNoteNotificationService
{
    private readonly IAdminRepository _admin;
    private readonly IProjectRepository _projects;
    private readonly IEmailSender _email;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<FileNoteNotificationService> _logger;

    public FileNoteNotificationService(
        IAdminRepository admin,
        IProjectRepository projects,
        IEmailSender email,
        IOptions<EmailOptions> emailOptions,
        ILogger<FileNoteNotificationService> logger)
    {
        _admin = admin;
        _projects = projects;
        _email = email;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    public async Task TryNotifyAsync(
        Project project,
        ProjectFile file,
        ProjectNote note,
        string authorName,
        bool notifyCatTeam,
        bool notifyAll,
        IReadOnlyList<int> mentionedUserIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(note);

        if (!notifyCatTeam && !notifyAll && (mentionedUserIds is null || mentionedUserIds.Count == 0))
        {
            return;
        }

        var recipientIds = new HashSet<int>();

        // Notify All covers the whole group (including CAT). Otherwise optionally CAT-only.
        if (notifyAll && project.UGID > 0)
        {
            var members = await _admin.ListUserGroupMembersAsync(project.UGID, cancellationToken);
            foreach (var m in members)
            {
                if (m.UserID > 0)
                {
                    recipientIds.Add(m.UserID);
                }
            }
        }
        else if (notifyCatTeam)
        {
            var catUsers = await _projects.ListCatDepartmentUsersForProjectAsync(
                project.ProjectID,
                cancellationToken);
            foreach (var u in catUsers)
            {
                if (u.UserID > 0)
                {
                    recipientIds.Add(u.UserID);
                }
            }
        }

        foreach (var id in mentionedUserIds ?? [])
        {
            if (id > 0)
            {
                recipientIds.Add(id);
            }
        }

        // Do not email the author back to themselves.
        if (note.UserID > 0)
        {
            recipientIds.Remove(note.UserID);
        }

        if (recipientIds.Count == 0)
        {
            return;
        }

        var contacts = await _admin.GetUserContactInfoByIdsAsync(recipientIds, cancellationToken);
        var emails = contacts
            .Select(c => c.Email?.Trim())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
        if (emails.Count == 0)
        {
            _logger.LogWarning(
                "File note notification skipped for ProjectID {ProjectID} FileID {FileId}: no recipient emails.",
                project.ProjectID,
                file.ProjectFileID);
            return;
        }

        var kindLabel = FileNoteKinds.DisplayLabel(note.NoteType);
        var fileName = string.IsNullOrWhiteSpace(file.OriginalFileName)
            ? $"File {file.ProjectFileID}"
            : file.OriginalFileName.Trim();
        var projectLabel = string.Join(
            ' ',
            new[] { project.ProjectNumber, project.ProjectName }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (projectLabel.Length == 0)
        {
            projectLabel = $"Project {project.ProjectID}";
        }

        var audience = notifyAll ? "user group" : notifyCatTeam ? "CAT team" : "mentioned teammates";
        var subject = $"{projectLabel}: {kindLabel} – {fileName}";
        var link = BuildFileNotesUrl(project.ProjectID);
        var author = string.IsNullOrWhiteSpace(authorName) ? "A teammate" : authorName.Trim();
        var bodyText = (note.NoteText ?? string.Empty).Trim();
        if (bodyText.Length > 2000)
        {
            bodyText = bodyText[..2000] + "…";
        }

        var html = new StringBuilder();
        html.Append("<p>")
            .Append(WebUtility.HtmlEncode(author))
            .Append(" posted a <strong>")
            .Append(WebUtility.HtmlEncode(kindLabel))
            .Append("</strong> on <strong>")
            .Append(WebUtility.HtmlEncode(fileName))
            .Append("</strong> for <strong>")
            .Append(WebUtility.HtmlEncode(projectLabel))
            .Append("</strong> (notifying ")
            .Append(WebUtility.HtmlEncode(audience))
            .Append(").</p>");
        if (!string.IsNullOrWhiteSpace(project.ClientName))
        {
            html.Append("<p>Client: ").Append(WebUtility.HtmlEncode(project.ClientName.Trim())).Append("</p>");
        }

        html.Append("<blockquote style=\"margin:0.75rem 0;padding:0.65rem 0.85rem;border-left:3px solid #f08700;background:#f7f7f7;\">")
            .Append(WebUtility.HtmlEncode(bodyText).Replace("\n", "<br/>", StringComparison.Ordinal))
            .Append("</blockquote>");
        if (!string.IsNullOrWhiteSpace(link))
        {
            html.Append("<p><a href=\"")
                .Append(WebUtility.HtmlEncode(link))
                .Append("\">Open File Notes &amp; Approvals</a></p>");
        }

        var plain = new StringBuilder();
        plain.Append(author).Append(" posted a ").Append(kindLabel)
            .Append(" on ").Append(fileName)
            .Append(" for ").Append(projectLabel)
            .Append(" (notifying ").Append(audience).AppendLine(").")
            .AppendLine()
            .AppendLine(bodyText);
        if (!string.IsNullOrWhiteSpace(link))
        {
            plain.AppendLine().Append("Open: ").Append(link);
        }

        try
        {
            await _email.SendAsync(new EmailMessage
            {
                To = emails,
                Subject = subject,
                HtmlBody = html.ToString(),
                PlainTextBody = plain.ToString(),
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "File note notification failed for ProjectID {ProjectID} FileID {FileId}.",
                project.ProjectID,
                file.ProjectFileID);
        }
    }

    private string BuildFileNotesUrl(int projectId)
    {
        var baseUrl = (_emailOptions.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        return $"{baseUrl}/projects/{projectId}?tab=projectData";
    }
}
