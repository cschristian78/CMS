using System.Globalization;
using System.Net;
using System.Text;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClientManagementSystem.Services;

public interface ICalendarOwnerNotificationService
{
    Task TryNotifyOwnersAfterSaveAsync(
        Project project,
        IReadOnlyList<ProjectCalendarItem> beforeSave,
        IReadOnlyList<ProjectCalendarItem> afterSave,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Sends one summary email to all calendar task owners (CC analytics lead) when every task
/// has an owner assigned and owner assignments changed or were just completed.
/// </summary>
public sealed class CalendarOwnerNotificationService : ICalendarOwnerNotificationService
{
    private readonly IAdminRepository _admin;
    private readonly IEmailSender _email;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<CalendarOwnerNotificationService> _logger;

    public CalendarOwnerNotificationService(
        IAdminRepository admin,
        IEmailSender email,
        IOptions<EmailOptions> emailOptions,
        ILogger<CalendarOwnerNotificationService> logger)
    {
        _admin = admin;
        _email = email;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    public async Task TryNotifyOwnersAfterSaveAsync(
        Project project,
        IReadOnlyList<ProjectCalendarItem> beforeSave,
        IReadOnlyList<ProjectCalendarItem> afterSave,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var beforeActive = ActiveItems(beforeSave);
        var afterActive = ActiveItems(afterSave);
        if (afterActive.Count == 0)
        {
            return;
        }

        if (afterActive.Any(i => i.OwnerUserID is not > 0))
        {
            return;
        }

        var allAssignedBefore = beforeActive.Count > 0 && beforeActive.All(i => i.OwnerUserID is > 0);
        var ownersChanged = OwnerAssignmentsChanged(beforeActive, afterActive);
        if (allAssignedBefore && !ownersChanged)
        {
            return;
        }

        var ownerIds = afterActive
            .Select(i => i.OwnerUserID!.Value)
            .Distinct()
            .ToList();

        var contacts = await _admin.GetUserContactInfoByIdsAsync(ownerIds, cancellationToken);
        var ownerEmails = contacts
            .Select(c => c.Email?.Trim())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
        if (ownerEmails.Count == 0)
        {
            _logger.LogWarning(
                "Calendar owner notification skipped for ProjectID {ProjectID}: no owner email addresses found.",
                project.ProjectID);
            return;
        }

        var contactsByUserId = contacts.ToDictionary(c => c.UserId, c => c);
        var editUrl = BuildCalendarEditUrl(project.ProjectID);
        var subject =
            $"Analytics calendar tasks assigned – {project.ProjectNumber} {project.ProjectName}".Trim();
        var htmlBody = BuildHtmlBody(project, afterActive, contactsByUserId, editUrl);
        var plainBody = BuildPlainTextBody(project, afterActive, contactsByUserId, editUrl);

        var cc = string.IsNullOrWhiteSpace(_emailOptions.CalendarCcAddress)
            ? Array.Empty<string>()
            : [_emailOptions.CalendarCcAddress.Trim()];

        try
        {
            await _email.SendAsync(new EmailMessage
            {
                To = ownerEmails,
                Cc = cc,
                Subject = subject,
                HtmlBody = htmlBody,
                PlainTextBody = plainBody,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Calendar owner notification email failed for ProjectID {ProjectID}.",
                project.ProjectID);
        }
    }

    private string BuildCalendarEditUrl(int projectId)
    {
        var baseUrl = (_emailOptions.PublicBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return $"/projects/{projectId}?tab=calendar";
        }

        return $"{baseUrl}/projects/{projectId}?tab=calendar";
    }

    private static List<ProjectCalendarItem> ActiveItems(IReadOnlyList<ProjectCalendarItem> items)
        => items
            .Where(i => !string.IsNullOrWhiteSpace(i.Title))
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.CalendarItemID)
            .ToList();

    private static bool OwnerAssignmentsChanged(
        IReadOnlyList<ProjectCalendarItem> beforeActive,
        IReadOnlyList<ProjectCalendarItem> afterActive)
    {
        var beforeById = beforeActive
            .Where(i => i.CalendarItemID > 0)
            .ToDictionary(i => i.CalendarItemID, i => i.OwnerUserID);

        foreach (var item in afterActive)
        {
            if (item.CalendarItemID <= 0)
            {
                return true;
            }

            if (!beforeById.TryGetValue(item.CalendarItemID, out var previousOwner))
            {
                return true;
            }

            if (previousOwner != item.OwnerUserID)
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildHtmlBody(
        Project project,
        IReadOnlyList<ProjectCalendarItem> items,
        IReadOnlyDictionary<int, UserContactInfo> contactsByUserId,
        string editUrl)
    {
        var sb = new StringBuilder();
        sb.Append("<html><body style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222;\">");
        sb.Append("<p>Analytics calendar tasks have been assigned for the project below.</p>");
        sb.Append("<p><strong>Project:</strong> ")
            .Append(WebUtility.HtmlEncode(project.ProjectNumber))
            .Append(" – ")
            .Append(WebUtility.HtmlEncode(project.ProjectName))
            .Append("<br><strong>Client:</strong> ")
            .Append(WebUtility.HtmlEncode(project.ClientName ?? "—"))
            .Append("</p>");
        sb.Append("<table cellpadding=\"6\" cellspacing=\"0\" border=\"1\" style=\"border-collapse:collapse;border-color:#ccc;\">");
        sb.Append("<thead><tr style=\"background:#f3f4f6;\">");
        sb.Append("<th align=\"left\">Type</th><th align=\"left\">Title</th><th align=\"left\">Owner</th>");
        sb.Append("<th align=\"left\">Received</th><th align=\"left\">Completed</th><th align=\"left\">Notes</th>");
        sb.Append("</tr></thead><tbody>");

        foreach (var item in items)
        {
            var ownerName = ResolveOwnerName(item, contactsByUserId);
            sb.Append("<tr>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(item.ItemType)).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(item.Title)).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(ownerName)).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(FormatDate(item.ReceivedDate))).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(FormatDate(item.CompletedDate))).Append("</td>");
            sb.Append("<td>").Append(WebUtility.HtmlEncode(item.Notes ?? "—")).Append("</td>");
            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
        sb.Append("<p><a href=\"")
            .Append(WebUtility.HtmlEncode(editUrl))
            .Append("\">Open the Analytics Calendar for this project</a> to review or update tasks.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string BuildPlainTextBody(
        Project project,
        IReadOnlyList<ProjectCalendarItem> items,
        IReadOnlyDictionary<int, UserContactInfo> contactsByUserId,
        string editUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analytics calendar tasks have been assigned for the project below.");
        sb.AppendLine();
        sb.AppendLine($"Project: {project.ProjectNumber} – {project.ProjectName}");
        sb.AppendLine($"Client: {project.ClientName ?? "—"}");
        sb.AppendLine();
        foreach (var item in items)
        {
            sb.AppendLine(
                $"- [{item.ItemType}] {item.Title} | Owner: {ResolveOwnerName(item, contactsByUserId)} | Received: {FormatDate(item.ReceivedDate)} | Completed: {FormatDate(item.CompletedDate)}");
            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                sb.AppendLine($"  Notes: {item.Notes}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Edit calendar: {editUrl}");
        return sb.ToString();
    }

    private static string ResolveOwnerName(
        ProjectCalendarItem item,
        IReadOnlyDictionary<int, UserContactInfo> contactsByUserId)
    {
        if (item.OwnerUserID is > 0 && contactsByUserId.TryGetValue(item.OwnerUserID.Value, out var contact))
        {
            return contact.FullName;
        }

        return string.IsNullOrWhiteSpace(item.OwnerFullName) ? "—" : item.OwnerFullName.Trim();
    }

    private static string FormatDate(DateTime? value)
        => value?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? "—";
}
