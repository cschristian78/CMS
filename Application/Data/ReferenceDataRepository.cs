using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

/// <summary>
/// Reads against the CMS_PhaseI_All schema: <c>dbo.vUser</c>, <c>dbo.vClient</c>,
/// <c>dbo.vUserGroup</c>, plus the lookup <c>_List</c> stored procedures.
/// </summary>
public sealed class ReferenceDataRepository : IReferenceDataRepository
{
    private readonly IDbExecutor _db;

    public ReferenceDataRepository(IDbExecutor db)
    {
        _db = db;
    }

    public Task<IReadOnlyList<Status>> GetStatusesAsync(CancellationToken cancellationToken = default)
        => _db.QueryAsync("dbo.usp_Status_List", MapStatus,
        [
            SqlParameterFactory.Bit("@ActiveOnly", true)
        ], cancellationToken);

    // The CMS_PhaseI_All_v3 schema models statuses as a flat dbo.tStatus lookup (no per-entity
    // tStatusAppliesTo), so entity-scoped lookups return the same list.
    public Task<IReadOnlyList<Status>> GetStatusesForEntityAsync(string entityType, CancellationToken cancellationToken = default)
        => GetStatusesAsync(cancellationToken);

    public Task<IReadOnlyList<BusinessUnit>> GetBusinessUnitsAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        // dbo.usp_BusinessUnit_List (deployed): @ActiveOnly = 0 returns all rows; @ActiveOnly = 1 restricts to IsActive = 1.
        return _db.QueryAsync("dbo.usp_BusinessUnit_List", MapBusinessUnit,
        [
            SqlParameterFactory.Bit("@ActiveOnly", activeOnly)
        ], cancellationToken);
    }

    public Task<IReadOnlyList<BusinessClass>> GetBusinessClassesAsync(CancellationToken cancellationToken = default)
        => _db.QueryAsync("dbo.usp_BusinessClass_List", MapBusinessClass,
        [
            SqlParameterFactory.Bit("@ActiveOnly", false)
        ], cancellationToken);

    public Task<IReadOnlyList<BusinessClass>> GetBusinessClassesForBusinessUnitAsync(int businessUnitId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT
                bc.BusinessClassID,
                bc.BusinessClassName,
                bc.BusinessClassDescription
              FROM dbo.tClientRef AS cr
              INNER JOIN dbo.tBusinessClass AS bc ON bc.BusinessClassID = cr.BusinessClassID
             WHERE cr.BusinessUnitID = @BusinessUnitID
               AND cr.IsActive = 1
               AND bc.IsActive = 1
             ORDER BY bc.BusinessClassDescription, bc.BusinessClassName, bc.BusinessClassID;
            """;

        return _db.QueryTextAsync(
            sql,
            MapBusinessClass,
            [SqlParameterFactory.Int("@BusinessUnitID", businessUnitId)],
            cancellationToken);
    }

    public Task<IReadOnlyList<BusinessClass>> GetBusinessClassesForClientAsync(int clientId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT
                bc.BusinessClassID,
                bc.BusinessClassName,
                bc.BusinessClassDescription
              FROM dbo.tClient AS c
              OUTER APPLY (
                  SELECT TOP (1) cr.BusinessUnitID
                    FROM dbo.tClientRef AS cr
                   WHERE cr.ClientID = c.ClientID
                     AND cr.IsActive = 1
              ) AS owner_ref
              INNER JOIN dbo.tClientRef AS cr
                      ON cr.BusinessUnitID = owner_ref.BusinessUnitID
                     AND cr.IsActive = 1
              INNER JOIN dbo.tBusinessClass AS bc
                      ON bc.BusinessClassID = cr.BusinessClassID
                     AND bc.IsActive = 1
             WHERE c.ClientID = @ClientID
             ORDER BY bc.BusinessClassDescription, bc.BusinessClassName, bc.BusinessClassID;
            """;

        return _db.QueryTextAsync(
            sql,
            MapBusinessClass,
            [SqlParameterFactory.Int("@ClientID", clientId)],
            cancellationToken);
    }

    public Task<IReadOnlyList<ClientCategory>> GetClientCategoriesAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
        => _db.QueryAsync("dbo.usp_ClientTypeRef_List", MapClientCategoryFromTypeRef,
        [
            SqlParameterFactory.Bit("@ActiveOnly", activeOnly)
        ], cancellationToken);

    public Task<IReadOnlyList<ClientType>> GetClientTypesAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        return _db.QueryAsync("dbo.usp_ClientType_List", MapClientType,
        [
            SqlParameterFactory.Bit("@ActiveOnly", activeOnly)
        ], cancellationToken);
    }

    public Task<IReadOnlyList<ProjectType>> GetProjectTypesAsync(CancellationToken cancellationToken = default)
    {
        return _db.QueryAsync("dbo.usp_ProjectType_List", MapProjectType, cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<ServiceCategory>> GetServiceCategoriesAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        return _db.QueryAsync("dbo.usp_ServiceCategory_List", MapServiceCategory,
        [
            SqlParameterFactory.Bit("@ActiveOnly", activeOnly)
        ], cancellationToken);
    }

    public Task<IReadOnlyList<ServiceGroup>> GetServiceGroupsAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        return _db.QueryAsync("dbo.usp_ServiceGroup_List", MapServiceGroup,
        [
            SqlParameterFactory.Bit("@ActiveOnly", activeOnly)
        ], cancellationToken);
    }

    public async Task<IReadOnlyList<Tag>> GetTagsAsync(string? scope = null, CancellationToken cancellationToken = default)
    {
        var rows = await _db.QueryAsync("dbo.usp_Tag_List", MapTag,
        [
            SqlParameterFactory.Bit("@ActiveOnly", false)
        ], cancellationToken);

        if (string.IsNullOrWhiteSpace(scope))
        {
            return rows;
        }

        return rows
            .Where(t => string.Equals(t.TagScope, scope, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public Task<IReadOnlyList<User>> GetActiveUsersAsync(CancellationToken cancellationToken = default)
        => _db.QueryAsync("dbo.usp_User_List", MapUser,
        [
            SqlParameterFactory.Bit("@ActiveOnly", true)
        ], cancellationToken);

    public Task<IReadOnlyList<User>> GetAllUsersForPicklistAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                u.UserID,
                u.FirstName,
                u.LastName,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS FullName,
                u.Title,
                u.Department,
                u.Email,
                u.Phone,
                u.Company,
                u.UserFacts,
                u.Bio,
                u.UserType,
                u.IsActive,
                u.CreatedDate,
                u.ModifiedDate,
                pa.AddressID        AS PrimaryAddressID,
                pa.AddressType      AS PrimaryAddressType,
                pa.Address1         AS PrimaryAddress1,
                pa.Address2         AS PrimaryAddress2,
                pa.City             AS PrimaryCity,
                pa.State            AS PrimaryState,
                pa.PostalCode       AS PrimaryPostalCode,
                pa.Country          AS PrimaryCountry
            FROM dbo.tUser AS u
            OUTER APPLY (
                SELECT TOP (1) r.AddressID, r.AddressType, a.Address1, a.Address2,
                               a.City, a.State, a.PostalCode, a.Country
                FROM dbo.tUserAddressRef AS r
                INNER JOIN dbo.tAddress AS a ON a.AddressID = r.AddressID
                WHERE r.UserID = u.UserID
                  AND r.IsActive = 1
                  AND a.IsActive = 1
                ORDER BY CASE WHEN r.IsPrimary = 1 THEN 0 ELSE 1 END,
                         CASE WHEN r.AddressType = N'Work' THEN 0 ELSE 1 END,
                         r.AddressID DESC
            ) AS pa
            ORDER BY u.LastName, u.FirstName, u.UserID;
            """;

        return _db.QueryTextAsync(sql, MapUser, null, cancellationToken);
    }

    public Task<IReadOnlyList<User>> GetTopUsersAsync(string? searchTerm = null, bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return _db.QueryAsync("dbo.usp_User_List", MapUser,
            [
                SqlParameterFactory.Bit("@ActiveOnly", activeOnly)
            ], cancellationToken);
        }

        const string sql = """
            SELECT
                u.UserID,
                u.FirstName,
                u.LastName,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS FullName,
                u.Title,
                u.Department,
                u.Email,
                u.Phone,
                u.Company,
                u.UserType,
                u.IsActive,
                u.CreatedDate,
                u.ModifiedDate
            FROM dbo.tUser AS u
            WHERE (@ActiveOnly = 0 OR u.IsActive = 1)
              AND (
                    u.Email LIKE @SearchPattern
                 OR u.FirstName LIKE @SearchPattern
                 OR u.LastName LIKE @SearchPattern
                 OR CONCAT(u.FirstName, N' ', u.LastName) LIKE @SearchPattern
              )
            ORDER BY u.LastName, u.FirstName, u.UserID;
            """;

        return _db.QueryTextAsync(sql, MapUser,
        [
            SqlParameterFactory.Bit("@ActiveOnly", activeOnly),
            SqlParameterFactory.NVarChar("@SearchPattern", 102, $"%{searchTerm.Trim()}%")
        ], cancellationToken);
    }

    public Task<IReadOnlyList<User>> GetBmsUsersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                u.UserID,
                u.FirstName,
                u.LastName,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS FullName,
                u.Title,
                u.Department,
                u.Email,
                u.Phone,
                u.Company,
                u.UserType,
                u.IsActive,
                u.CreatedDate,
                u.ModifiedDate
            FROM dbo.tUser AS u
            WHERE u.Company LIKE N'BMS%'
              AND u.IsActive = 1
            ORDER BY u.LastName, u.FirstName;
            """;

        return _db.QueryTextAsync(sql, MapUser, null, cancellationToken);
    }

    public Task<IReadOnlyList<UserRole>> GetUserRolesAsync(CancellationToken cancellationToken = default)
        => _db.QueryAsync("dbo.usp_UserRole_List", MapUserRole,
        [
            SqlParameterFactory.Bit("@ActiveOnly", false)
        ], cancellationToken);

    public Task<IReadOnlyList<UserGroupSummary>> GetTopUserGroupSummariesAsync(string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                ug.UserGroupID AS UGID,
                ug.UserGroupNumber,
                ug.UserGroupName AS UGName,
                (
                    SELECT COUNT(1)
                    FROM dbo.tUserGroupRef AS ugr
                    WHERE ugr.UserGroupID = ug.UserGroupID
                ) AS MemberCount
            FROM dbo.tUserGroup AS ug
            WHERE ug.IsActive = 1
              AND (@SearchTerm IS NULL OR ug.UserGroupNumber LIKE @SearchPattern OR ug.UserGroupName LIKE @SearchPattern)
            ORDER BY ug.UserGroupName, ug.UserGroupID;
            """;

        return _db.QueryTextAsync(sql, MapUserGroupSummary,
        [
            SqlParameterFactory.NVarChar("@SearchTerm", 100, string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm.Trim()),
            SqlParameterFactory.NVarChar("@SearchPattern", 102, string.IsNullOrWhiteSpace(searchTerm) ? null : $"%{searchTerm.Trim()}%")
        ], cancellationToken);
    }

    public Task<IReadOnlyList<UserGroupSummary>> GetUserGroupsUsedOnClientProjectsAsync(int clientId, CancellationToken cancellationToken = default)
    {
        // Groups assigned on ProjectRef.UserGroupID OR inferred via project users' membership.
        const string sql = """
            SELECT DISTINCT
                ug.UserGroupID AS UGID,
                ug.UserGroupNumber,
                ug.UserGroupName AS UGName,
                (
                    SELECT COUNT(1)
                    FROM dbo.tUserGroupRef AS ugr2
                    WHERE ugr2.UserGroupID = ug.UserGroupID
                ) AS MemberCount
            FROM dbo.tUserGroup AS ug
            WHERE ug.IsActive = 1
              AND ug.UserGroupID IN (
                    SELECT pr.UserGroupID
                      FROM dbo.tProject AS p
                      INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectID = p.ProjectID AND pr.IsActive = 1
                      INNER JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID AND cr.IsActive = 1
                     WHERE cr.ClientID = @ClientId
                       AND p.IsActive = 1
                       AND pr.UserGroupID IS NOT NULL
                    UNION
                    SELECT ugr.UserGroupID
                      FROM dbo.tProject AS p
                      INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectID = p.ProjectID AND pr.IsActive = 1
                      INNER JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID AND cr.IsActive = 1
                      INNER JOIN dbo.tProjectUser AS pu ON pu.ProjectRefID = pr.ProjectRefID AND pu.IsActive = 1
                      INNER JOIN dbo.tUserGroupRef AS ugr ON ugr.UserID = pu.UserID
                     WHERE cr.ClientID = @ClientId
                       AND p.IsActive = 1
              )
            ORDER BY ug.UserGroupNumber;
            """;

        return _db.QueryTextAsync(sql, MapUserGroupSummary,
        [
            SqlParameterFactory.Int("@ClientId", clientId)
        ], cancellationToken);
    }

    private static Status MapStatus(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        StatusID = reader.GetInt32OrDefault("StatusID"),
        StatusTypeID = reader.TryGetNullableInt32("StatusTypeID") ?? 0,
        StatusTypeName = reader.TryGetNullableString("StatusTypeName") ?? reader.TryGetNullableString("TypeName"),
        StatusCode = reader.GetNullableString("StatusCode"),
        StatusDescription = reader.GetNullableString("StatusDescription"),
        IsActive = reader.TryGetNullableBoolean("IsActive") ?? true
    };

    private static BusinessUnit MapBusinessUnit(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        BusinessUnitID = reader.GetInt32OrDefault("BusinessUnitID"),
        BusinessUnitName = reader.GetNullableString("BusinessUnitName"),
        BusinessUnitDescription = reader.GetNullableString("BusinessUnitDescription"),
        IsActive = reader.TryGetNullableBoolean("IsActive") ?? true,
        CreatedDate = reader.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = reader.TryGetNullableDateTime("ModifiedDate")
    };

    private static BusinessClass MapBusinessClass(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        BusinessClassID = reader.GetInt32OrDefault("BusinessClassID"),
        BusinessClassName = reader.GetNullableString("BusinessClassName"),
        BusinessClassDescription = reader.GetNullableString("BusinessClassDescription"),
        BusinessUnitID = reader.TryGetNullableInt32("BusinessUnitID") ?? 0,
        BusinessUnitName = reader.TryGetNullableString("BusinessUnitName"),
        CreatedDate = reader.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = reader.TryGetNullableDateTime("ModifiedDate")
    };

    private static ClientCategory MapClientCategoryFromTypeRef(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        ClientCategoryID = reader.GetInt32OrDefault("ClientTypeRefID"),
        CCName = reader.GetNullableString("ClientTypeRefName") ?? string.Empty,
        IsActive = reader.TryGetNullableBoolean("IsActive") ?? true
    };

    private static ClientCategory MapClientCategory(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        ClientCategoryID = reader.GetInt32OrDefault("ClientCategoryID"),
        CCName = reader.GetNullableString("CCName") ?? string.Empty,
        CCDescription = reader.GetNullableString("CCDescription"),
        IsActive = reader.TryGetNullableBoolean("IsActive") ?? true,
        CreatedDate = reader.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = reader.TryGetNullableDateTime("ModifiedDate")
    };

    private static ClientType MapClientType(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        ClientTypeID = reader.GetInt32OrDefault("ClientTypeID"),
        CTName = reader.TryGetNullableString("CTName") ?? reader.TryGetNullableString("ClientTypeName") ?? string.Empty,
        CTDescription = reader.TryGetNullableString("CTDescription") ?? reader.TryGetNullableString("ClientTypeDescription"),
        IsActive = reader.TryGetNullableBoolean("IsActive") ?? true,
        CreatedDate = reader.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = reader.TryGetNullableDateTime("ModifiedDate")
    };

    private static ProjectType MapProjectType(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        ProjectTypeID = reader.GetInt32OrDefault("ProjectTypeID"),
        PTCode = reader.TryGetNullableString("PTCode") ?? reader.TryGetNullableString("ProjectTypeCode"),
        PTDescription = reader.TryGetNullableString("PTDescription") ?? reader.TryGetNullableString("ProjectTypeDescription"),
        IsActive = reader.TryGetNullableBoolean("IsActive") ?? true,
        CreatedDate = reader.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = reader.TryGetNullableDateTime("ModifiedDate")
    };

    private static ServiceGroup MapServiceGroup(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        ServiceGroupID = reader.GetInt32OrDefault("ServiceGroupID"),
        ServiceGroupNumber = reader.TryGetNullableString("ServiceGroupNumber") ?? string.Empty,
        ServiceGroupName = reader.GetNullableString("ServiceGroupName") ?? string.Empty,
        ServiceGroupDescription = reader.TryGetNullableString("ServiceGroupDescription"),
        IsActive = reader.TryGetNullableBoolean("IsActive") ?? true
    };

    private static ServiceCategory MapServiceCategory(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        ServiceCategoryID = reader.GetInt32OrDefault("ServiceCategoryID"),
        CategoryName = reader.GetNullableString("CategoryName") ?? string.Empty,
        FunctionID = reader.TryGetNullableInt32("FunctionID") ?? 0,
        ServiceCategoryDescription = reader.TryGetNullableString("ServiceCategoryDescription"),
        IsActive = reader.TryGetNullableBoolean("IsActive") ?? true,
        CreatedDate = reader.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = reader.TryGetNullableDateTime("ModifiedDate")
    };

    private static Tag MapTag(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        TagID = reader.GetInt32OrDefault("TagID"),
        TagName = reader.GetNullableString("TagName") ?? string.Empty,
        TagScope = reader.GetNullableString("TagScope") ?? string.Empty,
        CreatedDate = reader.TryGetNullableDateTime("CreatedDate")
    };

    private static User MapUser(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        UserID = reader.GetInt32OrDefault("UserID"),
        FirstName = reader.GetNullableString("FirstName"),
        LastName = reader.GetNullableString("LastName"),
        FullName = reader.TryGetNullableString("FullName"),
        Email = reader.GetNullableString("Email"),
        Phone = reader.GetNullableString("Phone"),
        Company = reader.GetNullableString("Company"),
        Department = reader.TryGetNullableString("Department"),
        Title = reader.TryGetNullableString("Title"),
        UserFacts = reader.TryGetNullableString("UserFacts"),
        Bio = reader.TryGetNullableString("Bio"),
        UserType = reader.TryGetNullableString("UserType") ?? "Internal",
        StatusID = reader.TryGetNullableInt32("StatusID") ?? 0,
        StatusCode = reader.TryGetNullableString("StatusCode"),
        StatusDescription = reader.TryGetNullableString("StatusDescription"),
        IsDeleted = !(reader.TryGetNullableBoolean("IsActive") ?? !(reader.TryGetNullableBoolean("IsDeleted") ?? false)),
        DeletedDate = reader.TryGetNullableDateTime("DeletedDate"),
        CreatedDate = reader.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = reader.TryGetNullableDateTime("ModifiedDate"),
        PrimaryAddress = TryReadPrimaryAddress(reader)
    };

    /// <summary>Builds a <see cref="UserAddress"/> from the optional aliased columns
    /// (<c>PrimaryAddressID</c>, <c>PrimaryAddress1</c>, ...) produced by
    /// <see cref="GetAllUsersForPicklistAsync"/>'s OUTER APPLY. Returns <c>null</c> when
    /// the columns aren't present in the reader or no address row was joined.</summary>
    private static UserAddress? TryReadPrimaryAddress(Microsoft.Data.SqlClient.SqlDataReader reader)
    {
        var addressId = reader.TryGetNullableInt32("PrimaryAddressID");
        if (addressId is null or 0)
        {
            return null;
        }

        return new UserAddress
        {
            AddressID = addressId.Value,
            UserID = reader.GetInt32OrDefault("UserID"),
            AddressType = reader.TryGetNullableString("PrimaryAddressType") ?? "Work",
            Address1 = reader.TryGetNullableString("PrimaryAddress1") ?? string.Empty,
            Address2 = reader.TryGetNullableString("PrimaryAddress2"),
            City = reader.TryGetNullableString("PrimaryCity") ?? string.Empty,
            State = reader.TryGetNullableString("PrimaryState"),
            PostalCode = reader.TryGetNullableString("PrimaryPostalCode"),
            Country = reader.TryGetNullableString("PrimaryCountry") ?? "US",
            CreatedDate = reader.TryGetNullableDateTime("PrimaryAddressCreatedDate"),
            ModifiedDate = reader.TryGetNullableDateTime("PrimaryAddressModifiedDate")
        };
    }

    private static UserRole MapUserRole(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        UserRoleID = reader.TryGetNullableInt32("UserRoleID") ?? 0,
        UserRoleName = reader.TryGetNullableString("UserRoleName"),
        RoleDescription = reader.TryGetNullableString("RoleDescription"),
        Tag1 = reader.TryGetNullableString("Tag1"),
        Tag2 = reader.TryGetNullableString("Tag2"),
        Tag3 = reader.TryGetNullableString("Tag3"),
        StatusID = reader.TryGetNullableInt32("StatusID"),
        StatusCode = reader.TryGetNullableString("StatusCode"),
        StatusDescription = reader.TryGetNullableString("StatusDescription"),
        IsActive = reader.TryGetNullableBoolean("IsActive") ?? true,
        CreatedDate = reader.TryGetNullableDateTime("CreatedDate"),
        ModifiedDate = reader.TryGetNullableDateTime("ModifiedDate")
    };

    private static UserGroupSummary MapUserGroupSummary(Microsoft.Data.SqlClient.SqlDataReader reader) => new()
    {
        UGID = reader.TryGetNullableInt32("UGID") ?? reader.GetInt32OrDefault("UserGroupID"),
        UserGroupNumber = reader.TryGetNullableString("UserGroupNumber") ?? string.Empty,
        UGName = reader.TryGetNullableString("UGName") ?? reader.TryGetNullableString("UserGroupName") ?? string.Empty,
        MemberCount = reader.GetInt32OrDefault("MemberCount")
    };
}
