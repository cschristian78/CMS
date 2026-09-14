using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

/// <summary>
/// Admin/back-office writes. All Save* methods route to the new CMS_PhaseI_All <c>_Add</c>/
/// <c>_Update</c>/<c>_GetOrCreate</c> stored procedures. Hard deletes are gone — replaced by
/// soft-delete + restore where applicable, and by <c>SetActive</c> for lookups that just
/// toggle <c>IsActive</c>.
/// </summary>
public interface IAdminRepository
{
    Task<bool> UserExistsAsync(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> GetExistingActiveUserEmailsAsync(IEnumerable<string> emails, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserContactInfo>> GetUserContactInfoByIdsAsync(IEnumerable<int> userIds, CancellationToken cancellationToken = default);

    Task<int> SaveUserAsync(User user, CancellationToken cancellationToken = default);
    Task<int> CreateUserWithPrimaryAddressAsync(User user, UserAddress? primaryAddress, CancellationToken cancellationToken = default);
    Task<int> SoftDeleteUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<int> RestoreUserAsync(int userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserAddress>> ListUserAddressesAsync(int userId, CancellationToken cancellationToken = default);
    Task<int> AddUserAddressAsync(UserAddress address, CancellationToken cancellationToken = default);
    Task<int> UpdateUserAddressAsync(UserAddress address, CancellationToken cancellationToken = default);
    Task<int> DeleteUserAddressAsync(int addressId, CancellationToken cancellationToken = default);

    Task<int> SaveUserRoleAsync(UserRole userRole, CancellationToken cancellationToken = default);

    Task<int> SaveProjectTypeAsync(ProjectType projectType, CancellationToken cancellationToken = default);
    Task SoftDeleteProjectTypeAsync(int projectTypeId, CancellationToken cancellationToken = default);

    Task<int> SaveBusinessUnitAsync(BusinessUnit businessUnit, CancellationToken cancellationToken = default);
    Task<int> SetBusinessUnitActiveAsync(int businessUnitId, bool isActive, CancellationToken cancellationToken = default);
    Task HardDeleteBusinessUnitAsync(int businessUnitId, CancellationToken cancellationToken = default);

    Task<int> SaveBusinessClassAsync(BusinessClass businessClass, CancellationToken cancellationToken = default);
    Task HardDeleteBusinessClassAsync(int businessClassId, CancellationToken cancellationToken = default);

    Task<int> SaveClientCategoryAsync(ClientCategory category, CancellationToken cancellationToken = default);
    Task<int> SaveClientTypeAsync(ClientType clientType, CancellationToken cancellationToken = default);
    Task<int> SetClientTypeActiveAsync(int clientTypeId, bool isActive, CancellationToken cancellationToken = default);
    Task<int> SaveServiceCategoryAsync(ServiceCategory category, CancellationToken cancellationToken = default);
    Task<int> SetServiceCategoryActiveAsync(int serviceCategoryId, bool isActive, CancellationToken cancellationToken = default);
    Task<int> SaveServiceGroupAsync(ServiceGroup group, CancellationToken cancellationToken = default);
    Task<int> SaveTagAsync(Tag tag, CancellationToken cancellationToken = default);

    // UserGroups
    Task<IReadOnlyList<UserGroup>> ListUserGroupsAsync(string? searchTerm, bool includeDeleted, CancellationToken cancellationToken = default);
    Task<UserGroup?> GetUserGroupAsync(int ugid, CancellationToken cancellationToken = default);
    Task<string> GetNextUserGroupNumberAsync(CancellationToken cancellationToken = default);
    Task<int> SaveUserGroupAsync(UserGroup group, CancellationToken cancellationToken = default);
    Task<int> SoftDeleteUserGroupAsync(int ugid, CancellationToken cancellationToken = default);
    Task HardDeleteUserGroupAsync(int ugid, CancellationToken cancellationToken = default);
    Task<int> RestoreUserGroupAsync(int ugid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserGroupMember>> ListUserGroupMembersAsync(int ugid, CancellationToken cancellationToken = default);
    Task<int> AddUserGroupMemberAsync(UserGroupMember member, CancellationToken cancellationToken = default);
    Task<int> UpdateUserGroupMemberAsync(UserGroupMember member, CancellationToken cancellationToken = default);
    Task<int> RemoveUserGroupMemberAsync(int ugid, int userId, CancellationToken cancellationToken = default);

    // Clients (tClient + tClientRef)
    Task<IReadOnlyList<ClientAdminRow>> ListClientsAsync(string? searchTerm, bool includeInactive, CancellationToken cancellationToken = default);
    Task<int> SaveClientAsync(ClientAdminSaveRequest request, CancellationToken cancellationToken = default);
    Task<int> SoftDeleteClientAsync(int clientId, CancellationToken cancellationToken = default);
    Task HardDeleteClientAsync(int clientId, CancellationToken cancellationToken = default);

    // Services
    Task<IReadOnlyList<Service>> GetServicesAsync(bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<Service?> GetServiceAsync(int serviceId, CancellationToken cancellationToken = default);
    Task<int> SaveServiceAsync(Service service, CancellationToken cancellationToken = default);
    Task<int> SoftDeleteServiceAsync(int serviceId, CancellationToken cancellationToken = default);
    Task<int> RestoreServiceAsync(int serviceId, CancellationToken cancellationToken = default);
    Task<int> AddServiceTagAsync(int serviceId, int tagId, CancellationToken cancellationToken = default);
    Task<int> RemoveServiceTagAsync(int serviceId, int tagId, CancellationToken cancellationToken = default);

    // Activity log
    Task LogActivityAsync(string entityType, int entityId, string action, string? fieldName = null, string? oldValue = null, string? newValue = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityLog>> GetActivityForEntityAsync(string entityType, int entityId, int top = 100, CancellationToken cancellationToken = default);
}
