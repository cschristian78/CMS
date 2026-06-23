using ClientManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

/// <summary>Admin writes against CMS_PhaseI_All_v3: <c>usp_*_Create</c>/<c>_Update</c>/<c>_SoftDelete</c>
/// with <see cref="CmsDefaults.ActingUserId"/>; lifecycle uses <c>IsActive</c> instead of soft-delete restore SPs.</summary>
public sealed class AdminRepository : IAdminRepository
{
    private readonly IDbExecutor _db;
    private readonly ISqlConnectionFactory _connections;

    public AdminRepository(IDbExecutor db, ISqlConnectionFactory connections)
    {
        _db = db;
        _connections = connections;
    }

    public Task<bool> UserExistsAsync(int userId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.tUser WHERE UserID = @UserID AND IsActive = 1) THEN 1 ELSE 0 END AS INT);";
        return ExistsScalarAsync(sql, SqlParameterFactory.Int("@UserID", userId), cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetExistingActiveUserEmailsAsync(IEnumerable<string> emails, CancellationToken cancellationToken = default)
    {
        var normalized = emails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        const string sql = """
            SELECT u.Email
              FROM dbo.tUser AS u
             WHERE u.IsActive = 1
               AND u.Email IN (SELECT LTRIM(RTRIM(value)) FROM STRING_SPLIT(@Emails, CHAR(31)));
            """;

        var payload = string.Join((char)31, normalized);
        var rows = await _db.QueryTextAsync(sql, static r => r.GetString(0),
        [
            SqlParameterFactory.NVarChar("@Emails", -1, payload)
        ], cancellationToken);

        return rows.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> ExistsScalarAsync(string sql, SqlParameter parameter, CancellationToken cancellationToken)
    {
        var rows = await _db.QueryTextAsync(sql, static r => r.GetInt32(0), [parameter], cancellationToken);
        return rows.Count > 0 && rows[0] != 0;
    }

    // -- USERS -----------------------------------------------------------

    public async Task<int> SaveUserAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.UserID > 0)
        {
            await _db.ExecuteAsync("dbo.usp_User_Update", BuildUserUpdateParameters(user), cancellationToken);
            return user.UserID;
        }

        var output = SqlParameterFactory.OutputInt("@NewUserID");
        await _db.ExecuteWithOutputIntAsync("dbo.usp_User_Create",
        [
            SqlParameterFactory.VarChar("@FirstName", 50, user.FirstName),
            SqlParameterFactory.VarChar("@LastName", 50, user.LastName),
            SqlParameterFactory.VarChar("@Title", 100, user.Title),
            SqlParameterFactory.VarChar("@Email", 150, user.Email),
            SqlParameterFactory.VarChar("@Phone", 30, user.Phone),
            SqlParameterFactory.VarChar("@Company", 150, user.Company),
            SqlParameterFactory.VarChar("@Department", 100, user.Department),
            SqlParameterFactory.VarChar("@UserType", 20, user.UserType),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], "@NewUserID", cancellationToken);

        return Convert.ToInt32(output.Value);
    }

    public async Task<int> CreateUserWithPrimaryAddressAsync(User user, UserAddress? primaryAddress, CancellationToken cancellationToken = default)
    {
        var userOutput = SqlParameterFactory.OutputInt("@NewUserID");
        var addressOutput = SqlParameterFactory.OutputInt("@NewAddressID");
        var parameters = new List<SqlParameter>
        {
            SqlParameterFactory.VarChar("@FirstName", 50, user.FirstName),
            SqlParameterFactory.VarChar("@LastName", 50, user.LastName),
            SqlParameterFactory.VarChar("@Title", 100, user.Title),
            SqlParameterFactory.VarChar("@Email", 150, user.Email),
            SqlParameterFactory.VarChar("@Phone", 30, user.Phone),
            SqlParameterFactory.VarChar("@Company", 150, user.Company),
            SqlParameterFactory.VarChar("@Department", 100, user.Department),
            SqlParameterFactory.VarChar("@UserType", 20, user.UserType),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            userOutput,
            addressOutput
        };

        if (primaryAddress is not null && !string.IsNullOrWhiteSpace(primaryAddress.Address1))
        {
            parameters.AddRange(
            [
                SqlParameterFactory.VarCharAsWritten("@AddressType", 20, primaryAddress.AddressType),
                SqlParameterFactory.VarCharAsWritten("@Address1", 200, primaryAddress.Address1),
                SqlParameterFactory.VarChar("@Address2", 200, primaryAddress.Address2),
                SqlParameterFactory.VarCharAsWritten("@City", 100, primaryAddress.City),
                SqlParameterFactory.VarChar("@State", 50, primaryAddress.State),
                SqlParameterFactory.VarChar("@PostalCode", 20, primaryAddress.PostalCode),
                SqlParameterFactory.VarCharAsWritten("@Country", 50, primaryAddress.Country)
            ]);
        }

        var outputs = await _db.ExecuteWithOutputIntsAsync(
            "dbo.usp_User_CreateWithPrimaryAddress",
            parameters,
            cancellationToken);

        return outputs.TryGetValue("@NewUserID", out var userId) && userId.HasValue
            ? userId.Value
            : Convert.ToInt32(userOutput.Value);
    }

    public Task<int> SoftDeleteUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return SoftDeleteUserInternalAsync(userId, cancellationToken);
    }

    private async Task<int> SoftDeleteUserInternalAsync(int userId, CancellationToken cancellationToken)
    {
        await _db.ExecuteAsync("dbo.usp_User_SoftDelete",
        [
            SqlParameterFactory.Int("@UserID", userId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
        return userId;
    }

    public async Task<int> RestoreUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        await ReactivateEntityAsync("User", "tUser", "UserID", userId, cancellationToken);
        return userId;
    }

    public Task<IReadOnlyList<UserAddress>> ListUserAddressesAsync(int userId, CancellationToken cancellationToken = default)
        => _db.QueryAsync("dbo.usp_UserAddressRef_List", MapUserAddress,
        [
            SqlParameterFactory.Int("@UserID", userId)
        ], cancellationToken);

    public async Task<int> AddUserAddressAsync(UserAddress a, CancellationToken cancellationToken = default)
    {
        var output = SqlParameterFactory.OutputInt("@NewAddressID");
        await _db.ExecuteWithOutputIntAsync("dbo.usp_Address_Create",
        [
            SqlParameterFactory.VarCharAsWritten("@Address1", 200, a.Address1),
            SqlParameterFactory.VarChar("@Address2", 200, a.Address2),
            SqlParameterFactory.VarCharAsWritten("@City", 100, a.City),
            SqlParameterFactory.VarChar("@State", 50, a.State),
            SqlParameterFactory.VarChar("@PostalCode", 20, a.PostalCode),
            SqlParameterFactory.VarCharAsWritten("@Country", 50, a.Country),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], "@NewAddressID", cancellationToken);

        var addressId = Convert.ToInt32(output.Value);
        await _db.ExecuteAsync("dbo.usp_UserAddressRef_Add",
        [
            SqlParameterFactory.Int("@UserID", a.UserID),
            SqlParameterFactory.Int("@AddressID", addressId),
            SqlParameterFactory.VarCharAsWritten("@AddressType", 20, a.AddressType),
            SqlParameterFactory.Bit("@IsPrimary", true),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);

        return addressId;
    }

    public async Task<int> UpdateUserAddressAsync(UserAddress a, CancellationToken cancellationToken = default)
    {
        if (a.AddressID <= 0)
        {
            throw new InvalidOperationException("AddressID is required to update an address.");
        }

        await _db.ExecuteAsync("dbo.usp_Address_Update",
        [
            SqlParameterFactory.Int("@AddressID", a.AddressID),
            SqlParameterFactory.VarCharAsWritten("@Address1", 200, a.Address1),
            SqlParameterFactory.VarChar("@Address2", 200, a.Address2),
            SqlParameterFactory.VarCharAsWritten("@City", 100, a.City),
            SqlParameterFactory.VarChar("@State", 50, a.State),
            SqlParameterFactory.VarChar("@PostalCode", 20, a.PostalCode),
            SqlParameterFactory.VarCharAsWritten("@Country", 50, a.Country),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);

        return a.AddressID;
    }

    public async Task<int> DeleteUserAddressAsync(int addressId, CancellationToken cancellationToken = default)
    {
        const string lookupSql = """
            SELECT TOP (1) UserID, AddressType
              FROM dbo.tUserAddressRef
             WHERE AddressID = @AddressID;
            """;

        var refs = await _db.QueryTextAsync(lookupSql, static r => new
        {
            UserID = r.GetInt32(0),
            AddressType = r.GetString(1)
        }, [SqlParameterFactory.Int("@AddressID", addressId)], cancellationToken);

        if (refs.Count == 0)
        {
            return 0;
        }

        var row = refs[0];
        await _db.ExecuteAsync("dbo.usp_UserAddressRef_Remove",
        [
            SqlParameterFactory.Int("@UserID", row.UserID),
            SqlParameterFactory.Int("@AddressID", addressId),
            SqlParameterFactory.VarChar("@AddressType", 20, row.AddressType),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
        return addressId;
    }

    // -- LOOKUPS ---------------------------------------------------------

    public async Task<int> SaveUserRoleAsync(UserRole r, CancellationToken cancellationToken = default)
    {
        if (r.UserRoleID > 0)
        {
            await _db.ExecuteAsync("dbo.usp_UserRole_Update",
            [
                SqlParameterFactory.Int("@UserRoleID", r.UserRoleID),
                SqlParameterFactory.VarChar("@UserRoleName", 100, r.UserRoleName),
                SqlParameterFactory.VarChar("@RoleDescription", 255, r.RoleDescription),
                SqlParameterFactory.Bit("@IsActive", r.IsActive),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return r.UserRoleID;
        }

        var output = SqlParameterFactory.OutputInt("@NewUserRoleID");
        await _db.ExecuteWithOutputIntAsync("dbo.usp_UserRole_Create",
        [
            SqlParameterFactory.VarChar("@UserRoleName", 100, r.UserRoleName),
            SqlParameterFactory.VarChar("@RoleDescription", 255, r.RoleDescription),
            SqlParameterFactory.Bit("@IsActive", r.IsActive),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], "@NewUserRoleID", cancellationToken);

        return Convert.ToInt32(output.Value);
    }

    public async Task<int> SaveProjectTypeAsync(ProjectType pt, CancellationToken cancellationToken = default)
    {
        if (pt.ProjectTypeID > 0)
        {
            await _db.ExecuteAsync("dbo.usp_ProjectType_Update",
            [
                SqlParameterFactory.Int("@ProjectTypeID", pt.ProjectTypeID),
                SqlParameterFactory.VarChar("@ProjectTypeCode", 20, pt.PTCode),
                SqlParameterFactory.VarChar("@ProjectTypeDescription", 255, pt.PTDescription),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return pt.ProjectTypeID;
        }

        var output = SqlParameterFactory.OutputInt("@NewProjectTypeID");
        await _db.ExecuteWithOutputIntAsync("dbo.usp_ProjectType_Create",
        [
            SqlParameterFactory.VarChar("@ProjectTypeCode", 20, pt.PTCode),
            SqlParameterFactory.VarChar("@ProjectTypeDescription", 255, pt.PTDescription),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], "@NewProjectTypeID", cancellationToken);

        return Convert.ToInt32(output.Value);
    }

    public async Task<int> SaveBusinessUnitAsync(BusinessUnit bu, CancellationToken cancellationToken = default)
    {
        if (bu.BusinessUnitID > 0)
        {
            await _db.ExecuteAsync("dbo.usp_BusinessUnit_Update",
            [
                SqlParameterFactory.Int("@BusinessUnitID", bu.BusinessUnitID),
                SqlParameterFactory.VarChar("@BusinessUnitName", 100, bu.BusinessUnitName),
                SqlParameterFactory.VarChar("@BusinessUnitDescription", 255, bu.BusinessUnitDescription),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return bu.BusinessUnitID;
        }

        var output = SqlParameterFactory.OutputInt("@NewBusinessUnitID");
        await _db.ExecuteWithOutputIntAsync("dbo.usp_BusinessUnit_Create",
        [
            SqlParameterFactory.VarChar("@BusinessUnitName", 100, bu.BusinessUnitName),
            SqlParameterFactory.VarChar("@BusinessUnitDescription", 255, bu.BusinessUnitDescription),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], "@NewBusinessUnitID", cancellationToken);

        return Convert.ToInt32(output.Value);
    }

    public async Task<int> SetBusinessUnitActiveAsync(int businessUnitId, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!isActive)
        {
            await _db.ExecuteAsync("dbo.usp_BusinessUnit_SoftDelete",
            [
                SqlParameterFactory.Int("@BusinessUnitID", businessUnitId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return businessUnitId;
        }

        await ReactivateEntityAsync("BusinessUnit", "tBusinessUnit", "BusinessUnitID", businessUnitId, cancellationToken);
        return businessUnitId;
    }

    public async Task<int> SaveBusinessClassAsync(BusinessClass bc, CancellationToken cancellationToken = default)
    {
        if (bc.BusinessClassID > 0)
        {
            await _db.ExecuteAsync("dbo.usp_BusinessClass_Update",
            [
                SqlParameterFactory.Int("@BusinessClassID", bc.BusinessClassID),
                SqlParameterFactory.VarChar("@BusinessClassName", 100, bc.BusinessClassName),
                SqlParameterFactory.VarChar("@BusinessClassDescription", 255, bc.BusinessClassDescription),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return bc.BusinessClassID;
        }

        var output = SqlParameterFactory.OutputInt("@NewBusinessClassID");
        await _db.ExecuteWithOutputIntAsync("dbo.usp_BusinessClass_Create",
        [
            SqlParameterFactory.VarChar("@BusinessClassName", 100, bc.BusinessClassName),
            SqlParameterFactory.VarChar("@BusinessClassDescription", 255, bc.BusinessClassDescription),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], "@NewBusinessClassID", cancellationToken);

        return Convert.ToInt32(output.Value);
    }

    public async Task<int> SetClientTypeActiveAsync(int clientTypeId, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!isActive)
        {
            await _db.ExecuteAsync("dbo.usp_ClientType_SoftDelete",
            [
                SqlParameterFactory.Int("@ClientTypeID", clientTypeId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return clientTypeId;
        }

        await ReactivateEntityAsync("ClientType", "tClientType", "ClientTypeID", clientTypeId, cancellationToken);
        return clientTypeId;
    }

    public Task<int> SaveClientCategoryAsync(ClientCategory c, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Client categories were removed in CMS Phase I v3.");

    public async Task<int> SaveClientTypeAsync(ClientType c, CancellationToken cancellationToken = default)
    {
        if (c.ClientTypeID > 0)
        {
            await _db.ExecuteAsync("dbo.usp_ClientType_Update",
            [
                SqlParameterFactory.Int("@ClientTypeID", c.ClientTypeID),
                SqlParameterFactory.VarChar("@ClientTypeName", 100, c.CTName),
                SqlParameterFactory.VarChar("@ClientTypeDescription", 255, c.CTDescription),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return c.ClientTypeID;
        }

        var output = SqlParameterFactory.OutputInt("@NewClientTypeID");
        await _db.ExecuteWithOutputIntAsync("dbo.usp_ClientType_Create",
        [
            SqlParameterFactory.VarChar("@ClientTypeName", 100, c.CTName),
            SqlParameterFactory.VarChar("@ClientTypeDescription", 255, c.CTDescription),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], "@NewClientTypeID", cancellationToken);

        return Convert.ToInt32(output.Value);
    }

    public async Task<int> SaveServiceCategoryAsync(ServiceCategory c, CancellationToken cancellationToken = default)
    {
        if (c.ServiceCategoryID > 0)
        {
            await _db.ExecuteAsync("dbo.usp_ServiceCategory_Update",
            [
                SqlParameterFactory.Int("@ServiceCategoryID", c.ServiceCategoryID),
                SqlParameterFactory.VarChar("@CategoryName", 100, c.CategoryName),
                SqlParameterFactory.VarChar("@ServiceCategoryDescription", 100, c.ServiceCategoryDescription),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return c.ServiceCategoryID;
        }

        var output = SqlParameterFactory.OutputInt("@NewServiceCategoryID");
        await _db.ExecuteWithOutputIntAsync("dbo.usp_ServiceCategory_Create",
        [
            SqlParameterFactory.VarChar("@CategoryName", 100, c.CategoryName),
            SqlParameterFactory.VarChar("@ServiceCategoryDescription", 100, c.ServiceCategoryDescription),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], "@NewServiceCategoryID", cancellationToken);

        return Convert.ToInt32(output.Value);
    }

    public async Task<int> SetServiceCategoryActiveAsync(int serviceCategoryId, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!isActive)
        {
            await _db.ExecuteAsync("dbo.usp_ServiceCategory_SoftDelete",
            [
                SqlParameterFactory.Int("@ServiceCategoryID", serviceCategoryId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return serviceCategoryId;
        }

        await ReactivateEntityAsync("ServiceCategory", "tServiceCategory", "ServiceCategoryID", serviceCategoryId, cancellationToken);
        return serviceCategoryId;
    }

    public async Task<int> SaveTagAsync(Tag t, CancellationToken cancellationToken = default)
    {
        if (t.TagID > 0)
        {
            await _db.ExecuteAsync("dbo.usp_Tag_Update",
            [
                SqlParameterFactory.Int("@TagID", t.TagID),
                SqlParameterFactory.VarChar("@TagName", 50, t.TagName),
                SqlParameterFactory.VarChar("@TagScope", 30, t.TagScope),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return t.TagID;
        }

        var output = SqlParameterFactory.OutputInt("@NewTagID");
        await _db.ExecuteWithOutputIntAsync("dbo.usp_Tag_Create",
        [
            SqlParameterFactory.VarChar("@TagName", 50, t.TagName),
            SqlParameterFactory.VarChar("@TagScope", 30, t.TagScope),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], "@NewTagID", cancellationToken);

        return Convert.ToInt32(output.Value);
    }

    // -- CLIENTS ---------------------------------------------------------

    public Task<IReadOnlyList<ClientAdminRow>> ListClientsAsync(string? searchTerm, bool includeInactive, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                c.ClientID,
                c.ClientName,
                c.ClientShortName,
                c.ClientDescription,
                c.IsActive,
                c.ClientStatus,
                c.AccountExecutiveUserID,
                AccountExecutiveName = LTRIM(RTRIM(CONCAT(ae.FirstName, N' ', ae.LastName))),
                c.AnalyticsLeadUserID,
                AnalyticsLeadName    = LTRIM(RTRIM(CONCAT(al.FirstName, N' ', al.LastName))),
                cr.ClientRefID,
                cr.BusinessUnitID,
                BusinessUnitName  = bu.BusinessUnitName,
                cr.BusinessClassID,
                BusinessClassName = COALESCE(NULLIF(LTRIM(RTRIM(bc.BusinessClassDescription)), N''), bc.BusinessClassName),
                bc.BusinessClassDescription,
                cr.ClientTypeID,
                ClientTypeName    = ct.ClientTypeName,
                cr.StatusID,
                RefStatusCode        = st.StatusCode,
                RefStatusDescription = st.StatusDescription
              FROM dbo.tClient AS c
              OUTER APPLY (
                  SELECT TOP (1) r.ClientRefID, r.BusinessUnitID, r.BusinessClassID, r.ClientTypeID, r.StatusID
                    FROM dbo.tClientRef AS r
                   WHERE r.ClientID = c.ClientID AND r.IsActive = 1
                   ORDER BY r.ClientRefID
              ) AS cr
              LEFT JOIN dbo.tBusinessUnit  AS bu ON bu.BusinessUnitID  = cr.BusinessUnitID
              LEFT JOIN dbo.tBusinessClass AS bc ON bc.BusinessClassID = cr.BusinessClassID
              LEFT JOIN dbo.tClientType    AS ct ON ct.ClientTypeID    = cr.ClientTypeID
              LEFT JOIN dbo.tStatus        AS st ON st.StatusID        = cr.StatusID
              LEFT JOIN dbo.tUser AS ae ON ae.UserID = c.AccountExecutiveUserID
              LEFT JOIN dbo.tUser AS al ON al.UserID = c.AnalyticsLeadUserID
             WHERE (@IncludeInactive = 1 OR c.IsActive = 1)
               AND (@SearchTerm IS NULL
                    OR c.ClientName LIKE @SearchPattern
                    OR c.ClientShortName LIKE @SearchPattern)
             ORDER BY c.ClientName, c.ClientID;
            """;

        var term = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm.Trim();
        return _db.QueryTextAsync(sql, MapClientAdminRow,
        [
            SqlParameterFactory.Bit("@IncludeInactive", includeInactive),
            SqlParameterFactory.NVarChar("@SearchTerm", 150, term),
            SqlParameterFactory.NVarChar("@SearchPattern", 152, term is null ? null : $"%{term}%")
        ], cancellationToken);
    }

    public async Task<int> SaveClientAsync(ClientAdminSaveRequest r, CancellationToken cancellationToken = default)
    {
        // ClientStatus is the source of truth for the client's lifecycle; the stored procs
        // derive IsActive from it (Inactive => 0, Active/Prospect => 1).
        var status = NormalizeClientStatus(r.ClientStatus);

        int clientId;
        if (r.ClientID > 0)
        {
            await _db.ExecuteAsync("dbo.usp_Client_Update",
            [
                SqlParameterFactory.Int("@ClientID", r.ClientID),
                SqlParameterFactory.Int("@AccountExecutiveUserID", r.AccountExecutiveUserID),
                SqlParameterFactory.Int("@AnalyticsLeadUserID", r.AnalyticsLeadUserID),
                SqlParameterFactory.VarChar("@ClientName", 150, r.ClientName),
                SqlParameterFactory.VarChar("@ClientDescription", 500, r.ClientDescription),
                SqlParameterFactory.VarChar("@ClientShortName", 50, r.ClientShortName),
                SqlParameterFactory.VarChar("@ClientStatus", 10, status),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            clientId = r.ClientID;
        }
        else
        {
            var output = SqlParameterFactory.OutputInt("@NewClientID");
            await _db.ExecuteWithOutputIntAsync("dbo.usp_Client_Create",
            [
                SqlParameterFactory.Int("@AccountExecutiveUserID", r.AccountExecutiveUserID),
                SqlParameterFactory.Int("@AnalyticsLeadUserID", r.AnalyticsLeadUserID),
                SqlParameterFactory.VarChar("@ClientName", 150, r.ClientName),
                SqlParameterFactory.VarChar("@ClientDescription", 500, r.ClientDescription),
                SqlParameterFactory.VarChar("@ClientShortName", 50, r.ClientShortName),
                SqlParameterFactory.VarChar("@ClientStatus", 10, status),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                output
            ], "@NewClientID", cancellationToken);
            clientId = Convert.ToInt32(output.Value);
        }

        await UpsertClientRefAsync(clientId, r.BusinessUnitID, r.BusinessClassID, r.ClientTypeID, StatusIdFromStatus(status), cancellationToken);
        return clientId;
    }

    public async Task<int> SoftDeleteClientAsync(int clientId, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteAsync("dbo.usp_Client_SoftDelete",
        [
            SqlParameterFactory.Int("@ClientID", clientId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
        return clientId;
    }

    /// <summary>Coerce an arbitrary status string to one of the allowed values, defaulting to Active.</summary>
    private static string NormalizeClientStatus(string? status)
    {
        var s = (status ?? string.Empty).Trim();
        if (string.Equals(s, "Inactive", StringComparison.OrdinalIgnoreCase)) return "Inactive";
        if (string.Equals(s, "Prospect", StringComparison.OrdinalIgnoreCase)) return "Prospect";
        return "Active";
    }

    /// <summary>Map a normalized status string to its dbo.tStatus seed id (A=1, I=2, P=3).</summary>
    private static int StatusIdFromStatus(string status) => status switch
    {
        "Inactive" => 2,
        "Prospect" => 3,
        _ => 1
    };

    /// <summary>Business Unit / Business Class / Client Type live on a single tClientRef row. Each is nullable, so
    /// the ref is written as soon as <em>any</em> of the three is supplied; the others are stored as NULL until the
    /// user fills them in later. The ref carries its own StatusID so a client can hold a different status per
    /// business unit; it defaults to the client's overall status.</summary>
    private async Task UpsertClientRefAsync(int clientId, int? businessUnitId, int? businessClassId, int? clientTypeId, int statusId, CancellationToken cancellationToken)
    {
        // Normalize "not selected" (0) to NULL so partial classifications persist cleanly.
        businessUnitId = businessUnitId > 0 ? businessUnitId : null;
        businessClassId = businessClassId > 0 ? businessClassId : null;
        clientTypeId = clientTypeId > 0 ? clientTypeId : null;

        var existingRefId = await GetPrimaryClientRefIdAsync(clientId, cancellationToken);

        // Nothing to classify yet and no existing row to update — leave the client unclassified.
        if (businessUnitId is null && businessClassId is null && clientTypeId is null && existingRefId is null)
        {
            return;
        }

        if (existingRefId is null)
        {
            var output = SqlParameterFactory.OutputInt("@NewClientRefID");
            await _db.ExecuteWithOutputIntAsync("dbo.usp_ClientRef_Create",
            [
                SqlParameterFactory.Int("@ClientID", clientId),
                SqlParameterFactory.Int("@BusinessUnitID", businessUnitId),
                SqlParameterFactory.Int("@BusinessClassID", businessClassId),
                SqlParameterFactory.Int("@ClientTypeID", clientTypeId),
                SqlParameterFactory.Int("@ClientTypeRefID", null),
                SqlParameterFactory.Int("@StatusID", statusId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                output
            ], "@NewClientRefID", cancellationToken);
            return;
        }

        await _db.ExecuteAsync("dbo.usp_ClientRef_Update",
        [
            SqlParameterFactory.Int("@ClientRefID", existingRefId.Value),
            SqlParameterFactory.Int("@ClientID", clientId),
            SqlParameterFactory.Int("@BusinessUnitID", businessUnitId),
            SqlParameterFactory.Int("@BusinessClassID", businessClassId),
            SqlParameterFactory.Int("@ClientTypeID", clientTypeId),
            SqlParameterFactory.Int("@ClientTypeRefID", null),
            SqlParameterFactory.Int("@StatusID", statusId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
    }

    private async Task<int?> GetPrimaryClientRefIdAsync(int clientId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) ClientRefID
              FROM dbo.tClientRef
             WHERE ClientID = @ClientID
               AND IsActive = 1
             ORDER BY ClientRefID;
            """;

        var rows = await _db.QueryTextAsync(sql, static r => r.GetInt32(0),
        [
            SqlParameterFactory.Int("@ClientID", clientId)
        ], cancellationToken);

        return rows.Count == 0 ? null : rows[0];
    }

    // -- USER GROUPS -----------------------------------------------------

    public Task<IReadOnlyList<UserGroup>> ListUserGroupsAsync(string? searchTerm, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                ug.UserGroupID AS UGID,
                ug.UserGroupNumber,
                ug.UserGroupName AS UGName,
                ug.IsActive
              FROM dbo.tUserGroup AS ug
             WHERE (@IncludeInactive = 1 OR ug.IsActive = 1)
               AND (@SearchTerm IS NULL OR ug.UserGroupNumber LIKE @SearchPattern OR ug.UserGroupName LIKE @SearchPattern)
             ORDER BY ug.UserGroupName, ug.UserGroupID;
            """;

        return _db.QueryTextAsync(sql, MapUserGroup,
        [
            SqlParameterFactory.Bit("@IncludeInactive", includeDeleted),
            SqlParameterFactory.NVarChar("@SearchTerm", 100, string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm.Trim()),
            SqlParameterFactory.NVarChar("@SearchPattern", 102, string.IsNullOrWhiteSpace(searchTerm) ? null : $"%{searchTerm.Trim()}%")
        ], cancellationToken);
    }

    public async Task<UserGroup?> GetUserGroupAsync(int ugid, CancellationToken cancellationToken = default)
    {
        var rows = await _db.QueryAsync("dbo.usp_UserGroup_Get", MapUserGroupFromTable,
        [
            SqlParameterFactory.Int("@UserGroupID", ugid)
        ], cancellationToken);

        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<string> GetNextUserGroupNumberAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT CAST(ISNULL(MAX(TRY_CAST(UserGroupNumber AS INT)), 0) + 1 AS VARCHAR(50)) AS NextUserGroupNumber
              FROM dbo.tUserGroup;
            """;

        var rows = await _db.QueryTextAsync(sql, static r => r.GetString(0) ?? "1", null, cancellationToken);
        return rows.Count == 0 ? "1" : rows[0];
    }

    public async Task<int> SaveUserGroupAsync(UserGroup g, CancellationToken cancellationToken = default)
    {
        if (g.UGID > 0)
        {
            await _db.ExecuteAsync("dbo.usp_UserGroup_Update",
            [
                SqlParameterFactory.Int("@UserGroupID", g.UGID),
                SqlParameterFactory.VarChar("@UserGroupNumber", 50, g.UserGroupNumber),
                SqlParameterFactory.VarChar("@UserGroupName", 100, g.UGName),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
            return g.UGID;
        }

        var output = SqlParameterFactory.OutputInt("@NewUserGroupID");
        await _db.ExecuteWithOutputIntAsync("dbo.usp_UserGroup_Create",
        [
            SqlParameterFactory.VarChar("@UserGroupNumber", 50, g.UserGroupNumber),
            SqlParameterFactory.VarChar("@UserGroupName", 100, g.UGName),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], "@NewUserGroupID", cancellationToken);

        return Convert.ToInt32(output.Value);
    }

    public async Task<int> SoftDeleteUserGroupAsync(int ugid, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteAsync("dbo.usp_UserGroup_SoftDelete",
        [
            SqlParameterFactory.Int("@UserGroupID", ugid),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
        return ugid;
    }

    public async Task<int> RestoreUserGroupAsync(int ugid, CancellationToken cancellationToken = default)
    {
        await ReactivateEntityAsync("UserGroup", "tUserGroup", "UserGroupID", ugid, cancellationToken);
        return ugid;
    }

    public Task<IReadOnlyList<UserGroupMember>> ListUserGroupMembersAsync(int ugid, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                ugr.UserGroupID AS UGID,
                ug.UserGroupNumber,
                ug.UserGroupName AS UGName,
                ugr.UserID,
                u.FirstName,
                u.LastName,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS UserFullName,
                u.Email,
                ugr.UserRoleID,
                ur.UserRoleName,
                ugr.JoinedDate
              FROM dbo.tUserGroupRef AS ugr
              INNER JOIN dbo.tUserGroup AS ug ON ug.UserGroupID = ugr.UserGroupID
              INNER JOIN dbo.tUser AS u ON u.UserID = ugr.UserID
              LEFT JOIN dbo.tUserRole AS ur ON ur.UserRoleID = ugr.UserRoleID
             WHERE ugr.UserGroupID = @UserGroupID
             ORDER BY u.LastName, u.FirstName, u.UserID;
            """;

        return _db.QueryTextAsync(sql, MapUserGroupMember,
        [
            SqlParameterFactory.Int("@UserGroupID", ugid)
        ], cancellationToken);
    }

    public async Task<int> AddUserGroupMemberAsync(UserGroupMember m, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteAsync("dbo.usp_UserGroupRef_Add",
        [
            SqlParameterFactory.Int("@UserGroupID", m.UGID),
            SqlParameterFactory.Int("@UserID", m.UserID),
            SqlParameterFactory.Int("@UserRoleID", m.UserRoleID),
            SqlParameterFactory.DateTime2("@JoinedDate", m.JoinedDate == default ? DateTime.UtcNow : m.JoinedDate),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
        return m.UserID;
    }

    public async Task<int> UpdateUserGroupMemberAsync(UserGroupMember m, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.tUserGroupRef
               SET UserRoleID = @UserRoleID
             WHERE UserGroupID = @UserGroupID
               AND UserID = @UserID;
            """;

        await _db.ExecuteTextAsync(sql,
        [
            SqlParameterFactory.Int("@UserGroupID", m.UGID),
            SqlParameterFactory.Int("@UserID", m.UserID),
            SqlParameterFactory.Int("@UserRoleID", m.UserRoleID)
        ], cancellationToken);
        return m.UserID;
    }

    public async Task<int> RemoveUserGroupMemberAsync(int ugid, int userId, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteAsync("dbo.usp_UserGroupRef_Remove",
        [
            SqlParameterFactory.Int("@UserGroupID", ugid),
            SqlParameterFactory.Int("@UserID", userId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
        return userId;
    }

    // -- SERVICES --------------------------------------------------------

    public Task<IReadOnlyList<Service>> GetServicesAsync(bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                s.ServiceID,
                s.ServiceName,
                s.ServiceDescription,
                s.IsActive,
                sr.ServiceRefID,
                sr.ServiceCategoryID,
                sr.ServiceGroupID,
                sc.CategoryName,
                sc.ServiceCategoryDescription,
                sg.ServiceGroupName
              FROM dbo.tService AS s
              OUTER APPLY (
                  SELECT TOP (1) r.ServiceRefID, r.ServiceCategoryID, r.ServiceGroupID
                    FROM dbo.tServiceRef AS r
                   WHERE r.ServiceID = s.ServiceID
                   ORDER BY r.ServiceRefID DESC
              ) AS sr
              LEFT JOIN dbo.tServiceCategory AS sc ON sc.ServiceCategoryID = sr.ServiceCategoryID
              LEFT JOIN dbo.tServiceGroup AS sg ON sg.ServiceGroupID = sr.ServiceGroupID
             WHERE (@IncludeInactive = 1 OR s.IsActive = 1)
             ORDER BY sg.ServiceGroupName, sc.CategoryName, s.ServiceName, s.ServiceID;
            """;

        return _db.QueryTextAsync(sql, MapService,
        [
            SqlParameterFactory.Bit("@IncludeInactive", includeDeleted)
        ], cancellationToken);
    }

    public async Task<Service?> GetServiceAsync(int serviceId, CancellationToken cancellationToken = default)
    {
        var rows = await GetServicesAsync(includeDeleted: true, cancellationToken);
        return rows.FirstOrDefault(s => s.ServiceID == serviceId);
    }

    public async Task<int> SaveServiceAsync(Service service, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.CreateOpenConnectionAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            int serviceId;
            if (service.ServiceID > 0)
            {
                await ExecuteProcAsync(connection, tx, "dbo.usp_Service_Update",
                [
                    SqlParameterFactory.Int("@ServiceID", service.ServiceID),
                    SqlParameterFactory.VarChar("@ServiceName", 150, service.ServiceName),
                    SqlParameterFactory.VarChar("@ServiceDescription", 255, service.ServiceDescription),
                    SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
                ], cancellationToken);
                serviceId = service.ServiceID;
            }
            else
            {
                var output = SqlParameterFactory.OutputInt("@NewServiceID");
                await ExecuteProcAsync(connection, tx, "dbo.usp_Service_Create",
                [
                    SqlParameterFactory.VarChar("@ServiceName", 150, service.ServiceName),
                    SqlParameterFactory.VarChar("@ServiceDescription", 255, service.ServiceDescription),
                    SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
                    output
                ], cancellationToken);
                serviceId = Convert.ToInt32(output.Value);
            }

            await UpsertServiceRefAsync(connection, tx, serviceId, service.ServiceCategoryID, service.ServiceGroupID, cancellationToken);

            await tx.CommitAsync(cancellationToken);
            return serviceId;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<int> SoftDeleteServiceAsync(int serviceId, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteAsync("dbo.usp_Service_SoftDelete",
        [
            SqlParameterFactory.Int("@ServiceID", serviceId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);
        return serviceId;
    }

    public async Task<int> RestoreServiceAsync(int serviceId, CancellationToken cancellationToken = default)
    {
        await ReactivateEntityAsync("Service", "tService", "ServiceID", serviceId, cancellationToken);
        return serviceId;
    }

    public async Task<int> AddServiceTagAsync(int serviceId, int tagId, CancellationToken cancellationToken = default)
    {
        var serviceRefId = await GetPrimaryServiceRefIdAsync(serviceId, cancellationToken);
        if (serviceRefId is null)
        {
            throw new InvalidOperationException($"Service {serviceId} has no ServiceRef row; save the service before adding tags.");
        }

        await _db.ExecuteAsync("dbo.usp_ServiceTag_Add",
        [
            SqlParameterFactory.Int("@ServiceRefID", serviceRefId.Value),
            SqlParameterFactory.Int("@TagID", tagId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);

        return serviceRefId.Value;
    }

    public async Task<int> RemoveServiceTagAsync(int serviceId, int tagId, CancellationToken cancellationToken = default)
    {
        var serviceRefId = await GetPrimaryServiceRefIdAsync(serviceId, cancellationToken);
        if (serviceRefId is null)
        {
            return 0;
        }

        await _db.ExecuteAsync("dbo.usp_ServiceTag_Remove",
        [
            SqlParameterFactory.Int("@ServiceRefID", serviceRefId.Value),
            SqlParameterFactory.Int("@TagID", tagId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);

        return serviceRefId.Value;
    }

    // -- ACTIVITY LOG ----------------------------------------------------

    public Task LogActivityAsync(string entityType, int entityId, string action, string? fieldName = null, string? oldValue = null, string? newValue = null, CancellationToken cancellationToken = default)
        => _db.ExecuteAsync("dbo.usp_ActivityLog_Insert",
        [
            SqlParameterFactory.VarChar("@EntityType", 50, entityType),
            SqlParameterFactory.Int("@EntityID", entityId),
            SqlParameterFactory.Int("@UserID", CmsDefaults.ActingUserId),
            SqlParameterFactory.VarChar("@Action", 30, action),
            SqlParameterFactory.VarChar("@FieldName", 100, fieldName),
            SqlParameterFactory.NVarChar("@OldValue", -1, oldValue),
            SqlParameterFactory.NVarChar("@NewValue", -1, newValue)
        ], cancellationToken);

    public Task<IReadOnlyList<ActivityLog>> GetActivityForEntityAsync(string entityType, int entityId, int top = 100, CancellationToken cancellationToken = default)
        => _db.QueryAsync("dbo.usp_ActivityLog_GetByEntity", MapActivityLog,
        [
            SqlParameterFactory.VarChar("@EntityType", 50, entityType),
            SqlParameterFactory.Int("@EntityID", entityId),
            SqlParameterFactory.Int("@Top", top)
        ], cancellationToken);

    // -- HELPERS ---------------------------------------------------------

    private async Task UpsertServiceRefAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int serviceId,
        int serviceCategoryId,
        int? serviceGroupId,
        CancellationToken cancellationToken)
    {
        if (serviceCategoryId <= 0)
        {
            throw new InvalidOperationException("ServiceCategoryID is required to upsert tServiceRef.");
        }

        var groupId = serviceGroupId is > 0 ? serviceGroupId : null;

        var existingRefId = await GetPrimaryServiceRefIdAsync(connection, tx, serviceId, cancellationToken);
        if (existingRefId is null)
        {
            await ExecuteTextAsync(connection, tx, """
                INSERT INTO dbo.tServiceRef (ServiceCategoryID, ServiceGroupID, ServiceID)
                VALUES (@ServiceCategoryID, @ServiceGroupID, @ServiceID);
                """,
            [
                SqlParameterFactory.Int("@ServiceCategoryID", serviceCategoryId),
                SqlParameterFactory.Int("@ServiceGroupID", groupId),
                SqlParameterFactory.Int("@ServiceID", serviceId)
            ], cancellationToken);
            return;
        }

        await ExecuteTextAsync(connection, tx, """
            UPDATE dbo.tServiceRef
               SET ServiceCategoryID = @ServiceCategoryID,
                   ServiceGroupID = @ServiceGroupID,
                   ServiceID = @ServiceID,
                   IsActive = 1,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE ServiceRefID = @ServiceRefID;
            """,
        [
            SqlParameterFactory.Int("@ServiceRefID", existingRefId.Value),
            SqlParameterFactory.Int("@ServiceCategoryID", serviceCategoryId),
            SqlParameterFactory.Int("@ServiceGroupID", groupId),
            SqlParameterFactory.Int("@ServiceID", serviceId)
        ], cancellationToken);
    }

    private async Task<int?> GetPrimaryServiceRefIdAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int serviceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) ServiceRefID
              FROM dbo.tServiceRef
             WHERE ServiceID = @ServiceID
             ORDER BY ServiceRefID DESC;
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        command.CommandType = System.Data.CommandType.Text;
        command.Parameters.Add(SqlParameterFactory.Int("@ServiceID", serviceId));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    private async Task<int?> GetPrimaryServiceRefIdAsync(int serviceId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) ServiceRefID
              FROM dbo.tServiceRef
             WHERE ServiceID = @ServiceID
             ORDER BY ServiceRefID DESC;
            """;

        var rows = await _db.QueryTextAsync(sql, static r => r.GetInt32(0),
        [
            SqlParameterFactory.Int("@ServiceID", serviceId)
        ], cancellationToken);

        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task ExecuteProcAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string storedProcedure,
        IEnumerable<SqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandType = System.Data.CommandType.StoredProcedure;
        command.CommandText = storedProcedure;
        command.CommandTimeout = 60;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteTextAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string sql,
        IEnumerable<SqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandType = System.Data.CommandType.Text;
        command.CommandText = sql;
        command.CommandTimeout = 60;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ReactivateEntityAsync(string entityType, string tableName, string idColumn, int entityId, CancellationToken cancellationToken)
    {
        var sql = $"""
            UPDATE dbo.{tableName}
               SET IsActive = 1
             WHERE {idColumn} = @EntityID;
            """;

        await _db.ExecuteTextAsync(sql, [SqlParameterFactory.Int("@EntityID", entityId)], cancellationToken);
        await LogActivityAsync(entityType, entityId, "Reactivate", cancellationToken: cancellationToken);
    }

    private static SqlParameter[] BuildUserUpdateParameters(User user) =>
    [
        SqlParameterFactory.Int("@UserID", user.UserID),
        SqlParameterFactory.VarChar("@FirstName", 50, user.FirstName),
        SqlParameterFactory.VarChar("@LastName", 50, user.LastName),
        SqlParameterFactory.VarChar("@Title", 100, user.Title),
        SqlParameterFactory.VarChar("@Email", 150, user.Email),
        SqlParameterFactory.VarChar("@Phone", 30, user.Phone),
        SqlParameterFactory.VarChar("@Company", 150, user.Company),
        SqlParameterFactory.VarChar("@Department", 100, user.Department),
        SqlParameterFactory.VarChar("@UserType", 20, user.UserType),
        SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
    ];

    private static UserAddress MapUserAddress(SqlDataReader r) => new()
    {
        AddressID = r.TryGetNullableInt32("AddressID") ?? 0,
        UserID = r.TryGetNullableInt32("UserID") ?? 0,
        AddressType = r.TryGetNullableString("AddressType") ?? "Work",
        Address1 = r.TryGetNullableString("Address1") ?? string.Empty,
        Address2 = r.TryGetNullableString("Address2"),
        City = r.TryGetNullableString("City") ?? string.Empty,
        State = r.TryGetNullableString("State"),
        PostalCode = r.TryGetNullableString("PostalCode"),
        Country = r.TryGetNullableString("Country") ?? "US"
    };

    private static UserGroup MapUserGroup(SqlDataReader r)
    {
        var isActive = r.TryGetNullableBoolean("IsActive") ?? true;
        return new UserGroup
        {
            UGID = r.TryGetNullableInt32("UGID") ?? r.GetInt32OrDefault("UserGroupID"),
            UserGroupNumber = r.GetNullableString("UserGroupNumber") ?? string.Empty,
            UGName = r.GetNullableString("UGName") ?? r.GetNullableString("UserGroupName") ?? string.Empty,
            UserRoleID = r.TryGetNullableInt32("UserRoleID"),
            UserRoleName = r.TryGetNullableString("UserRoleName"),
            IsDeleted = !isActive,
            CreatedDate = r.TryGetNullableDateTime("CreatedDate"),
            ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
        };
    }

    private static UserGroup MapUserGroupFromTable(SqlDataReader r)
    {
        var isActive = r.TryGetNullableBoolean("IsActive") ?? true;
        return new UserGroup
        {
            UGID = r.GetInt32OrDefault("UserGroupID"),
            UserGroupNumber = r.GetNullableString("UserGroupNumber") ?? string.Empty,
            UGName = r.GetNullableString("UserGroupName") ?? string.Empty,
            IsDeleted = !isActive,
            CreatedDate = r.TryGetNullableDateTime("CreatedDate"),
            ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
        };
    }

    private static UserGroupMember MapUserGroupMember(SqlDataReader r) => new()
    {
        UGID = r.GetInt32OrDefault("UGID"),
        UserGroupNumber = r.TryGetNullableString("UserGroupNumber"),
        UGName = r.TryGetNullableString("UGName"),
        UserID = r.GetInt32OrDefault("UserID"),
        UserFullName = r.TryGetNullableString("UserFullName") ?? r.TryGetNullableString("FullName"),
        Email = r.TryGetNullableString("Email"),
        UserRoleID = r.TryGetNullableInt32("UserRoleID"),
        UserRoleName = r.TryGetNullableString("UserRoleName"),
        JoinedDate = r.TryGetNullableDateTime("JoinedDate") ?? DateTime.MinValue
    };

    private static ClientAdminRow MapClientAdminRow(SqlDataReader r)
    {
        var aeName = r.TryGetNullableString("AccountExecutiveName");
        var alName = r.TryGetNullableString("AnalyticsLeadName");
        return new ClientAdminRow
        {
            ClientID = r.GetInt32OrDefault("ClientID"),
            ClientName = r.TryGetNullableString("ClientName"),
            ClientShortName = r.TryGetNullableString("ClientShortName"),
            ClientDescription = r.TryGetNullableString("ClientDescription"),
            IsActive = r.TryGetNullableBoolean("IsActive") ?? true,
            ClientStatus = r.TryGetNullableString("ClientStatus") ?? "Active",
            AccountExecutiveUserID = r.TryGetNullableInt32("AccountExecutiveUserID"),
            AccountExecutiveName = string.IsNullOrWhiteSpace(aeName) ? null : aeName,
            AnalyticsLeadUserID = r.TryGetNullableInt32("AnalyticsLeadUserID"),
            AnalyticsLeadName = string.IsNullOrWhiteSpace(alName) ? null : alName,
            ClientRefID = r.TryGetNullableInt32("ClientRefID"),
            BusinessUnitID = r.TryGetNullableInt32("BusinessUnitID"),
            BusinessUnitName = r.TryGetNullableString("BusinessUnitName"),
            BusinessClassID = r.TryGetNullableInt32("BusinessClassID"),
            BusinessClassName = BusinessClass.FormatDisplayDescription(
                r.TryGetNullableString("BusinessClassName"),
                r.TryGetNullableString("BusinessClassDescription")),
            ClientTypeID = r.TryGetNullableInt32("ClientTypeID"),
            ClientTypeName = r.TryGetNullableString("ClientTypeName"),
            RefStatusID = r.TryGetNullableInt32("StatusID"),
            RefStatusCode = r.TryGetNullableString("RefStatusCode"),
            RefStatusDescription = r.TryGetNullableString("RefStatusDescription")
        };
    }

    private static Service MapService(SqlDataReader r)
    {
        var isActive = r.TryGetNullableBoolean("IsActive") ?? true;
        return new Service
        {
            ServiceID = r.GetInt32OrDefault("ServiceID"),
            ServiceName = r.GetNullableString("ServiceName") ?? string.Empty,
            ServiceDescription = r.GetNullableString("ServiceDescription"),
            ServiceCategoryID = r.GetInt32OrDefault("ServiceCategoryID"),
            ServiceGroupID = r.TryGetNullableInt32("ServiceGroupID"),
            ServiceGroupName = r.TryGetNullableString("ServiceGroupName"),
            CategoryName = r.TryGetNullableString("CategoryName"),
            ServiceCategoryDescription = r.TryGetNullableString("ServiceCategoryDescription"),
            IsActive = isActive,
            IsDeleted = !isActive,
            CreatedDate = r.TryGetNullableDateTime("CreatedDate"),
            ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
        };
    }

    private static ActivityLog MapActivityLog(SqlDataReader r) => new()
    {
        LogID = r.GetInt64OrDefault("LogID"),
        EntityType = r.GetNullableString("EntityType") ?? string.Empty,
        EntityID = r.GetInt32OrDefault("EntityID"),
        ActionDate = r.GetDateTimeOrDefault("ActionDate"),
        Action = r.GetNullableString("Action") ?? string.Empty,
        FieldName = r.TryGetNullableString("FieldName"),
        OldValue = r.TryGetNullableString("OldValue"),
        NewValue = r.TryGetNullableString("NewValue")
    };
}
