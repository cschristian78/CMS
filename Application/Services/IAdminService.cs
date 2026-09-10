using ClientManagementSystem.Models;
using ClientManagementSystem.ViewModels;

namespace ClientManagementSystem.Services;

public interface IAdminService
{
    Task<AdminIndexViewModel> GetIndexAsync(CancellationToken cancellationToken = default);
    Task<ConnectionTestResult> TestServerSettingsAsync(ServerConnectionSettings settings, CancellationToken cancellationToken = default);
    Task SaveServerSettingsAsync(ServerConnectionSettings settings, CancellationToken cancellationToken = default);

    // ---- Users -----------------------------------------------------------
    Task<int> SaveUserAsync(User user, CancellationToken cancellationToken = default);
    Task SaveUserPhotoAsync(int userId, Stream stream, long? declaredLength, CancellationToken cancellationToken = default);
    Task SoftDeleteUserAsync(int userId, CancellationToken cancellationToken = default);
    Task RestoreUserAsync(int userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserAddress>> ListUserAddressesAsync(int userId, CancellationToken cancellationToken = default);
    Task<int> SaveUserAddressAsync(UserAddress address, CancellationToken cancellationToken = default);
    Task DeleteUserAddressAsync(int addressId, CancellationToken cancellationToken = default);

    Task<UserBulkParseResult> ParseUserImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default);
    Task<UserBulkVerifyResult> VerifyUserImportRowsAsync(IReadOnlyList<UserImportRowDto> rows, CancellationToken cancellationToken = default);
    Task<UserBulkImportResult> CommitUserImportRowsAsync(IReadOnlyList<UserImportRowDto> rows, CancellationToken cancellationToken = default);

    // ---- Catalog / Services / Tags --------------------------------------
    Task<int> SaveCatalogServiceAsync(Service service, CancellationToken cancellationToken = default);
    Task SoftDeleteCatalogServiceAsync(int serviceId, CancellationToken cancellationToken = default);
    Task RestoreCatalogServiceAsync(int serviceId, CancellationToken cancellationToken = default);
    Task AddServiceTagAsync(int serviceId, int tagId, CancellationToken cancellationToken = default);
    Task RemoveServiceTagAsync(int serviceId, int tagId, CancellationToken cancellationToken = default);

    Task<ServiceBulkParseResult> ParseServiceImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default);
    Task<ServiceBulkVerifyResult> VerifyServiceImportRowsAsync(IReadOnlyList<ServiceImportRowDto> rows, CancellationToken cancellationToken = default);
    Task<ServiceBulkImportResult> CommitServiceImportRowsAsync(IReadOnlyList<ServiceImportRowDto> rows, CancellationToken cancellationToken = default);

    // ---- Clients --------------------------------------------------------
    Task<IReadOnlyList<ClientAdminRow>> ListClientsAsync(string? searchTerm, bool includeInactive, CancellationToken cancellationToken = default);
    Task<int> SaveClientAsync(ClientAdminSaveRequest request, CancellationToken cancellationToken = default);
    Task SoftDeleteClientAsync(int clientId, CancellationToken cancellationToken = default);
    Task HardDeleteClientAsync(int clientId, CancellationToken cancellationToken = default);

    Task<ClientAdminBulkParseResult> ParseClientImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default);
    Task<ClientAdminBulkVerifyResult> VerifyClientImportRowsAsync(IReadOnlyList<ClientAdminImportRowDto> rows, CancellationToken cancellationToken = default);
    Task<ClientAdminBulkImportResult> CommitClientImportRowsAsync(IReadOnlyList<ClientAdminImportRowDto> rows, CancellationToken cancellationToken = default);

    // ---- Lookups --------------------------------------------------------
    Task SaveUserRoleAsync(UserRole userRole, CancellationToken cancellationToken = default);
    Task SaveProjectTypeAsync(ProjectType projectType, CancellationToken cancellationToken = default);
    Task SaveBusinessUnitAsync(BusinessUnit businessUnit, CancellationToken cancellationToken = default);
    Task SetBusinessUnitActiveAsync(int businessUnitId, bool isActive, CancellationToken cancellationToken = default);
    Task HardDeleteBusinessUnitAsync(int businessUnitId, CancellationToken cancellationToken = default);
    Task SaveBusinessClassAsync(BusinessClass businessClass, CancellationToken cancellationToken = default);
    Task HardDeleteBusinessClassAsync(int businessClassId, CancellationToken cancellationToken = default);
    Task SaveClientCategoryAsync(ClientCategory category, CancellationToken cancellationToken = default);
    Task SaveClientTypeAsync(ClientType clientType, CancellationToken cancellationToken = default);
    Task SetClientTypeActiveAsync(int clientTypeId, bool isActive, CancellationToken cancellationToken = default);
    Task SaveServiceCategoryAsync(ServiceCategory category, CancellationToken cancellationToken = default);
    Task SetServiceCategoryActiveAsync(int serviceCategoryId, bool isActive, CancellationToken cancellationToken = default);
    Task SaveTagAsync(Tag tag, CancellationToken cancellationToken = default);

    // ---- User groups + membership ---------------------------------------
    Task<string> GetNextUserGroupNumberAsync(CancellationToken cancellationToken = default);
    Task<int> SaveUserGroupAsync(UserGroup group, CancellationToken cancellationToken = default);
    Task SoftDeleteUserGroupAsync(int ugid, CancellationToken cancellationToken = default);
    Task HardDeleteUserGroupAsync(int ugid, CancellationToken cancellationToken = default);
    Task RestoreUserGroupAsync(int ugid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserGroup>> ListUserGroupsAsync(string? searchTerm, bool includeDeleted, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserGroupSummary>> SearchUserGroupsAsync(string? term, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserGroupMember>> ListUserGroupMembersAsync(int ugid, CancellationToken cancellationToken = default);
    Task SaveUserGroupMemberAsync(UserGroupMember member, CancellationToken cancellationToken = default);
    Task RemoveUserGroupMemberAsync(int ugid, int userId, CancellationToken cancellationToken = default);
    Task<int> SaveUserGroupBatchAsync(SaveUserGroupBatchRequest batch, CancellationToken cancellationToken = default);

    // ---- Data audit -----------------------------------------------------
    Task<DataAuditReport> GetDataAuditReportAsync(CancellationToken cancellationToken = default);

    // ---- Activity log ---------------------------------------------------
    Task<IReadOnlyList<ActivityLog>> GetActivityForEntityAsync(string entityType, int entityId, int top = 100, CancellationToken cancellationToken = default);
}
