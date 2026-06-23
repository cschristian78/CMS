using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

public interface IReferenceDataRepository
{
    Task<IReadOnlyList<Status>> GetStatusesAsync(CancellationToken cancellationToken = default);

    /// <summary>Statuses scoped to a specific entity type via <c>dbo.tStatusAppliesTo</c>
    /// (e.g., <c>'Client'</c>, <c>'Project'</c>, <c>'User'</c>, <c>'UserGroup'</c>,
    /// <c>'Service'</c>). Backed by <c>usp_Status_ListByEntity</c>.</summary>
    Task<IReadOnlyList<Status>> GetStatusesForEntityAsync(string entityType, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessUnit>> GetBusinessUnitsAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessClass>> GetBusinessClassesAsync(CancellationToken cancellationToken = default);

    /// <summary>Business classes in <c>dbo.tBusinessClass</c> where <c>BusinessUnitID</c> matches.</summary>
    Task<IReadOnlyList<BusinessClass>> GetBusinessClassesForBusinessUnitAsync(int businessUnitId, CancellationToken cancellationToken = default);

    /// <summary>Business classes for <paramref name="clientId"/>: <c>tBusinessClass</c> rows whose
    /// <c>BusinessUnitID</c> matches the BU of the client's BC.</summary>
    Task<IReadOnlyList<BusinessClass>> GetBusinessClassesForClientAsync(int clientId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientCategory>> GetClientCategoriesAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClientType>> GetClientTypesAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectType>> GetProjectTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceCategory>> GetServiceCategoriesAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ServiceGroup>> GetServiceGroupsAsync(bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tag>> GetTagsAsync(string? scope = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> GetActiveUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>All non-deleted users from <c>dbo.vUser</c> for pick lists (e.g., UserGroup).</summary>
    Task<IReadOnlyList<User>> GetAllUsersForPicklistAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> GetTopUsersAsync(string? searchTerm = null, bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetBmsUsersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserRole>> GetUserRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserGroupSummary>> GetTopUserGroupSummariesAsync(string? searchTerm = null, CancellationToken cancellationToken = default);

    /// <summary>Distinct user groups that appear on projects for this client (empty if none).</summary>
    Task<IReadOnlyList<UserGroupSummary>> GetUserGroupsUsedOnClientProjectsAsync(int clientId, CancellationToken cancellationToken = default);
}
