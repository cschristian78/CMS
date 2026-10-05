using ClientManagementSystem.Data;
using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public interface ITmsService
{
    Task<IReadOnlyList<TmsCatUser>> ListCatUsersAsync(CancellationToken cancellationToken = default);

    Task<TmsWeekResponse> GetWeekAsync(
        int userId,
        DateTime weekStartDate,
        CancellationToken cancellationToken = default);

    Task<TmsWeekSaveResult> SaveWeekAsync(
        TmsWeekSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<TmsReportResponse> GetReportAsync(
        int year,
        int? userId,
        CancellationToken cancellationToken = default);

    Task<TmsHoursSummary> GetHoursSummaryAsync(
        int userId,
        CancellationToken cancellationToken = default);
}

public sealed class TmsService : ITmsService
{
    private readonly ITmsRepository _tms;
    private readonly IProjectRepository _projects;
    private readonly IProjectService _projectService;
    private readonly ILogger<TmsService>? _logger;

    public TmsService(
        ITmsRepository tms,
        IProjectRepository projects,
        IProjectService projectService,
        ILogger<TmsService>? logger = null)
    {
        _tms = tms;
        _projects = projects;
        _projectService = projectService;
        _logger = logger;
    }

    public Task<IReadOnlyList<TmsCatUser>> ListCatUsersAsync(CancellationToken cancellationToken = default)
        => _tms.ListCatUsersAsync(cancellationToken);

    public Task<TmsReportResponse> GetReportAsync(
        int year,
        int? userId,
        CancellationToken cancellationToken = default)
        => _tms.GetReportAsync(year, userId, cancellationToken);

    public async Task<TmsHoursSummary> GetHoursSummaryAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        await EnsureCatUserAsync(userId, cancellationToken);
        return await _tms.GetHoursSummaryAsync(userId, cancellationToken);
    }

    public async Task<TmsWeekResponse> GetWeekAsync(
        int userId,
        DateTime weekStartDate,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        await EnsureCatUserAsync(userId, cancellationToken);

        var monday = StartOfWeekMonday(weekStartDate);
        var projectHeaders = await _tms.ListProjectsForUserAsync(userId, cancellationToken);
        var projects = new List<TmsUserProject>(projectHeaders.Count);

        foreach (var header in projectHeaders)
        {
            var tasks = await _projects.ListCalendarItemsAsync(header.ProjectID, cancellationToken);
            var entries = await _projects.ListTimeEntriesForWeekAsync(
                header.ProjectID,
                monday,
                userId,
                cancellationToken);

            projects.Add(new TmsUserProject
            {
                ProjectID = header.ProjectID,
                ProjectRefID = header.ProjectRefID,
                ProjectName = header.ProjectName,
                ClientName = header.ClientName,
                IsOnline = header.IsOnline,
                Tasks = tasks,
                Entries = entries,
            });
        }

        var otherEntries = await _tms.ListOtherTimeForWeekAsync(userId, monday, cancellationToken);

        return new TmsWeekResponse
        {
            WeekStartDate = monday,
            WeekEndDate = monday.AddDays(6),
            UserID = userId,
            Projects = projects,
            OtherEntries = otherEntries,
            OtherCategories = TmsOtherTimeCategories.All,
        };
    }

    public async Task<TmsWeekSaveResult> SaveWeekAsync(
        TmsWeekSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.UserID <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request.UserID));
        }

        await EnsureCatUserAsync(request.UserID, cancellationToken);

        var monday = StartOfWeekMonday(request.WeekStartDate);
        var projectSaved = 0;

        foreach (var projectSave in request.Projects ?? [])
        {
            if (projectSave.ProjectID <= 0)
            {
                continue;
            }

            var weekRequest = new ProjectTimeWeekRequest
            {
                WeekStartDate = monday,
                UserID = request.UserID,
                Entries = projectSave.Entries ?? [],
            };
            projectSaved += await _projectService.SaveTimeWeekAsync(
                projectSave.ProjectID,
                weekRequest,
                cancellationToken);
        }

        var otherEntries = request.OtherEntries ?? [];
        await _tms.SaveOtherTimeForWeekAsync(request.UserID, monday, otherEntries, cancellationToken);
        var otherSaved = otherEntries.Count(e =>
            TmsOtherTimeCategories.IsValid(e.Category) && e.Hours > 0m);

        _logger?.LogInformation(
            "TMS week saved for UserID {UserID}: {ProjectCells} project cell(s), {OtherCells} other cell(s).",
            request.UserID,
            projectSaved,
            otherSaved);

        return new TmsWeekSaveResult
        {
            ProjectSavedCount = projectSaved,
            OtherSavedCount = otherSaved,
            Message =
                $"Saved {projectSaved} project time cell(s) and {otherSaved} other time cell(s).",
        };
    }

    private async Task EnsureCatUserAsync(int userId, CancellationToken cancellationToken)
    {
        var users = await _tms.ListCatUsersAsync(cancellationToken);
        if (!users.Any(u => u.UserID == userId))
        {
            throw new InvalidOperationException("The selected user is not an active CAT department member.");
        }
    }

    private static DateTime StartOfWeekMonday(DateTime date)
    {
        var d = date.Date;
        var diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.AddDays(-diff);
    }
}
