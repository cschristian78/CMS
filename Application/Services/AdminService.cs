using System.Globalization;
using System.Linq;
using System.Text;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;
using ClientManagementSystem.Security;
using ClientManagementSystem.ViewModels;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Services;

/// <summary>Admin tab orchestration against the CMS_PhaseI_All schema. Soft delete replaces
/// hard delete everywhere; lookups go through <c>_GetOrCreate</c>; user addresses are a
/// 1:N child of <c>tUser</c>; user groups split into header + membership join table.</summary>
public sealed class AdminService : IAdminService
{
    private const int UserEmailMaxLength = 150;
    private const int UserNameFieldMaxLength = 50;

    private readonly IAdminRepository _admin;
    private readonly IReferenceDataRepository _referenceData;
    private readonly ISecureConnectionSettingsStore _settingsStore;
    private readonly IUserPhotoStorage _userPhotos;
    private readonly IDashboardRepository _dashboard;

    public AdminService(
        IAdminRepository admin,
        IReferenceDataRepository referenceData,
        ISecureConnectionSettingsStore settingsStore,
        IUserPhotoStorage userPhotos,
        IDashboardRepository dashboard)
    {
        _admin = admin;
        _referenceData = referenceData;
        _settingsStore = settingsStore;
        _userPhotos = userPhotos;
        _dashboard = dashboard;
    }

    /// <summary>Best-effort refresh of <c>dbo.vProject_Current</c> after any admin write
    /// that can change a column the home page filter set surfaces (users, user groups,
    /// roles, business units, business classes, project types). Never let a refresh
    /// failure surface as a save failure.</summary>
    private async Task RefreshHomeListAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dashboard.RefreshClientProjectListAsync(cancellationToken);
        }
        catch
        {
            // Refresh is best-effort; the next read of /api/home/client-project-list will retry it.
        }
    }

    public async Task<AdminIndexViewModel> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        var allUsers = await _referenceData.GetAllUsersForPicklistAsync(cancellationToken);

        return new AdminIndexViewModel
        {
            ServerSettings = await _settingsStore.GetAsync(cancellationToken),
            ServerOptions = _settingsStore.AllowedServers,
            Users = allUsers,
            // AE / Analytics Lead pickers should include all BMS-affiliated staff. Company values
            // include subsidiaries such as "BMS Re Mexico", so match any company that starts with
            // "BMS" rather than requiring an exact match.
            BmsUsers = allUsers
                .Where(u => (u.Company ?? string.Empty).TrimStart().StartsWith("BMS", StringComparison.OrdinalIgnoreCase))
                .ToList(),
            UserRoles = await _referenceData.GetUserRolesAsync(cancellationToken),
            UserGroups = await _admin.ListUserGroupsAsync(null, includeDeleted: false, cancellationToken),
            UserGroupSummaries = await _referenceData.GetTopUserGroupSummariesAsync(cancellationToken: cancellationToken),
            ProjectTypes = await _referenceData.GetProjectTypesAsync(cancellationToken),
            BusinessUnits = await _referenceData.GetBusinessUnitsAsync(cancellationToken: cancellationToken),
            BusinessClasses = await _referenceData.GetBusinessClassesAsync(cancellationToken),
            ClientCategories = await _referenceData.GetClientCategoriesAsync(cancellationToken: cancellationToken),
            ClientTypes = await _referenceData.GetClientTypesAsync(cancellationToken: cancellationToken),
            ServiceCategories = await _referenceData.GetServiceCategoriesAsync(cancellationToken: cancellationToken),
            ServiceGroups = await _referenceData.GetServiceGroupsAsync(activeOnly: true, cancellationToken: cancellationToken),
            Tags = await _referenceData.GetTagsAsync(cancellationToken: cancellationToken),
            Statuses = await _referenceData.GetStatusesAsync(cancellationToken),
            Services = await _admin.GetServicesAsync(includeDeleted: false, cancellationToken),
            Clients = await _admin.ListClientsAsync(null, includeInactive: true, cancellationToken)
        };
    }

    public Task<ConnectionTestResult> TestServerSettingsAsync(ServerConnectionSettings settings, CancellationToken cancellationToken = default)
        => _settingsStore.TestConnectionAsync(settings, cancellationToken);

    public async Task SaveServerSettingsAsync(ServerConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        var testResult = await _settingsStore.TestConnectionAsync(settings, cancellationToken);
        if (!testResult.Succeeded)
        {
            throw new InvalidOperationException(testResult.Message);
        }

        await _settingsStore.SaveAsync(settings, cancellationToken);
    }

    // ---- USERS ----------------------------------------------------------

    public async Task<int> SaveUserAsync(User user, CancellationToken cancellationToken = default)
    {
        TrimUser(user);
        if (!string.IsNullOrEmpty(user.Email) && user.Email.Length > UserEmailMaxLength)
        {
            throw new InvalidOperationException($"Email must be {UserEmailMaxLength} characters or fewer.");
        }

        EnsureFieldLength(user.FirstName, nameof(user.FirstName), UserNameFieldMaxLength);
        EnsureFieldLength(user.LastName, nameof(user.LastName), UserNameFieldMaxLength);
        EnsureFieldLength(user.Title, nameof(user.Title), 100);
        EnsureFieldLength(user.Department, nameof(user.Department), 100);

        if (string.IsNullOrWhiteSpace(user.UserType))
        {
            user.UserType = "Internal";
        }

        var userId = await _admin.SaveUserAsync(user, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
        return userId;
    }

    public async Task SaveUserPhotoAsync(int userId, Stream stream, long? declaredLength, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            throw new InvalidOperationException("Save the user first, then upload a photo.");
        }

        if (!await _admin.UserExistsAsync(userId, cancellationToken))
        {
            throw new InvalidOperationException($"User {userId} was not found.");
        }

        await _userPhotos.SavePhotoAsync(userId, stream, declaredLength, cancellationToken);
    }

    public async Task SoftDeleteUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        await _admin.SoftDeleteUserAsync(userId, cancellationToken);
        try
        {
            await _admin.LogActivityAsync("User", userId, "SoftDelete", null, null, null, cancellationToken);
        }
        catch
        {
            // Activity log is best-effort.
        }

        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task RestoreUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        await _admin.RestoreUserAsync(userId, cancellationToken);
        try
        {
            await _admin.LogActivityAsync("User", userId, "Restore", null, null, null, cancellationToken);
        }
        catch
        {
        }

        await RefreshHomeListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<UserAddress>> ListUserAddressesAsync(int userId, CancellationToken cancellationToken = default)
        => _admin.ListUserAddressesAsync(userId, cancellationToken);

    public async Task<int> SaveUserAddressAsync(UserAddress address, CancellationToken cancellationToken = default)
    {
        if (address.UserID <= 0)
        {
            throw new InvalidOperationException("UserID is required to save an address.");
        }

        address.AddressType = string.IsNullOrWhiteSpace(address.AddressType) ? "Work" : address.AddressType.Trim();
        address.Address1 = address.Address1?.Trim() ?? string.Empty;
        address.City = address.City?.Trim() ?? string.Empty;
        address.State = string.IsNullOrWhiteSpace(address.State) ? null : address.State.Trim();
        address.PostalCode = string.IsNullOrWhiteSpace(address.PostalCode) ? null : address.PostalCode.Trim();
        address.Address2 = string.IsNullOrWhiteSpace(address.Address2) ? null : address.Address2.Trim();

        if (string.IsNullOrWhiteSpace(address.Country))
        {
            address.Country = "US";
        }
        else
        {
            address.Country = address.Country.Trim();
        }

        /* usp_User_UpdateAddress updates every tUserAddress row for @UserID; only call it when at least one row exists. */
        var existing = await _admin.ListUserAddressesAsync(address.UserID, cancellationToken);
        return existing.Count > 0
            ? await _admin.UpdateUserAddressAsync(address, cancellationToken)
            : await _admin.AddUserAddressAsync(address, cancellationToken);
    }

    public async Task DeleteUserAddressAsync(int addressId, CancellationToken cancellationToken = default)
    {
        await _admin.DeleteUserAddressAsync(addressId, cancellationToken);
    }

    // ---- CLIENTS --------------------------------------------------------

    private const int ClientNameMaxLength = 150;
    private const int ClientShortNameMaxLength = 50;
    private const int ClientDescriptionMaxLength = 500;

    public Task<IReadOnlyList<ClientAdminRow>> ListClientsAsync(string? searchTerm, bool includeInactive, CancellationToken cancellationToken = default)
        => _admin.ListClientsAsync(searchTerm, includeInactive, cancellationToken);

    public async Task<int> SaveClientAsync(ClientAdminSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.ClientName = (request.ClientName ?? string.Empty).Trim();
        request.ClientShortName = NullIfBlank(request.ClientShortName);
        request.ClientDescription = NullIfBlank(request.ClientDescription);

        if (string.IsNullOrWhiteSpace(request.ClientName))
        {
            throw new InvalidOperationException("Client Name is required.");
        }

        EnsureFieldLength(request.ClientName, "Client Name", ClientNameMaxLength);
        EnsureFieldLength(request.ClientShortName, "Client Short Name", ClientShortNameMaxLength);
        EnsureFieldLength(request.ClientDescription, "Client Description", ClientDescriptionMaxLength);

        // Treat 0 / negatives as "not selected".
        request.AccountExecutiveUserID = Positive(request.AccountExecutiveUserID);
        request.AnalyticsLeadUserID = Positive(request.AnalyticsLeadUserID);
        request.BusinessUnitID = Positive(request.BusinessUnitID);
        request.BusinessClassID = Positive(request.BusinessClassID);
        request.ClientTypeID = Positive(request.ClientTypeID);

        var classificationCount =
            (request.BusinessUnitID is not null ? 1 : 0) +
            (request.BusinessClassID is not null ? 1 : 0) +
            (request.ClientTypeID is not null ? 1 : 0);
        if (classificationCount is > 0 and < 3)
        {
            throw new InvalidOperationException(
                "Business Unit, Business Class, and Client Type must be selected together (all three) to classify a client.");
        }

        var clientId = await _admin.SaveClientAsync(request, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
        return clientId;
    }

    public async Task SoftDeleteClientAsync(int clientId, CancellationToken cancellationToken = default)
    {
        await _admin.SoftDeleteClientAsync(clientId, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task<ClientAdminBulkParseResult> ParseClientImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvStream);

        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidOperationException("The CSV file is empty or missing a header row.");
        }

        var headerCells = CsvRowParser.SplitLine(headerLine);
        var columnIndex = BuildClientImportHeaderMap(headerCells);
        if (!columnIndex.ContainsKey("ClientName"))
        {
            throw new InvalidOperationException("The CSV must include a ClientName column.");
        }

        var rows = new List<ClientAdminImportRowDto>();
        var skippedEmpty = 0;
        var lineNumber = 1;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = CsvRowParser.SplitLine(line);
            var name = OptionalCell(cells, columnIndex, "ClientName")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                skippedEmpty++;
                continue;
            }

            rows.Add(new ClientAdminImportRowDto
            {
                LineNumber = lineNumber,
                ClientName = name,
                ClientShortName = NullIfBlank(OptionalCell(cells, columnIndex, "ClientShortName")) ?? string.Empty,
                ClientDescription = NullIfBlank(OptionalCell(cells, columnIndex, "ClientDescription")) ?? string.Empty,
                IsActive = ParseOptionalBoolFromCsv(cells, columnIndex, "IsActive", defaultWhenMissingOrEmpty: true),
                AccountExecutiveEmail = NullIfBlank(OptionalCell(cells, columnIndex, "AccountExecutiveEmail") ?? OptionalCell(cells, columnIndex, "AccountExecutive")) ?? string.Empty,
                AnalyticsLeadEmail = NullIfBlank(OptionalCell(cells, columnIndex, "AnalyticsLeadEmail") ?? OptionalCell(cells, columnIndex, "AnalyticsLead")) ?? string.Empty,
                BusinessUnit = NullIfBlank(OptionalCell(cells, columnIndex, "BusinessUnit")) ?? string.Empty,
                BusinessClass = NullIfBlank(OptionalCell(cells, columnIndex, "BusinessClass")) ?? string.Empty,
                ClientType = NullIfBlank(OptionalCell(cells, columnIndex, "ClientType")) ?? string.Empty
            });
        }

        return new ClientAdminBulkParseResult { Rows = rows, SkippedEmptyRows = skippedEmpty };
    }

    public async Task<ClientAdminBulkVerifyResult> VerifyClientImportRowsAsync(
        IReadOnlyList<ClientAdminImportRowDto> rows,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        if (rows is null || rows.Count == 0)
        {
            errors.Add("There are no rows to verify. Choose a CSV file with at least one data row.");
            return new ClientAdminBulkVerifyResult { Valid = false, Errors = errors };
        }

        var lookups = await LoadClientImportLookupsAsync(cancellationToken);

        foreach (var r in rows)
        {
            VerifyClientImportRow(r, lookups, errors, out _);
        }

        return new ClientAdminBulkVerifyResult { Valid = errors.Count == 0, Errors = errors };
    }

    public async Task<ClientAdminBulkImportResult> CommitClientImportRowsAsync(
        IReadOnlyList<ClientAdminImportRowDto> rows,
        CancellationToken cancellationToken = default)
    {
        var verify = await VerifyClientImportRowsAsync(rows, cancellationToken);
        if (!verify.Valid)
        {
            throw new InvalidOperationException(string.Join(' ', verify.Errors));
        }

        var lookups = await LoadClientImportLookupsAsync(cancellationToken);
        var rowErrors = new List<string>();
        var imported = 0;

        foreach (var r in rows)
        {
            var perRow = new List<string>();
            if (!VerifyClientImportRow(r, lookups, perRow, out var resolved))
            {
                rowErrors.AddRange(perRow);
                continue;
            }

            try
            {
                await _admin.SaveClientAsync(resolved, cancellationToken);
                imported++;
            }
            catch (Exception ex)
            {
                rowErrors.Add($"Line {r.LineNumber}: {ex.Message}");
            }
        }

        await RefreshHomeListAsync(cancellationToken);

        return new ClientAdminBulkImportResult
        {
            ImportedCount = imported,
            SkippedEmptyRows = 0,
            RowErrors = rowErrors
        };
    }

    /// <summary>Validates one client import row and (on success) emits a fully resolved save request.
    /// Resolution turns friendly names/emails into the IDs the stored procedures expect.</summary>
    private static bool VerifyClientImportRow(
        ClientAdminImportRowDto r,
        ClientImportLookups lookups,
        List<string> errors,
        out ClientAdminSaveRequest resolved)
    {
        resolved = new ClientAdminSaveRequest
        {
            ClientID = 0,
            IsActive = r.IsActive,
            ClientStatus = r.IsActive ? "Active" : "Inactive"
        };
        var ok = true;

        var name = (r.ClientName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add($"Line {r.LineNumber}: ClientName is required.");
            ok = false;
        }
        else if (name.Length > ClientNameMaxLength)
        {
            errors.Add($"Line {r.LineNumber}: ClientName exceeds {ClientNameMaxLength} characters.");
            ok = false;
        }
        resolved.ClientName = name;

        if (!string.IsNullOrWhiteSpace(r.ClientShortName) && r.ClientShortName.Trim().Length > ClientShortNameMaxLength)
        {
            errors.Add($"Line {r.LineNumber}: ClientShortName exceeds {ClientShortNameMaxLength} characters.");
            ok = false;
        }
        resolved.ClientShortName = NullIfBlank(r.ClientShortName);

        if (!string.IsNullOrWhiteSpace(r.ClientDescription) && r.ClientDescription.Trim().Length > ClientDescriptionMaxLength)
        {
            errors.Add($"Line {r.LineNumber}: ClientDescription exceeds {ClientDescriptionMaxLength} characters.");
            ok = false;
        }
        resolved.ClientDescription = NullIfBlank(r.ClientDescription);

        if (!string.IsNullOrWhiteSpace(r.AccountExecutiveEmail))
        {
            if (lookups.UsersByEmail.TryGetValue(r.AccountExecutiveEmail.Trim(), out var aeId))
            {
                resolved.AccountExecutiveUserID = aeId;
            }
            else
            {
                errors.Add($"Line {r.LineNumber}: Account Executive \"{r.AccountExecutiveEmail.Trim()}\" was not found (expected a user email).");
                ok = false;
            }
        }

        if (!string.IsNullOrWhiteSpace(r.AnalyticsLeadEmail))
        {
            if (lookups.UsersByEmail.TryGetValue(r.AnalyticsLeadEmail.Trim(), out var alId))
            {
                resolved.AnalyticsLeadUserID = alId;
            }
            else
            {
                errors.Add($"Line {r.LineNumber}: Analytics Lead \"{r.AnalyticsLeadEmail.Trim()}\" was not found (expected a user email).");
                ok = false;
            }
        }

        var hasBu = !string.IsNullOrWhiteSpace(r.BusinessUnit);
        var hasBc = !string.IsNullOrWhiteSpace(r.BusinessClass);
        var hasCt = !string.IsNullOrWhiteSpace(r.ClientType);
        if (hasBu || hasBc || hasCt)
        {
            if (!(hasBu && hasBc && hasCt))
            {
                errors.Add($"Line {r.LineNumber}: BusinessUnit, BusinessClass, and ClientType must all be provided together.");
                ok = false;
            }
            else
            {
                if (lookups.BusinessUnitsByName.TryGetValue(r.BusinessUnit.Trim(), out var buId))
                {
                    resolved.BusinessUnitID = buId;
                }
                else
                {
                    errors.Add($"Line {r.LineNumber}: Business Unit \"{r.BusinessUnit.Trim()}\" was not found.");
                    ok = false;
                }

                if (lookups.ClientTypesByName.TryGetValue(r.ClientType.Trim(), out var ctId))
                {
                    resolved.ClientTypeID = ctId;
                }
                else
                {
                    errors.Add($"Line {r.LineNumber}: Client Type \"{r.ClientType.Trim()}\" was not found.");
                    ok = false;
                }

                // dbo.tBusinessClass is global (no BU column), so match purely by name.
                if (lookups.BusinessClassesByName.TryGetValue(r.BusinessClass.Trim(), out var bcId))
                {
                    resolved.BusinessClassID = bcId;
                }
                else
                {
                    errors.Add($"Line {r.LineNumber}: Business Class \"{r.BusinessClass.Trim()}\" was not found.");
                    ok = false;
                }
            }
        }

        return ok;
    }

    private async Task<ClientImportLookups> LoadClientImportLookupsAsync(CancellationToken cancellationToken)
    {
        var businessUnits = await _referenceData.GetBusinessUnitsAsync(cancellationToken: cancellationToken);
        var businessClasses = await _referenceData.GetBusinessClassesAsync(cancellationToken);
        var clientTypes = await _referenceData.GetClientTypesAsync(cancellationToken: cancellationToken);
        var users = await _referenceData.GetAllUsersForPicklistAsync(cancellationToken);

        var buByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var bu in businessUnits)
        {
            var key = bu.BusinessUnitName?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !buByName.ContainsKey(key))
            {
                buByName[key] = bu.BusinessUnitID;
            }
        }

        var ctByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var ct in clientTypes)
        {
            var key = ct.CTName?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !ctByName.ContainsKey(key))
            {
                ctByName[key] = ct.ClientTypeID;
            }
        }

        var usersByEmail = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in users)
        {
            var key = u.Email?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !usersByEmail.ContainsKey(key))
            {
                usersByEmail[key] = u.UserID;
            }
        }

        var bcByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var bc in businessClasses)
        {
            var key = bc.BusinessClassName?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !bcByName.ContainsKey(key))
            {
                bcByName[key] = bc.BusinessClassID;
            }
        }

        return new ClientImportLookups(buByName, bcByName, ctByName, usersByEmail);
    }

    private static int? Positive(int? value) => value is > 0 ? value : null;

    private sealed record ClientImportLookups(
        IReadOnlyDictionary<string, int> BusinessUnitsByName,
        IReadOnlyDictionary<string, int> BusinessClassesByName,
        IReadOnlyDictionary<string, int> ClientTypesByName,
        IReadOnlyDictionary<string, int> UsersByEmail);

    // ---- LOOKUPS --------------------------------------------------------

    public async Task SaveUserRoleAsync(UserRole userRole, CancellationToken cancellationToken = default)
    {
        userRole.UserRoleName = userRole.UserRoleName?.Trim();
        userRole.RoleDescription = string.IsNullOrWhiteSpace(userRole.RoleDescription)
            ? null
            : userRole.RoleDescription.Trim();

        await _admin.SaveUserRoleAsync(userRole, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task SaveProjectTypeAsync(ProjectType projectType, CancellationToken cancellationToken = default)
    {
        projectType.PTCode = projectType.PTCode?.Trim().ToUpperInvariant();
        projectType.PTDescription = projectType.PTDescription?.Trim();
        await _admin.SaveProjectTypeAsync(projectType, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task SaveBusinessUnitAsync(BusinessUnit businessUnit, CancellationToken cancellationToken = default)
    {
        businessUnit.BusinessUnitName = businessUnit.BusinessUnitName?.Trim();
        businessUnit.BusinessUnitDescription = businessUnit.BusinessUnitDescription?.Trim();
        await _admin.SaveBusinessUnitAsync(businessUnit, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task SetBusinessUnitActiveAsync(int businessUnitId, bool isActive, CancellationToken cancellationToken = default)
    {
        await _admin.SetBusinessUnitActiveAsync(businessUnitId, isActive, cancellationToken);
    }

    public async Task SaveBusinessClassAsync(BusinessClass businessClass, CancellationToken cancellationToken = default)
    {
        businessClass.BusinessClassName = businessClass.BusinessClassName?.Trim();
        businessClass.BusinessClassDescription = businessClass.BusinessClassDescription?.Trim();
        await _admin.SaveBusinessClassAsync(businessClass, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
    }

    public Task SaveClientCategoryAsync(ClientCategory category, CancellationToken cancellationToken = default)
    {
        category.CCName = (category.CCName ?? string.Empty).Trim();
        category.CCDescription = category.CCDescription?.Trim();
        return _admin.SaveClientCategoryAsync(category, cancellationToken);
    }

    public async Task SaveClientTypeAsync(ClientType clientType, CancellationToken cancellationToken = default)
    {
        clientType.CTName = (clientType.CTName ?? string.Empty).Trim();
        clientType.CTDescription = clientType.CTDescription?.Trim();
        await _admin.SaveClientTypeAsync(clientType, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task SetClientTypeActiveAsync(int clientTypeId, bool isActive, CancellationToken cancellationToken = default)
    {
        await _admin.SetClientTypeActiveAsync(clientTypeId, isActive, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task SaveServiceCategoryAsync(ServiceCategory category, CancellationToken cancellationToken = default)
    {
        category.CategoryName = (category.CategoryName ?? string.Empty).Trim();
        category.ServiceCategoryDescription = category.ServiceCategoryDescription?.Trim();
        await _admin.SaveServiceCategoryAsync(category, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task SetServiceCategoryActiveAsync(int serviceCategoryId, bool isActive, CancellationToken cancellationToken = default)
    {
        await _admin.SetServiceCategoryActiveAsync(serviceCategoryId, isActive, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
    }

    public Task SaveTagAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        tag.TagName = (tag.TagName ?? string.Empty).Trim();
        tag.TagScope = (tag.TagScope ?? string.Empty).Trim();
        return _admin.SaveTagAsync(tag, cancellationToken);
    }

    // ---- CATALOG SERVICES ----------------------------------------------

    public async Task<int> SaveCatalogServiceAsync(Service service, CancellationToken cancellationToken = default)
    {
        service.ServiceName = (service.ServiceName ?? string.Empty).Trim();
        service.ServiceDescription = service.ServiceDescription?.Trim();
        if (string.IsNullOrWhiteSpace(service.ServiceName))
        {
            throw new InvalidOperationException("ServiceName is required.");
        }

        if (service.ServiceCategoryID <= 0)
        {
            throw new InvalidOperationException("ServiceCategoryID is required to create or update the tServiceRef link.");
        }

        if (service.ServiceGroupID is not > 0)
        {
            throw new InvalidOperationException("ServiceGroupID is required to create or update the tServiceRef link.");
        }

        return await _admin.SaveServiceAsync(service, cancellationToken).ConfigureAwait(false);
    }

    public async Task SoftDeleteCatalogServiceAsync(int serviceId, CancellationToken cancellationToken = default)
    {
        await _admin.SoftDeleteServiceAsync(serviceId, cancellationToken);
    }

    public async Task RestoreCatalogServiceAsync(int serviceId, CancellationToken cancellationToken = default)
    {
        await _admin.RestoreServiceAsync(serviceId, cancellationToken);
    }

    public async Task AddServiceTagAsync(int serviceId, int tagId, CancellationToken cancellationToken = default)
    {
        await _admin.AddServiceTagAsync(serviceId, tagId, cancellationToken);
    }

    public async Task RemoveServiceTagAsync(int serviceId, int tagId, CancellationToken cancellationToken = default)
    {
        await _admin.RemoveServiceTagAsync(serviceId, tagId, cancellationToken);
    }

    // ---- USER GROUPS ---------------------------------------------------

    public Task<string> GetNextUserGroupNumberAsync(CancellationToken cancellationToken = default)
        => _admin.GetNextUserGroupNumberAsync(cancellationToken);

    public async Task<int> SaveUserGroupAsync(UserGroup group, CancellationToken cancellationToken = default)
    {
        group.UGName = (group.UGName ?? string.Empty).Trim();
        if (group.UGID <= 0)
        {
            group.UserGroupNumber = await _admin.GetNextUserGroupNumberAsync(cancellationToken);
        }
        else
        {
            group.UserGroupNumber = (group.UserGroupNumber ?? string.Empty).Trim();
        }

        var ugid = await _admin.SaveUserGroupAsync(group, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
        return ugid;
    }

    public async Task SoftDeleteUserGroupAsync(int ugid, CancellationToken cancellationToken = default)
    {
        await _admin.SoftDeleteUserGroupAsync(ugid, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
    }

    public async Task RestoreUserGroupAsync(int ugid, CancellationToken cancellationToken = default)
    {
        await _admin.RestoreUserGroupAsync(ugid, cancellationToken);
        await RefreshHomeListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<UserGroup>> ListUserGroupsAsync(string? searchTerm, bool includeDeleted, CancellationToken cancellationToken = default)
        => _admin.ListUserGroupsAsync(searchTerm, includeDeleted, cancellationToken);

    public Task<IReadOnlyList<UserGroupSummary>> SearchUserGroupsAsync(string? term, CancellationToken cancellationToken = default)
        => _referenceData.GetTopUserGroupSummariesAsync(string.IsNullOrWhiteSpace(term) ? null : term.Trim(), cancellationToken);

    public Task<IReadOnlyList<UserGroupMember>> ListUserGroupMembersAsync(int ugid, CancellationToken cancellationToken = default)
        => _admin.ListUserGroupMembersAsync(ugid, cancellationToken);

    public Task SaveUserGroupMemberAsync(UserGroupMember member, CancellationToken cancellationToken = default)
        => member.UserID > 0 && member.UGID > 0
            ? _admin.AddUserGroupMemberAsync(member, cancellationToken)
            : throw new InvalidOperationException("UGID and UserID are required to save a group member.");

    public Task RemoveUserGroupMemberAsync(int ugid, int userId, CancellationToken cancellationToken = default)
        => _admin.RemoveUserGroupMemberAsync(ugid, userId, cancellationToken);

    public async Task<int> SaveUserGroupBatchAsync(SaveUserGroupBatchRequest batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var group = new UserGroup
        {
            UGID = batch.UGID,
            UserGroupNumber = batch.UGID > 0 ? (batch.UserGroupNumber ?? string.Empty).Trim() : string.Empty,
            UGName = (batch.UGName ?? string.Empty).Trim(),
            UserRoleID = batch.UserRoleID,
            StatusID = batch.StatusID > 0 ? batch.StatusID : 1
        };

        var ugid = await SaveUserGroupAsync(group, cancellationToken);

        var existing = await _admin.ListUserGroupMembersAsync(ugid, cancellationToken);
        var keepUserIds = batch.Members.Select(m => m.UserID).ToHashSet();
        foreach (var existingMember in existing)
        {
            if (!keepUserIds.Contains(existingMember.UserID))
            {
                await _admin.RemoveUserGroupMemberAsync(ugid, existingMember.UserID, cancellationToken);
            }
        }

        foreach (var line in batch.Members)
        {
            if (line.UserID <= 0)
            {
                continue;
            }

            var existingMember = existing.FirstOrDefault(m => m.UserID == line.UserID);
            if (existingMember is not null)
            {
                if (existingMember.UserRoleID != line.UserRoleID)
                {
                    await _admin.UpdateUserGroupMemberAsync(new UserGroupMember
                    {
                        UGID = ugid,
                        UserID = line.UserID,
                        UserRoleID = line.UserRoleID
                    }, cancellationToken);
                }

                continue;
            }

            await _admin.AddUserGroupMemberAsync(new UserGroupMember
            {
                UGID = ugid,
                UserID = line.UserID,
                UserRoleID = line.UserRoleID,
                StatusID = line.StatusID > 0 ? line.StatusID : 1
            }, cancellationToken);
        }

        await RefreshHomeListAsync(cancellationToken);
        return ugid;
    }

    public Task<IReadOnlyList<ActivityLog>> GetActivityForEntityAsync(string entityType, int entityId, int top = 100, CancellationToken cancellationToken = default)
        => _admin.GetActivityForEntityAsync(entityType, entityId, top, cancellationToken);

    // ---- USER BULK IMPORT ----------------------------------------------

    public async Task<UserBulkParseResult> ParseUserImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvStream);

        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidOperationException("The CSV file is empty or missing a header row.");
        }

        var headerCells = CsvRowParser.SplitLine(headerLine);
        var columnIndex = BuildUserImportHeaderMap(headerCells);
        if (!columnIndex.ContainsKey("Email"))
        {
            throw new InvalidOperationException("The CSV must include an Email column.");
        }

        var rows = new List<UserImportRowDto>();
        var skippedEmpty = 0;
        var lineNumber = 1;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = CsvRowParser.SplitLine(line);
            var email = OptionalCell(cells, columnIndex, "Email")?.Trim();
            if (string.IsNullOrWhiteSpace(email))
            {
                skippedEmpty++;
                continue;
            }

            rows.Add(new UserImportRowDto
            {
                LineNumber = lineNumber,
                Email = email,
                FirstName = NullIfBlank(OptionalCell(cells, columnIndex, "FirstName")) ?? string.Empty,
                LastName = NullIfBlank(OptionalCell(cells, columnIndex, "LastName")) ?? string.Empty,
                Phone = NullIfBlank(OptionalCell(cells, columnIndex, "Phone")) ?? string.Empty,
                Company = NullIfBlank(OptionalCell(cells, columnIndex, "Company")) ?? "BMS",
                Department = NullIfBlank(OptionalCell(cells, columnIndex, "Department")) ?? string.Empty,
                Title = NullIfBlank(OptionalCell(cells, columnIndex, "Title")) ?? string.Empty,
                UserType = NullIfBlank(OptionalCell(cells, columnIndex, "UserType")) ?? "Internal",
                IsActive = ParseOptionalBoolFromCsv(cells, columnIndex, "IsActive", defaultWhenMissingOrEmpty: true),

                AddressType = NullIfBlank(OptionalCell(cells, columnIndex, "AddressType")) ?? "Work",
                Address1 = NullIfBlank(OptionalCell(cells, columnIndex, "Address1") ?? OptionalCell(cells, columnIndex, "Address")) ?? string.Empty,
                Address2 = NullIfBlank(OptionalCell(cells, columnIndex, "Address2")) ?? string.Empty,
                City = NullIfBlank(OptionalCell(cells, columnIndex, "City")) ?? string.Empty,
                State = NullIfBlank(OptionalCell(cells, columnIndex, "State")) ?? string.Empty,
                PostalCode = NullIfBlank(OptionalCell(cells, columnIndex, "PostalCode")) ?? string.Empty,
                Country = NullIfBlank(OptionalCell(cells, columnIndex, "Country")) ?? string.Empty
            });
        }

        return new UserBulkParseResult { Rows = rows, SkippedEmptyRows = skippedEmpty };
    }

    public async Task<UserBulkVerifyResult> VerifyUserImportRowsAsync(
        IReadOnlyList<UserImportRowDto> rows,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (rows is null || rows.Count == 0)
        {
            errors.Add("There are no rows to verify. Choose a CSV file with at least one data row.");
            return new UserBulkVerifyResult { Valid = false, Errors = errors };
        }

        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.Email)) errors.Add($"Line {r.LineNumber}: Email is required.");
            else if (r.Email.Trim().Length > UserEmailMaxLength) errors.Add($"Line {r.LineNumber}: Email exceeds {UserEmailMaxLength} characters.");

            if (r.FirstName.Length > UserNameFieldMaxLength) errors.Add($"Line {r.LineNumber}: FirstName exceeds {UserNameFieldMaxLength} characters.");
            if (r.LastName.Length > UserNameFieldMaxLength) errors.Add($"Line {r.LineNumber}: LastName exceeds {UserNameFieldMaxLength} characters.");

            if (!string.Equals(r.UserType, "Internal", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.UserType, "External", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Line {r.LineNumber}: UserType must be Internal or External.");
            }
        }

        var dupEmails = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .GroupBy(r => r.Email.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"Duplicate Email \"{g.Key}\" on lines: {string.Join(", ", g.Select(x => x.LineNumber).Order())}.")
            .ToList();
        errors.AddRange(dupEmails);

        var existingEmails = await _admin.GetExistingActiveUserEmailsAsync(
            rows.Where(r => !string.IsNullOrWhiteSpace(r.Email)).Select(r => r.Email.Trim()),
            cancellationToken);
        foreach (var r in rows)
        {
            if (!string.IsNullOrWhiteSpace(r.Email) && existingEmails.Contains(r.Email.Trim()))
            {
                errors.Add($"Line {r.LineNumber}: Email \"{r.Email.Trim()}\" already exists in the database.");
            }
        }

        return new UserBulkVerifyResult { Valid = errors.Count == 0, Errors = errors };
    }

    public async Task<UserBulkImportResult> CommitUserImportRowsAsync(
        IReadOnlyList<UserImportRowDto> rows,
        CancellationToken cancellationToken = default)
    {
        var verify = await VerifyUserImportRowsAsync(rows, cancellationToken);
        if (!verify.Valid)
        {
            throw new InvalidOperationException(string.Join(' ', verify.Errors));
        }

        var rowErrors = new List<string>();
        var imported = 0;

        foreach (var dto in rows)
        {
            try
            {
                var user = new User
                {
                    UserID = 0,
                    Email = Truncate(dto.Email.Trim(), UserEmailMaxLength),
                    FirstName = Truncate(NullIfBlank(dto.FirstName), UserNameFieldMaxLength),
                    LastName = Truncate(NullIfBlank(dto.LastName), UserNameFieldMaxLength),
                    Phone = Truncate(NullIfBlank(dto.Phone), 30),
                    Company = Truncate(NullIfBlank(dto.Company) ?? "BMS", 150),
                    Department = Truncate(NullIfBlank(dto.Department), 100),
                    Title = Truncate(NullIfBlank(dto.Title), 100),
                    UserType = string.Equals(dto.UserType, "External", StringComparison.OrdinalIgnoreCase) ? "External" : "Internal"
                };

                UserAddress? primaryAddress = null;
                if (!string.IsNullOrWhiteSpace(dto.Address1))
                {
                    primaryAddress = new UserAddress
                    {
                        AddressType = string.IsNullOrWhiteSpace(dto.AddressType) ? "Work" : dto.AddressType.Trim(),
                        Address1 = dto.Address1.Trim(),
                        Address2 = NullIfBlank(dto.Address2),
                        City = (dto.City ?? string.Empty).Trim(),
                        State = NullIfBlank(dto.State),
                        PostalCode = NullIfBlank(dto.PostalCode),
                        Country = string.IsNullOrWhiteSpace(dto.Country) ? "US" : dto.Country.Trim()
                    };
                }

                await _admin.CreateUserWithPrimaryAddressAsync(user, primaryAddress, cancellationToken);
                imported++;
            }
            catch (Exception ex)
            {
                rowErrors.Add($"Line {dto.LineNumber}: {ex.Message}");
            }
        }

        return new UserBulkImportResult
        {
            ImportedCount = imported,
            SkippedEmptyRows = 0,
            RowErrors = rowErrors
        };
    }

    // ---- SERVICE BULK IMPORT -------------------------------------------

    public async Task<ServiceBulkParseResult> ParseServiceImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvStream);

        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidOperationException("The CSV file is empty or missing a header row.");
        }

        var headerCells = CsvRowParser.SplitLine(headerLine);
        var columnIndex = BuildServiceImportHeaderMap(headerCells);
        if (!columnIndex.ContainsKey("ServiceName"))
        {
            throw new InvalidOperationException("The CSV must include a ServiceName column.");
        }

        var statuses = await _referenceData.GetStatusesForEntityAsync("Service", cancellationToken);
        var defaultStatusId = statuses.FirstOrDefault(s => string.Equals(s.StatusCode, "A", StringComparison.OrdinalIgnoreCase))?.StatusID ?? 1;

        var rows = new List<ServiceImportRowDto>();
        var skippedEmpty = 0;
        var lineNumber = 1;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = CsvRowParser.SplitLine(line);
            var serviceId = ParseOptionalIntFromCsv(cells, columnIndex, "ServiceID", 0);
            var nameRaw = OptionalCell(cells, columnIndex, "ServiceName")?.Trim() ?? string.Empty;
            var descRaw = OptionalCell(cells, columnIndex, "ServiceDescription")?.Trim() ?? string.Empty;
            if (serviceId <= 0 && (string.IsNullOrWhiteSpace(nameRaw) || string.IsNullOrWhiteSpace(descRaw)))
            {
                skippedEmpty++;
                continue;
            }

            rows.Add(new ServiceImportRowDto
            {
                LineNumber = lineNumber,
                ServiceID = serviceId,
                ServiceName = nameRaw,
                ServiceDescription = descRaw,
                CategoryName = NullIfBlank(OptionalCellAny(cells, columnIndex, "CategoryName", "ServiceCategory")) ?? string.Empty,
                ServiceCategoryDescription = NullIfBlank(OptionalCellAny(cells, columnIndex, "ServiceCategoryDescription", "CatFunction")) ?? string.Empty,
                ServiceGroupName = NullIfBlank(OptionalCell(cells, columnIndex, "ServiceGroupName")) ?? string.Empty,
                Tags = NullIfBlank(OptionalCell(cells, columnIndex, "Tags")) ?? string.Empty,
                StatusID = ParseOptionalIntFromCsv(cells, columnIndex, "StatusID", defaultStatusId)
            });
        }

        return new ServiceBulkParseResult { Rows = rows, SkippedEmptyRows = skippedEmpty };
    }

    public async Task<ServiceBulkVerifyResult> VerifyServiceImportRowsAsync(
        IReadOnlyList<ServiceImportRowDto> rows,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (rows is null || rows.Count == 0)
        {
            errors.Add("There are no rows to verify. Choose a CSV file with at least one data row.");
            return new ServiceBulkVerifyResult { Valid = false, Errors = errors };
        }

        var groups = await _referenceData.GetServiceGroupsAsync(activeOnly: true, cancellationToken);

        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.ServiceName))
            {
                errors.Add($"Line {r.LineNumber}: ServiceName is required.");
            }
            else if (r.ServiceName.Length > 150)
            {
                errors.Add($"Line {r.LineNumber}: ServiceName exceeds 150 characters.");
            }

            if (string.IsNullOrWhiteSpace(r.ServiceDescription))
            {
                errors.Add($"Line {r.LineNumber}: ServiceDescription is required.");
            }
            else if (r.ServiceDescription.Length > 255)
            {
                errors.Add($"Line {r.LineNumber}: ServiceDescription exceeds 255 characters.");
            }

            if (!string.IsNullOrWhiteSpace(r.ServiceGroupName) && string.IsNullOrWhiteSpace(r.CategoryName))
            {
                errors.Add($"Line {r.LineNumber}: ServiceCategory is required when ServiceGroupName is provided (tServiceRef links category and group).");
            }

            if (!string.IsNullOrWhiteSpace(r.Tags) && string.IsNullOrWhiteSpace(r.CategoryName))
            {
                errors.Add($"Line {r.LineNumber}: ServiceCategory is required when Tags are provided (tags link through tServiceRef).");
            }

            if (!string.IsNullOrWhiteSpace(r.ServiceGroupName))
            {
                var groupName = r.ServiceGroupName.Trim();
                if (!groups.Any(g => string.Equals(g.ServiceGroupName, groupName, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Line {r.LineNumber}: ServiceGroupName \"{groupName}\" was not found among active service groups.");
                }
            }
        }

        var dupKeys = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.ServiceName))
            .GroupBy(r => r.ServiceName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"Duplicate ServiceName \"{g.Key}\" on lines: {string.Join(", ", g.Select(x => x.LineNumber).Order())}.")
            .ToList();
        errors.AddRange(dupKeys);

        return new ServiceBulkVerifyResult { Valid = errors.Count == 0, Errors = errors };
    }

    public async Task<ServiceBulkImportResult> CommitServiceImportRowsAsync(
        IReadOnlyList<ServiceImportRowDto> rows,
        CancellationToken cancellationToken = default)
    {
        var verify = await VerifyServiceImportRowsAsync(rows, cancellationToken);
        if (!verify.Valid)
        {
            throw new InvalidOperationException(string.Join(' ', verify.Errors));
        }

        var categories = await _referenceData.GetServiceCategoriesAsync(activeOnly: false, cancellationToken);
        var groups = await _referenceData.GetServiceGroupsAsync(activeOnly: true, cancellationToken);
        var rowErrors = new List<string>();
        var imported = 0;

        foreach (var dto in rows)
        {
            try
            {
                var categoryId = 0;
                if (!string.IsNullOrWhiteSpace(dto.CategoryName))
                {
                    var categoryName = dto.CategoryName.Trim();
                    var existing = categories.FirstOrDefault(c =>
                        string.Equals(c.CategoryName, categoryName, StringComparison.OrdinalIgnoreCase));
                    categoryId = existing?.ServiceCategoryID
                        ?? await _admin.SaveServiceCategoryAsync(new ServiceCategory
                        {
                            CategoryName = categoryName,
                            ServiceCategoryDescription = NullIfBlank(dto.ServiceCategoryDescription)
                        }, cancellationToken);
                }

                int? groupId = null;
                if (!string.IsNullOrWhiteSpace(dto.ServiceGroupName))
                {
                    var groupName = dto.ServiceGroupName.Trim();
                    groupId = groups.FirstOrDefault(g =>
                        string.Equals(g.ServiceGroupName, groupName, StringComparison.OrdinalIgnoreCase))?.ServiceGroupID;
                }

                var service = new Service
                {
                    ServiceID = dto.ServiceID,
                    ServiceName = dto.ServiceName.Trim(),
                    ServiceDescription = dto.ServiceDescription.Trim(),
                    ServiceCategoryID = categoryId,
                    ServiceGroupID = groupId,
                    StatusID = dto.StatusID
                };

                var serviceId = await _admin.SaveServiceAsync(service, cancellationToken).ConfigureAwait(false);

                // Attach each tag (TagScope = 'Service'); separator is ';' or ','.
                if (!string.IsNullOrWhiteSpace(dto.Tags))
                {
                    var tagNames = dto.Tags
                        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    foreach (var tagName in tagNames)
                    {
                        var tagId = await _admin.SaveTagAsync(new Tag { TagName = tagName, TagScope = "Service" }, cancellationToken);
                        await _admin.AddServiceTagAsync(serviceId, tagId, cancellationToken);
                    }
                }

                imported++;
            }
            catch (SqlException ex)
            {
                rowErrors.Add($"Line {dto.LineNumber}: {ex.Message}");
            }
            catch (Exception ex)
            {
                rowErrors.Add($"Line {dto.LineNumber}: {ex.Message}");
            }
        }

        return new ServiceBulkImportResult
        {
            ImportedCount = imported,
            SkippedEmptyRows = 0,
            RowErrors = rowErrors
        };
    }

    // ---- HELPERS --------------------------------------------------------

    private static string? GetCell(IReadOnlyList<string> cells, int columnIndex)
        => columnIndex < 0 || columnIndex >= cells.Count ? null : cells[columnIndex];

    private static string? OptionalCell(IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> columnIndex, string columnName)
        => columnIndex.TryGetValue(columnName, out var idx) ? GetCell(cells, idx) : null;

    private static string? OptionalCellAny(IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> columnIndex, params string[] columnNames)
    {
        foreach (var columnName in columnNames)
        {
            var value = OptionalCell(cells, columnIndex, columnName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static int ParseOptionalIntFromCsv(
        IReadOnlyList<string> cells,
        IReadOnlyDictionary<string, int> columnIndex,
        string columnName,
        int defaultWhenMissingOrEmpty)
    {
        if (!columnIndex.TryGetValue(columnName, out var idx))
        {
            return defaultWhenMissingOrEmpty;
        }

        var raw = GetCell(cells, idx);
        if (raw is null || string.IsNullOrWhiteSpace(raw.Trim()))
        {
            return defaultWhenMissingOrEmpty;
        }

        return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>Parses a flexible boolean CSV cell (1/0, true/false, yes/no, y/n). Falls back to
    /// <paramref name="defaultWhenMissingOrEmpty"/> when the column is missing or the cell is blank.</summary>
    private static bool ParseOptionalBoolFromCsv(
        IReadOnlyList<string> cells,
        IReadOnlyDictionary<string, int> columnIndex,
        string columnName,
        bool defaultWhenMissingOrEmpty)
    {
        if (!columnIndex.TryGetValue(columnName, out var idx))
        {
            return defaultWhenMissingOrEmpty;
        }

        var raw = GetCell(cells, idx)?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultWhenMissingOrEmpty;
        }

        return raw.ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" => true,
            "0" or "false" or "no" or "n" => false,
            _ => defaultWhenMissingOrEmpty
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= length ? value : value[..length];
    }

    private static void EnsureFieldLength(string? value, string fieldDisplayName, int maxLength)
    {
        if (!string.IsNullOrEmpty(value) && value.Length > maxLength)
        {
            throw new InvalidOperationException($"{fieldDisplayName} must be {maxLength} characters or fewer.");
        }
    }

    private static void TrimUser(User user)
    {
        user.FirstName = user.FirstName?.Trim();
        user.LastName = user.LastName?.Trim();
        user.Email = user.Email?.Trim();
        user.Phone = user.Phone?.Trim();
        user.Company = user.Company?.Trim();
        user.Department = user.Department?.Trim();
        user.Title = user.Title?.Trim();
        user.UserType = (user.UserType ?? "Internal").Trim();
    }

    private static Dictionary<string, int> BuildUserImportHeaderMap(IReadOnlyList<string> headerCells)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Email", "FirstName", "LastName", "Phone", "Company",
            "Department", "Title", "UserType", "IsActive",
            "AddressType", "Address1", "Address2", "Address",
            "City", "State", "PostalCode", "Country"
        };

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerCells.Count; i++)
        {
            var key = headerCells[i].Trim().TrimStart('\uFEFF');
            if (allowed.Contains(key))
            {
                map[key] = i;
            }
        }

        return map;
    }

    private static Dictionary<string, int> BuildClientImportHeaderMap(IReadOnlyList<string> headerCells)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ClientName", "ClientShortName", "ClientDescription", "IsActive",
            "AccountExecutiveEmail", "AccountExecutive",
            "AnalyticsLeadEmail", "AnalyticsLead",
            "BusinessUnit", "BusinessClass", "ClientType"
        };

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerCells.Count; i++)
        {
            var key = headerCells[i].Trim().TrimStart('\uFEFF');
            if (allowed.Contains(key))
            {
                map[key] = i;
            }
        }

        return map;
    }

    private static Dictionary<string, int> BuildServiceImportHeaderMap(IReadOnlyList<string> headerCells)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ServiceID", "ServiceName", "ServiceDescription",
            "CategoryName", "ServiceCategory", "ServiceCategoryDescription", "CatFunction",
            "ServiceGroupName", "Tags", "StatusID"
        };

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerCells.Count; i++)
        {
            var key = headerCells[i].Trim().TrimStart('\uFEFF');
            if (allowed.Contains(key))
            {
                map[key] = i;
            }
        }

        return map;
    }
}
