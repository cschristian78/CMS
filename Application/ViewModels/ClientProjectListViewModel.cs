namespace ClientManagementSystem.ViewModels;

/// <summary>Tabular result for dbo.tClientProjectList after refresh.</summary>
public sealed class ClientProjectListViewModel
{
    public IReadOnlyList<string> Columns { get; set; } = [];
    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; set; } = [];
}
