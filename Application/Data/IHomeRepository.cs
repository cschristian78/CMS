using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

public interface IHomeRepository
{
    /// <param name="businessUnitName">When any filter is set, metrics are limited to matching <c>dbo.vProject_Current</c> rows.</param>
    /// <param name="projectTypeName">Optional exact match on <c>PTDescription</c> from <c>dbo.vProject_Current</c>.</param>
    Task<HomeSummary> GetHomeSummaryAsync(
        string? businessUnitName = null,
        string? statusName = null,
        string? clientNameSearch = null,
        string? businessClassName = null,
        string? projectTypeName = null,
        CancellationToken cancellationToken = default);
}
