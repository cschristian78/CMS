using System.Globalization;
using System.Text.RegularExpressions;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ClientManagementSystem.Services;

public sealed class ProjectReportService : IProjectReportService
{
    private static readonly Color TitleColor = Color.FromRGB(11, 61, 92);
    private static readonly Color MutedColor = Color.FromRGB(74, 90, 106);
    private static readonly Color BodyColor = Color.FromRGB(26, 35, 50);
    private static readonly Color HeaderFill = Color.FromRGB(238, 243, 248);

    private readonly IProjectService _projects;
    private readonly IAdminRepository _admin;

    static ProjectReportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ProjectReportService(IProjectService projects, IAdminRepository admin)
    {
        _projects = projects;
        _admin = admin;
    }

    public async Task<ProjectReportGenerationResult> GenerateAndSaveAsync(
        int projectId,
        string? fileName = null,
        bool overwriteSameName = true,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            return Fail(projectId, "projectId must be a positive integer.");
        }

        ProjectFormViewModel? form;
        try
        {
            form = await _projects.PrepareEditFormAsync(projectId, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail(projectId, $"Could not load project form: {ex.Message}");
        }

        if (form is null)
        {
            return Fail(projectId, $"Project {projectId} was not found.");
        }

        ReportData data;
        try
        {
            data = await BuildReportDataAsync(projectId, form, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail(projectId, form.ProjectNumber, form.ProjectName, $"Could not assemble report data: {ex.Message}");
        }

        var defaultName = SuggestedFileName(data);
        var safeName = SanitizeFileName(fileName, defaultName);

        byte[] pdfBytes;
        try
        {
            pdfBytes = BuildPdf(data);
        }
        catch (Exception ex)
        {
            return Fail(projectId, form.ProjectNumber, form.ProjectName, $"Could not generate PDF: {ex.Message}");
        }

        try
        {
            var existing = await _projects.ListFilesAsync(projectId, cancellationToken);
            if (overwriteSameName)
            {
                foreach (var dup in existing.Where(f =>
                             string.Equals(
                                 (f.OriginalFileName ?? string.Empty).Trim(),
                                 safeName,
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    await _projects.SoftDeleteFileAsync(projectId, dup.ProjectFileID, cancellationToken);
                }
            }

            await using var stream = new MemoryStream(pdfBytes);
            var files = await _projects.UploadFilesAsync(
                projectId,
                [(stream, safeName, "application/pdf", pdfBytes.LongLength)],
                category: "CorrespondenceData",
                description: $"Project report generated {DateTime.Now.ToString("g", CultureInfo.CurrentCulture)}",
                versionLabel: "1",
                dataAsOf: DateTime.Today,
                isCurrent: true,
                cancellationToken);

            var saved = files.FirstOrDefault(f =>
                string.Equals(f.OriginalFileName, safeName, StringComparison.OrdinalIgnoreCase))
                ?? files.OrderByDescending(f => f.ProjectFileID).FirstOrDefault();

            if (saved is null || string.IsNullOrWhiteSpace(saved.DownloadUrl))
            {
                return Fail(
                    projectId,
                    form.ProjectNumber,
                    form.ProjectName,
                    "Report was uploaded but no download URL was returned. Check Azure Blob configuration.");
            }

            var linkLabel =
                $"{FileNameSegment(data.ClientName, "Client", 60)}_{FileNameSegment(data.ProjectName, $"Project{projectId}", 80)}_ProjectReport.pdf";

            return new ProjectReportGenerationResult
            {
                Success = true,
                ProjectId = projectId,
                ProjectNumber = form.ProjectNumber,
                ProjectName = form.ProjectName,
                ProjectFileId = saved.ProjectFileID,
                FileName = saved.OriginalFileName,
                LinkLabel = linkLabel,
                DownloadUrl = saved.DownloadUrl,
                Category = saved.Category,
                Message =
                    "Project report PDF was saved to project data (CorrespondenceData). "
                    + $"In your reply, include this markdown link exactly so the user can click it: [{linkLabel}]({saved.DownloadUrl})",
            };
        }
        catch (Exception ex)
        {
            return Fail(projectId, form.ProjectNumber, form.ProjectName, $"Could not save report to blob storage: {ex.Message}");
        }
    }

    private async Task<ReportData> BuildReportDataAsync(
        int projectId,
        ProjectFormViewModel form,
        CancellationToken cancellationToken)
    {
        var ugid = form.UserGroupID;
        var membersTask = ugid > 0
            ? _admin.ListUserGroupMembersAsync(ugid, cancellationToken)
            : Task.FromResult<IReadOnlyList<UserGroupMember>>([]);

        var filesTask = _projects.ListFilesAsync(projectId, cancellationToken);
        var notesTask = _projects.ListNotesAsync(projectId, cancellationToken);
        var calendarTask = _projects.ListCalendarItemsAsync(projectId, cancellationToken);
        var catalogTask = _projects.GetActiveServicesCatalogAsync(cancellationToken);
        var timeTask = _projects.GetTimeSummaryAsync(projectId, cancellationToken);

        await Task.WhenAll(membersTask, filesTask, notesTask, calendarTask, catalogTask, timeTask);

        var members = await membersTask;
        var files = await filesTask;
        var notes = await notesTask;
        var calendar = await calendarTask;
        var catalog = await catalogTask;
        var time = await timeTask;

        var memberById = members.ToDictionary(m => m.UserID, m => m);
        string OwnerName(int? ownerId)
        {
            if (ownerId is null or <= 0)
            {
                return "—";
            }

            if (memberById.TryGetValue(ownerId.Value, out var m)
                && !string.IsNullOrWhiteSpace(m.UserFullName))
            {
                return m.UserFullName!;
            }

            return $"User {ownerId.Value}";
        }

        var projectTypeText = OptionText(form.ProjectTypeOptions, form.ProjectTypeID);
        var projectTypeCode = ProjectTypeCode(projectTypeText);
        var selected = form.SelectedServiceIds ?? [];

        return new ReportData
        {
            ClientName = form.ClientName ?? string.Empty,
            ClientShortName = form.ClientShortName,
            BusinessUnit = OptionText(form.BusinessUnitOptions, form.ClientBusinessUnitID)
                .OrFallback(form.ClientBusinessUnitName),
            BusinessClass = OptionText(form.BusinessClassOptions, form.ClientBusinessClassID)
                .OrFallback(form.ClientBusinessClassName),
            ClientType = OptionText(form.ClientTypeOptions, form.ClientTypeID)
                .OrFallback(form.ClientTypeName),
            ClientStatus = OptionText(form.ClientStatusOptions, form.ClientStatusID)
                .OrFallback(form.ClientStatusCode),
            ProjectId = projectId,
            ProjectNumber = form.ProjectNumber,
            ProjectName = form.ProjectName ?? string.Empty,
            ProjectType = projectTypeText,
            ProjectTypeCode = projectTypeCode,
            UserGroup = OptionText(form.UserGroupOptions, ugid)
                .OrFallback(ugid > 0 ? $"User Group {ugid}" : null),
            Status = OptionText(form.StatusOptions, form.StatusID),
            IsOnline = form.IsOnline,
            RenewalDate = form.RenewalDate,
            DueDate = form.DueDate,
            LatestRevenue = form.RevenueAmount,
            Members = members
                .Select(m => new ReportMember
                {
                    Name = string.IsNullOrWhiteSpace(m.UserFullName) ? $"User {m.UserID}" : m.UserFullName!,
                    Title = string.IsNullOrWhiteSpace(m.Title) ? "—" : m.Title!,
                    Role = string.IsNullOrWhiteSpace(m.UserRoleName) ? "—" : m.UserRoleName!,
                    Phone = string.IsNullOrWhiteSpace(m.Phone) ? "—" : m.Phone!,
                    Email = string.IsNullOrWhiteSpace(m.Email) ? "—" : m.Email!,
                })
                .ToList(),
            DataSources = files
                .Select(f => new ReportFileRow
                {
                    FileName = f.OriginalFileName ?? string.Empty,
                    Category = FormatCategory(f.Category),
                    Version = string.IsNullOrWhiteSpace(f.VersionLabel) ? "—" : f.VersionLabel!,
                    DataAsOf = FormatDate(f.DataAsOf),
                    Active = f.IsCurrent,
                    UploadedBy = string.IsNullOrWhiteSpace(f.UploadedByName) ? "—" : f.UploadedByName!,
                })
                .ToList(),
            Services = catalog
                .Where(s => selected.Contains(s.ServiceID))
                .Select(s => new ReportServiceRow
                {
                    ServiceName = s.ServiceName,
                    Category = FirstNonBlank(s.ServiceCategoryDescription, s.CategoryName) ?? "—",
                })
                .ToList(),
            Calendar = calendar
                .Select(c => new ReportCalendarRow
                {
                    ItemType = string.IsNullOrWhiteSpace(c.ItemType) ? "Task" : c.ItemType,
                    Title = string.IsNullOrWhiteSpace(c.Title) ? "—" : c.Title,
                    Owner = string.IsNullOrWhiteSpace(c.OwnerFullName)
                        ? OwnerName(c.OwnerUserID)
                        : c.OwnerFullName!,
                    ReceivedDate = FormatDate(c.ReceivedDate),
                    CompletedDate = FormatDate(c.CompletedDate),
                    Notes = string.IsNullOrWhiteSpace(c.Notes) ? "—" : c.Notes!,
                })
                .ToList(),
            TimeByAnalyst = time.ByAnalyst
                .Select(r => new ReportTimeRow { Label = r.Label, Detail = r.Detail, Hours = r.Hours })
                .ToList(),
            TimeByTask = time.ByTask
                .Select(r => new ReportTimeRow { Label = r.Label, Detail = r.Detail, Hours = r.Hours })
                .ToList(),
            TotalHours = time.TotalHours,
            Notes = notes
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.NoteDate)
                .Select(n => new ReportNoteRow
                {
                    NoteType = string.IsNullOrWhiteSpace(n.NoteType) ? "General" : n.NoteType,
                    NoteDate = FormatDate(n.NoteDate),
                    Author = string.IsNullOrWhiteSpace(n.UserFullName) ? "—" : n.UserFullName!,
                    Pinned = n.IsPinned,
                    Text = n.NoteText ?? string.Empty,
                })
                .ToList(),
        };
    }

    private static byte[] BuildPdf(ReportData data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(BodyColor));

                page.Header().Column(col =>
                {
                    col.Item().Text("Project Report").FontSize(16).Bold().FontColor(TitleColor);
                    col.Item().PaddingTop(4).Text($"{Blank(data.ClientName, "Client")} · {Blank(data.ProjectName, $"Project {data.ProjectId}")}")
                        .FontSize(10).FontColor(MutedColor);
                    col.Item().PaddingTop(2).Text($"Generated {DateTime.Now.ToString("g", CultureInfo.CurrentCulture)}")
                        .FontSize(9).FontColor(MutedColor);
                    col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(TitleColor);
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    col.Spacing(12);

                    SectionTitle(col, "Client");
                    KvTable(col, [
                        ("Client name", Blank(data.ClientName)),
                        ("Short name", Blank(data.ClientShortName)),
                        ("Business unit", Blank(data.BusinessUnit)),
                        ("Business class", Blank(data.BusinessClass)),
                        ("Client type", Blank(data.ClientType)),
                        ("Status", Blank(data.ClientStatus)),
                    ]);

                    SectionTitle(col, "Project");
                    KvTable(col, [
                        ("Project ID", data.ProjectId.ToString(CultureInfo.InvariantCulture)),
                        ("Project number", Blank(data.ProjectNumber)),
                        ("Project name", Blank(data.ProjectName)),
                        ("Project type", Blank(data.ProjectType)),
                        ("User group", Blank(data.UserGroup)),
                        ("Status", Blank(data.Status)),
                        ("Online", data.IsOnline ? "Online" : "Offline"),
                        ("Renewal date", FormatDate(data.RenewalDate)),
                        ("Due date", FormatDate(data.DueDate)),
                        ("Latest revenue", FormatMoney(data.LatestRevenue)),
                    ]);

                    DataTable(
                        col,
                        $"User group — {Blank(data.UserGroup, "User group")}",
                        ["Name", "Title", "Role", "Phone", "Email"],
                        data.Members.Select(m => new[] { m.Name, m.Title, m.Role, m.Phone, m.Email }),
                        "No members in the selected user group.");

                    DataTable(
                        col,
                        "Data sources",
                        ["File", "Classification", "Version", "Data as of", "Active", "Uploaded by"],
                        data.DataSources.Select(f => new[]
                        {
                            f.FileName,
                            f.Category,
                            f.Version,
                            f.DataAsOf,
                            f.Active ? "Yes" : "No",
                            f.UploadedBy,
                        }),
                        "No project data files.");

                    DataTable(
                        col,
                        "Selected services",
                        ["Service", "Category"],
                        data.Services.Select(s => new[] { s.ServiceName, s.Category }),
                        "No services selected.");

                    DataTable(
                        col,
                        "Analytics calendar",
                        ["Type", "Title", "Owner", "Received", "Completed", "Notes"],
                        data.Calendar.Select(c => new[]
                        {
                            c.ItemType, c.Title, c.Owner, c.ReceivedDate, c.CompletedDate, c.Notes,
                        }),
                        "No calendar items.");

                    DataTable(
                        col,
                        "Time tracking by analyst",
                        ["Analyst", "Hours"],
                        data.TimeByAnalyst.Select(r => new[] { r.Label, FormatHours(r.Hours) }),
                        "No time entries.");

                    DataTable(
                        col,
                        "Time tracking by task",
                        ["Task", "Type", "Hours"],
                        data.TimeByTask.Select(r => new[] { r.Label, Blank(r.Detail), FormatHours(r.Hours) }),
                        "No time entries.");

                    col.Item().Text($"Total hours: {FormatHours(data.TotalHours)}").Bold().FontSize(10);

                    SectionTitle(col, "Project notes");
                    if (data.Notes.Count == 0)
                    {
                        col.Item().Text("No project notes.").FontColor(MutedColor);
                    }
                    else
                    {
                        foreach (var note in data.Notes)
                        {
                            var header =
                                $"{Blank(note.NoteType, "General")}{(note.Pinned ? " (Pinned)" : string.Empty)}"
                                + $" · {Blank(note.NoteDate)} · {Blank(note.Author)}";
                            col.Item().PaddingBottom(2).Text(header).Bold().FontColor(TitleColor);
                            col.Item().PaddingBottom(8).Text(Blank(note.Text, "(empty)"));
                        }
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void SectionTitle(ColumnDescriptor col, string title)
    {
        col.Item().PaddingBottom(2).Text(title).FontSize(12).Bold().FontColor(TitleColor);
        col.Item().PaddingBottom(6).LineHorizontal(0.75f).LineColor(Color.FromRGB(213, 221, 230));
    }

    private static void KvTable(ColumnDescriptor col, IReadOnlyList<(string Label, string Value)> rows)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(120);
                c.RelativeColumn();
            });

            foreach (var (label, value) in rows)
            {
                table.Cell().PaddingVertical(1.5f).Text(label).Bold().FontColor(MutedColor);
                table.Cell().PaddingVertical(1.5f).Text(value);
            }
        });
    }

    private static void DataTable(
        ColumnDescriptor col,
        string title,
        string[] headers,
        IEnumerable<string[]> body,
        string emptyMessage)
    {
        SectionTitle(col, title);
        var rows = body.ToList();
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                foreach (var _ in headers)
                {
                    c.RelativeColumn();
                }
            });

            table.Header(h =>
            {
                foreach (var header in headers)
                {
                    h.Cell().Background(HeaderFill).Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                        .Padding(4).Text(header).Bold().FontColor(TitleColor).FontSize(8.5f);
                }
            });

            if (rows.Count == 0)
            {
                table.Cell().ColumnSpan((uint)headers.Length)
                    .Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                    .Padding(4).Text(emptyMessage).FontColor(MutedColor).FontSize(8.5f);
            }
            else
            {
                foreach (var row in rows)
                {
                    for (var i = 0; i < headers.Length; i++)
                    {
                        var cell = i < row.Length ? row[i] : string.Empty;
                        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Padding(4).Text(cell).FontSize(8.5f);
                    }
                }
            }
        });
    }

    private static ProjectReportGenerationResult Fail(
        int projectId,
        string error)
        => Fail(projectId, null, null, error);

    private static ProjectReportGenerationResult Fail(
        int projectId,
        string? projectNumber,
        string? projectName,
        string error)
        => new()
        {
            Success = false,
            ProjectId = projectId,
            ProjectNumber = projectNumber,
            ProjectName = projectName,
            Error = error,
            Message = error,
        };

    private static string SuggestedFileName(ReportData data)
    {
        var clientShort = FileNameSegment(data.ClientShortName, "Client", 40);
        var renewal = data.RenewalDate is { } d
            ? d.ToString("MMddyyyy", CultureInfo.InvariantCulture)
            : "00000000";
        var typeCode = FileNameSegment(data.ProjectTypeCode, "Type", 30);
        var projectName = FileNameSegment(data.ProjectName, $"Project{data.ProjectId}", 80);
        return $"{clientShort}_{renewal}_{typeCode}_{projectName}_ProjectReport.pdf";
    }

    private static string SanitizeFileName(string? value, string fallback)
    {
        var fallbackSafe = fallback.Trim().EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? fallback.Trim()
            : $"{(string.IsNullOrWhiteSpace(fallback) ? "ProjectReport" : fallback.Trim())}.pdf";

        var name = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallbackSafe;
        }

        name = Regex.Replace(name, @"[<>:""/\\|?*\u0000-\u001f]", "_");
        name = Regex.Replace(name, @"\s+", " ").Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallbackSafe;
        }

        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            name += ".pdf";
        }

        return name.Length <= 180 ? name : name[..180];
    }

    private static string FileNameSegment(string? value, string fallback, int maxLen)
    {
        var cleaned = Regex.Replace((value ?? string.Empty).Trim(), @"[<>:""/\\|?*\u0000-\u001f]", "_");
        cleaned = Regex.Replace(cleaned, @"\s+", "_");
        cleaned = Regex.Replace(cleaned, "_+", "_").Trim('_');
        if (cleaned.Length > maxLen)
        {
            cleaned = cleaned[..maxLen];
        }

        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static string OptionText(IReadOnlyList<SelectListItem>? options, int? value)
    {
        if (value is null or 0 || options is null || options.Count == 0)
        {
            return string.Empty;
        }

        var key = value.Value.ToString(CultureInfo.InvariantCulture);
        return options.FirstOrDefault(o => o.Value == key)?.Text?.Trim() ?? string.Empty;
    }

    private static string ProjectTypeCode(string projectTypeText)
    {
        var text = (projectTypeText ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sep = text.IndexOf(" - ", StringComparison.Ordinal);
        return (sep > 0 ? text[..sep] : text).Trim();
    }

    private static string FormatCategory(string? category) => (category ?? string.Empty).Trim() switch
    {
        "SourceData" => "Source Data",
        "ExposureData" => "Exposure",
        "LossData" => "Loss",
        "ClaimsData" => "Claims",
        "CorrespondenceData" => "Correspondence",
        "SubmissionData" => "Submission",
        var other when other.Length > 0 => other,
        _ => "Source Data",
    };

    private static string FormatDate(DateTime? value)
        => value is { } d ? d.ToString("M/d/yyyy", CultureInfo.CurrentCulture) : "—";

    private static string FormatMoney(decimal? value)
        => value is { } v
            ? "$" + v.ToString("N2", CultureInfo.CurrentCulture)
            : "—";

    private static string FormatHours(decimal hours)
    {
        if (hours == 0m)
        {
            return "0";
        }

        return (Math.Round(hours, 2)).ToString(CultureInfo.CurrentCulture);
    }

    private static string Blank(string? value, string fallback = "—")
    {
        var t = (value ?? string.Empty).Trim();
        return t.Length > 0 ? t : fallback;
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v.Trim();
            }
        }

        return null;
    }

    private sealed class ReportData
    {
        public string ClientName { get; init; } = string.Empty;
        public string? ClientShortName { get; init; }
        public string? BusinessUnit { get; init; }
        public string? BusinessClass { get; init; }
        public string? ClientType { get; init; }
        public string? ClientStatus { get; init; }
        public int ProjectId { get; init; }
        public string? ProjectNumber { get; init; }
        public string ProjectName { get; init; } = string.Empty;
        public string? ProjectType { get; init; }
        public string? ProjectTypeCode { get; init; }
        public string? UserGroup { get; init; }
        public string? Status { get; init; }
        public bool IsOnline { get; init; }
        public DateTime? RenewalDate { get; init; }
        public DateTime? DueDate { get; init; }
        public decimal? LatestRevenue { get; init; }
        public IReadOnlyList<ReportMember> Members { get; init; } = [];
        public IReadOnlyList<ReportFileRow> DataSources { get; init; } = [];
        public IReadOnlyList<ReportServiceRow> Services { get; init; } = [];
        public IReadOnlyList<ReportCalendarRow> Calendar { get; init; } = [];
        public IReadOnlyList<ReportTimeRow> TimeByAnalyst { get; init; } = [];
        public IReadOnlyList<ReportTimeRow> TimeByTask { get; init; } = [];
        public decimal TotalHours { get; init; }
        public IReadOnlyList<ReportNoteRow> Notes { get; init; } = [];
    }

    private sealed class ReportMember
    {
        public string Name { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
    }

    private sealed class ReportFileRow
    {
        public string FileName { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string DataAsOf { get; init; } = string.Empty;
        public bool Active { get; init; }
        public string UploadedBy { get; init; } = string.Empty;
    }

    private sealed class ReportServiceRow
    {
        public string ServiceName { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
    }

    private sealed class ReportCalendarRow
    {
        public string ItemType { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Owner { get; init; } = string.Empty;
        public string ReceivedDate { get; init; } = string.Empty;
        public string CompletedDate { get; init; } = string.Empty;
        public string Notes { get; init; } = string.Empty;
    }

    private sealed class ReportTimeRow
    {
        public string Label { get; init; } = string.Empty;
        public string? Detail { get; init; }
        public decimal Hours { get; init; }
    }

    private sealed class ReportNoteRow
    {
        public string NoteType { get; init; } = string.Empty;
        public string NoteDate { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public bool Pinned { get; init; }
        public string Text { get; init; } = string.Empty;
    }
}

file static class ProjectReportStringExtensions
{
    public static string OrFallback(this string value, string? fallback)
        => string.IsNullOrWhiteSpace(value)
            ? (fallback ?? string.Empty)
            : value;
}
