namespace ClientManagementSystem.Models;

/// <summary>Label/count pair for dashboard charts; <see cref="ColorKey"/> drives region bar colors.</summary>
public sealed class DashboardNamedCount
{
    public string? Label { get; set; }
    public int Count { get; set; }
    /// <summary>Typically business unit code (US, UK, LA) for bar coloring.</summary>
    public string? ColorKey { get; set; }
}
