using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

public interface ITmsRepository
{
    Task EnsureUserTimeEntrySchemaAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TmsCatUser>> ListCatUsersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TmsUserProject>> ListProjectsForUserAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserTimeEntry>> ListOtherTimeForWeekAsync(
        int userId,
        DateTime weekStartDate,
        CancellationToken cancellationToken = default);

    Task SaveOtherTimeForWeekAsync(
        int userId,
        DateTime weekStartDate,
        IReadOnlyList<UserTimeEntry> entries,
        CancellationToken cancellationToken = default);

    Task<TmsReportResponse> GetReportAsync(
        int year,
        int? userId,
        CancellationToken cancellationToken = default);

    Task<TmsHoursSummary> GetHoursSummaryAsync(
        int userId,
        CancellationToken cancellationToken = default);
}
