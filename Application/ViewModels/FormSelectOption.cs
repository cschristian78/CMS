namespace ClientManagementSystem.ViewModels;

/// <summary>Select option for SPA JSON: stable <see cref="Value"/> (id), user-facing <see cref="Text"/>, optional <see cref="Code"/> for matching grid/import rows (e.g. BUName, BCName).</summary>
public sealed class FormSelectOption
{
    public string Value { get; set; } = "";

    public string Text { get; set; } = "";

    public string? Code { get; set; }
}
