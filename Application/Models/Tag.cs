namespace ClientManagementSystem.Models;

/// <summary>Mirrors <c>dbo.tTag</c>. Global tag library scoped by <see cref="TagScope"/>.</summary>
public sealed class Tag
{
    public int TagID { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string TagScope { get; set; } = string.Empty;
    public DateTime? CreatedDate { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(TagScope) ? TagName : $"{TagName} [{TagScope}]";
}
