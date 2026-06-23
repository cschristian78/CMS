namespace ClientManagementSystem.Models;

/// <summary>Client count for one business unit (primary active <c>dbo.tClientRef</c>).</summary>
public sealed class DashboardBusinessUnitCount
{
    public int BusinessUnitID { get; set; }
    public string? BusinessUnitName { get; set; }
    public int ClientCount { get; set; }
}
