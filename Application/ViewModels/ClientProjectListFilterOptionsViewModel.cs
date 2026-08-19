namespace ClientManagementSystem.ViewModels;

/// <summary>Per the CMS_PhaseI_All home-filter contract, every list below is
/// sourced from <c>SELECT DISTINCT</c> against the matching column in
/// <c>dbo.vProject_Current</c>:
/// <list type="bullet">
///   <item><see cref="BusinessUnitNames"/> ← <c>BUName</c></item>
///   <item><see cref="BusinessClassNames"/> ← <c>BCName</c></item>
///   <item><see cref="StatusNames"/> ← <c>StatusDescription</c> (tClientRef.StatusID → tStatus)</item>
///   <item><see cref="ClientNames"/> ← <c>ClientName</c></item>
///   <item><see cref="ProjectTypeNames"/> ← <c>PTDescription</c></item>
///   <item><see cref="AnalyticsLeadNames"/> ← <c>[Analytics Lead]</c></item>
/// </list>
/// </summary>
public sealed class ClientProjectListFilterOptionsViewModel
{
    public IReadOnlyList<string> BusinessUnitNames { get; set; } = [];
    public IReadOnlyList<string> BusinessClassNames { get; set; } = [];
    public IReadOnlyList<string> StatusNames { get; set; } = [];
    public IReadOnlyList<string> ClientNames { get; set; } = [];
    public IReadOnlyList<string> ProjectTypeNames { get; set; } = [];
    public IReadOnlyList<string> AnalyticsLeadNames { get; set; } = [];
}
