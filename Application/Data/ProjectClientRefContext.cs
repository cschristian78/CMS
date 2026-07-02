namespace ClientManagementSystem.Data;

/// <summary>Optional client classification from the project form used to resolve the
/// <c>dbo.tClientRef</c> row linked through <c>dbo.tProjectRef</c>.</summary>
public sealed record ProjectClientRefContext(
    int? BusinessUnitId = null,
    int? BusinessClassId = null,
    int? ClientTypeId = null,
    int? ClientStatusId = null);
