using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;
using Microsoft.Extensions.Logging;

namespace ClientManagementSystem.Services;

public sealed class ClientProjectListService : IClientProjectListService
{
    private readonly IDashboardRepository _dashboard;
    private readonly IReferenceDataRepository _referenceData;
    private readonly ILogger<ClientProjectListService>? _logger;

    public ClientProjectListService(
        IDashboardRepository dashboard,
        IReferenceDataRepository referenceData,
        ILogger<ClientProjectListService>? logger = null)
    {
        _dashboard = dashboard;
        _referenceData = referenceData;
        _logger = logger;
    }

    public async Task<ClientProjectListViewModel> GetMaterializedListAsync(
        bool refresh,
        string? businessUnitName,
        string? statusName,
        string? clientNameSearch,
        string? businessClassName,
        string? projectTypeName = null,
        bool? isOnline = null,
        CancellationToken cancellationToken = default)
    {
        if (refresh)
        {
            await _dashboard.RefreshClientProjectListAsync(cancellationToken);
        }

        var raw = await _dashboard.GetClientProjectListTableAsync(
            businessUnitName,
            statusName,
            clientNameSearch,
            businessClassName,
            projectTypeName,
            isOnline,
            cancellationToken);

        var positioned = PlaceTotalHoursAfterProjectName(raw);
        return new ClientProjectListViewModel
        {
            Columns = positioned.Columns,
            Rows = positioned.Rows
        };
    }

    /// <summary>
    /// Ensures <c>TotalHours</c> appears immediately after <c>ProjectName</c> (project description)
    /// on the Main grid, regardless of SELECT column order.
    /// </summary>
    private static RawSqlTableResult PlaceTotalHoursAfterProjectName(RawSqlTableResult raw)
    {
        var columns = raw.Columns.ToList();
        var hoursIdx = columns.FindIndex(c => string.Equals(c, "TotalHours", StringComparison.OrdinalIgnoreCase));
        if (hoursIdx < 0)
        {
            return raw;
        }

        var projectNameIdx = columns.FindIndex(c => string.Equals(c, "ProjectName", StringComparison.OrdinalIgnoreCase));
        // Fallback: treat ProjectNumber as description anchor if ProjectName is missing.
        if (projectNameIdx < 0)
        {
            projectNameIdx = columns.FindIndex(c => string.Equals(c, "ProjectNumber", StringComparison.OrdinalIgnoreCase));
        }

        var targetIdx = projectNameIdx >= 0 ? projectNameIdx + 1 : columns.Count;
        if (hoursIdx == targetIdx || (projectNameIdx >= 0 && hoursIdx == projectNameIdx + 1))
        {
            return raw;
        }

        var hoursName = columns[hoursIdx];
        columns.RemoveAt(hoursIdx);
        if (hoursIdx < targetIdx)
        {
            targetIdx--;
        }

        if (targetIdx < 0)
        {
            targetIdx = 0;
        }

        if (targetIdx > columns.Count)
        {
            targetIdx = columns.Count;
        }

        columns.Insert(targetIdx, hoursName);

        var rows = raw.Rows.Select(row =>
        {
            var cells = row.ToList();
            while (cells.Count < raw.Columns.Count)
            {
                cells.Add(null);
            }

            if (hoursIdx >= cells.Count)
            {
                return (IReadOnlyList<object?>)cells;
            }

            var hoursValue = cells[hoursIdx];
            cells.RemoveAt(hoursIdx);
            var insertAt = targetIdx;
            if (insertAt > cells.Count)
            {
                insertAt = cells.Count;
            }

            cells.Insert(insertAt, hoursValue);
            return (IReadOnlyList<object?>)cells;
        }).ToList();

        return new RawSqlTableResult
        {
            Columns = columns,
            Rows = rows
        };
    }

    public async Task<ClientProjectListFilterOptionsViewModel> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        // Per the CMS_PhaseI_All home-filter contract, every dropdown list is
        // sourced from the unique values of the matching column in
        // dbo.vProject_Current. Reference-data tables are only used as a
        // fallback when the view side returns nothing (e.g. a fresh deployment
        // with no projects yet or the view doesn't have that column) so the
        // dropdowns are never blank.
        //
        // CRITICAL: every individual source MUST be isolated. A single broken
        // reference query (or a column missing from the view) must NOT take
        // down the whole filter-options endpoint and leave the user staring at
        // the generic 500 "Could not load filter options" message.

        var businessUnitsFromView = await SafeDistinctFromViewAsync("BusinessUnitName", cancellationToken);
        var businessClassesFromView = await SafeDistinctFromViewAsync("BusinessClassName", cancellationToken);
        var statusFromView = await SafeDistinctFromViewAsync("StatusDescription", cancellationToken);
        var clientsFromView = await SafeDistinctFromViewAsync("ClientName", cancellationToken);
        var projectTypesFromView = await SafeDistinctFromViewAsync("PTDescription", cancellationToken);

        var businessUnitsFallback = businessUnitsFromView.Count == 0
            ? await SafeAsync("BusinessUnitName/tBusinessUnit", async () =>
                (await _referenceData.GetBusinessUnitsAsync(activeOnly: false, cancellationToken)).Select(s => s.BusinessUnitName).ToList() as IReadOnlyList<string?>)
            : [];
        var businessClassesFallback = businessClassesFromView.Count == 0
            ? await SafeAsync("BusinessClassName/tBusinessClass", async () =>
                (await _referenceData.GetBusinessClassesAsync(cancellationToken))
                    .Select(c => BusinessClass.FormatDisplayDescription(c.BusinessClassName, c.BusinessClassDescription))
                    .ToList() as IReadOnlyList<string?>)
            : [];
        var statusFallback = statusFromView.Count == 0
            ? await SafeAsync("StatusDescription/tStatus", async () =>
                (await _referenceData.GetStatusesAsync(cancellationToken)).Select(StatusLabelForListFilter).ToList() as IReadOnlyList<string?>)
            : [];
        var clientsFallback = clientsFromView.Count == 0
            ? await SafeAsync("ClientName/vClient", async () =>
                (IReadOnlyList<string?>)(await _dashboard.GetDistinctClientNamesForFiltersAsync(cancellationToken)).Cast<string?>().ToList())
            : [];
        var projectTypesFallback = projectTypesFromView.Count == 0
            ? await SafeAsync("PTDescription/tProjectType", async () =>
                (await _referenceData.GetProjectTypesAsync(cancellationToken)).Select(p => p.PTDescription).ToList() as IReadOnlyList<string?>)
            : [];

        return new ClientProjectListFilterOptionsViewModel
        {
            BusinessUnitNames = DistinctSortedNonEmpty(businessUnitsFromView.Concat(businessUnitsFallback)),
            BusinessClassNames = DistinctSortedNonEmpty(businessClassesFromView.Concat(businessClassesFallback)),
            StatusNames = DistinctSortedNonEmpty(statusFromView.Concat(statusFallback)),
            ClientNames = DistinctSortedNonEmpty(clientsFromView.Concat(clientsFallback)),
            ProjectTypeNames = DistinctSortedNonEmpty(projectTypesFromView.Concat(projectTypesFallback)),
        };
    }

    private async Task<IReadOnlyList<string>> SafeDistinctFromViewAsync(string columnName, CancellationToken cancellationToken)
    {
        try
        {
            return await _dashboard.GetClientProjectListDistinctColumnValuesAsync(columnName, cancellationToken);
        }
        catch (Exception ex)
        {
            // dbo.vProject_Current may not yet exist, or may not project this column. The home
            // page must still render — fall back to whatever the reference tables can supply.
            _logger?.LogWarning(
                ex,
                "Home filter-options: SELECT DISTINCT [{Column}] FROM dbo.vProject_Current failed; falling back to reference data.",
                columnName);
            return [];
        }
    }

    private async Task<IReadOnlyList<string?>> SafeAsync(string source, Func<Task<IReadOnlyList<string?>>> producer)
    {
        try
        {
            return await producer();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Home filter-options reference fallback for {Source} failed; the dropdown will be empty.",
                source);
            return [];
        }
    }

    private static string? StatusLabelForListFilter(Status s)
    {
        if (!string.IsNullOrWhiteSpace(s.StatusDescription))
        {
            return s.StatusDescription.Trim();
        }

        return string.IsNullOrWhiteSpace(s.StatusCode) ? null : s.StatusCode.Trim();
    }

    private static IReadOnlyList<string> DistinctSortedNonEmpty(IEnumerable<string?> raw)
    {
        return raw
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
