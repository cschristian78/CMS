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

    private const string UserDeleteSql = """
        CREATE OR ALTER PROCEDURE dbo.usp_User_Delete
            @UserID INT,
            @ActingUserID INT
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            IF NOT EXISTS (SELECT 1 FROM dbo.tUser WHERE UserID = @UserID)
                THROW 50001, 'tUser not found.', 1;

            DECLARE @Keeper INT = @ActingUserID;
            IF @Keeper = @UserID OR NOT EXISTS (SELECT 1 FROM dbo.tUser WHERE UserID = @Keeper)
                SELECT TOP (1) @Keeper = UserID FROM dbo.tUser WHERE UserID <> @UserID ORDER BY UserID;
            IF @Keeper IS NULL
                THROW 50002, 'Cannot delete the only user because some records must keep an owner.', 1;

            BEGIN TRY
                BEGIN TRAN;

                DECLARE @AddressIds TABLE (AddressID INT PRIMARY KEY);
                INSERT INTO @AddressIds (AddressID)
                SELECT r.AddressID
                  FROM dbo.tUserAddressRef AS r
                 WHERE r.UserID = @UserID
                   AND NOT EXISTS (
                        SELECT 1
                          FROM dbo.tUserAddressRef AS other
                         WHERE other.AddressID = r.AddressID
                           AND other.UserID <> @UserID);

                EXEC dbo.usp_ActivityLog_Insert
                    @EntityType = 'User',
                    @EntityID = @UserID,
                    @UserID = @Keeper,
                    @Action = 'Remove';

                UPDATE dbo.tClient SET AccountExecutiveUserID = NULL WHERE AccountExecutiveUserID = @UserID;
                UPDATE dbo.tClient SET AnalyticsLeadUserID = NULL WHERE AnalyticsLeadUserID = @UserID;
                UPDATE dbo.tProjectNote SET CreatedByUserID = NULL WHERE CreatedByUserID = @UserID;

                DECLARE @UserIdText NVARCHAR(20) = CONVERT(NVARCHAR(20), @UserID);
                DECLARE @KeeperText NVARCHAR(20) = CONVERT(NVARCHAR(20), @Keeper);

                IF OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NOT NULL
                    EXEC(N'UPDATE dbo.tProjectCalendarItem SET OwnerUserID = NULL WHERE OwnerUserID = ' + @UserIdText);
                IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
                BEGIN
                    EXEC(N'UPDATE dbo.tProjectFile SET UploadedByUserID = ' + @KeeperText + N' WHERE UploadedByUserID = ' + @UserIdText);
                    EXEC(N'UPDATE dbo.tProjectFile SET ApprovalUpdatedByUserID = NULL WHERE ApprovalUpdatedByUserID = ' + @UserIdText);
                END;
                IF OBJECT_ID(N'dbo.tProjectRevenue', N'U') IS NOT NULL
                    EXEC(N'UPDATE dbo.tProjectRevenue SET RecordedBy = ' + @KeeperText + N' WHERE RecordedBy = ' + @UserIdText);
                IF OBJECT_ID(N'dbo.tProjectTimeEntry', N'U') IS NOT NULL
                    EXEC(N'DELETE FROM dbo.tProjectTimeEntry WHERE UserID = ' + @UserIdText);
                IF OBJECT_ID(N'dbo.tUserTimeEntry', N'U') IS NOT NULL
                    EXEC(N'DELETE FROM dbo.tUserTimeEntry WHERE UserID = ' + @UserIdText);
                IF OBJECT_ID(N'dbo.tCatAiMessage', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.tCatAiConversation', N'U') IS NOT NULL
                    EXEC(N'DELETE m FROM dbo.tCatAiMessage AS m INNER JOIN dbo.tCatAiConversation AS c ON c.ConversationID = m.ConversationID WHERE c.UserID = ' + @UserIdText);
                IF OBJECT_ID(N'dbo.tCatAiConversation', N'U') IS NOT NULL
                    EXEC(N'DELETE FROM dbo.tCatAiConversation WHERE UserID = ' + @UserIdText);

                DELETE FROM dbo.tUserGroupRef WHERE UserID = @UserID;
                DELETE FROM dbo.tActivityLog WHERE UserID = @UserID;
                DELETE FROM dbo.tUser WHERE UserID = @UserID;

                DELETE a
                  FROM dbo.tAddress AS a
                  INNER JOIN @AddressIds AS i ON i.AddressID = a.AddressID
                 WHERE NOT EXISTS (SELECT 1 FROM dbo.tUserAddressRef AS r WHERE r.AddressID = a.AddressID);

                COMMIT TRAN;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRAN;
                THROW;
            END CATCH
        END;
        """;

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

    public async Task<IReadOnlyList<UserContactInfo>> GetUserContactInfoByIdsAsync(
        IEnumerable<int> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        const string sql = """
            SELECT
                u.UserID,
                u.Email,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS FullName
              FROM dbo.tUser AS u
             WHERE u.IsActive = 1
               AND u.UserID IN (
                   SELECT TRY_CAST(LTRIM(RTRIM(value)) AS INT)
                     FROM STRING_SPLIT(@UserIds, CHAR(31))
                    WHERE TRY_CAST(LTRIM(RTRIM(value)) AS INT) IS NOT NULL
               )
             ORDER BY u.LastName, u.FirstName, u.UserID;
            """;

        var payload = string.Join((char)31, ids);
        return await _db.QueryTextAsync(
            sql,
            static r =>
            {
                var email = r.TryGetNullableString("Email") ?? string.Empty;
                var fullName = r.TryGetNullableString("FullName") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    fullName = email;
                }

                return new UserContactInfo(
                    r.GetInt32OrDefault("UserID"),
                    email.Trim(),
                    fullName.Trim());
            },
            [SqlParameterFactory.NVarChar("@UserIds", -1, payload)],
            cancellationToken);
    }

    private async Task<bool> ExistsScalarAsync(string sql, SqlParameter parameter, CancellationToken cancellationToken)
    {
        var rows = await _db.QueryTextAsync(sql, static r => r.GetInt32(0), [parameter], cancellationToken);
        return rows.Count > 0 && rows[0] != 0;
    }

    // -- USERS -----------------------------------------------------------

    public async Task EnsureUserTypeOptionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            IF EXISTS (
                SELECT 1
                  FROM sys.check_constraints
                 WHERE name = N'CK_tUser_UserType'
                   AND parent_object_id = OBJECT_ID(N'dbo.tUser')
            )
            BEGIN
                ALTER TABLE dbo.tUser DROP CONSTRAINT CK_tUser_UserType;
            END;

            UPDATE dbo.tUser
               SET UserType = N'Basic',
                   ModifiedDate = SYSUTCDATETIME()
             WHERE UserType = N'Internal';

            UPDATE dbo.tUser
               SET UserType = N'Administrator',
                   ModifiedDate = SYSUTCDATETIME()
             WHERE (
                       LOWER(LTRIM(RTRIM(Email))) = N'scott.christian@bmsgroup.com'
                    OR (
                           LOWER(LTRIM(RTRIM(FirstName))) = N'scott'
                       AND LOWER(LTRIM(RTRIM(LastName))) = N'christian'
                       )
                   )
               AND IsActive = 1;

            IF NOT EXISTS (
                SELECT 1
                  FROM sys.check_constraints
                 WHERE name = N'CK_tUser_UserType'
                   AND parent_object_id = OBJECT_ID(N'dbo.tUser')
            )
            BEGIN
                ALTER TABLE dbo.tUser
                    ADD CONSTRAINT CK_tUser_UserType
                    CHECK (UserType IN (N'Basic', N'External', N'Administrator', N'Manager', N'Analyst'));
            END;

            IF COL_LENGTH('dbo.tUser', 'UserFacts') IS NULL
            BEGIN
                ALTER TABLE dbo.tUser ADD UserFacts NVARCHAR(1000) NULL;
            END;

            IF COL_LENGTH('dbo.tUser', 'Bio') IS NULL
            BEGIN
                ALTER TABLE dbo.tUser ADD Bio NVARCHAR(4000) NULL;
            END;
            """;

        await _db.ExecuteTextAsync(sql, cancellationToken: cancellationToken);

        // Keep create SP validation aligned with the new allowed values.
        const string spSql = """
            CREATE OR ALTER PROCEDURE dbo.usp_User_CreateWithPrimaryAddress
                @FirstName                    VARCHAR(50),
                @LastName                     VARCHAR(50),
                @Title                        VARCHAR(100) = NULL,
                @Email                        VARCHAR(150),
                @Phone                        VARCHAR(30) = NULL,
                @Company                      VARCHAR(150) = NULL,
                @Department                   VARCHAR(100) = NULL,
                @UserFacts                    NVARCHAR(1000) = NULL,
                @Bio                          NVARCHAR(4000) = NULL,
                @UserType                     VARCHAR(20),
                @AddressType                  VARCHAR(20) = NULL,
                @Address1                     VARCHAR(200) = NULL,
                @Address2                     VARCHAR(200) = NULL,
                @City                         VARCHAR(100) = NULL,
                @State                        VARCHAR(50)  = NULL,
                @PostalCode                   VARCHAR(20)  = NULL,
                @Country                      VARCHAR(50)  = NULL,
                @ActingUserID                INT,
                @NewUserID                   INT OUTPUT,
                @NewAddressID                INT = NULL OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                IF @UserType NOT IN ('Basic', 'External', 'Administrator', 'Manager', 'Analyst')
                    THROW 50003, 'UserType must be Basic, External, Administrator, Manager, or Analyst.', 1;

                IF EXISTS (SELECT 1 FROM dbo.tUser WHERE Email = @Email AND IsActive = 1)
                    THROW 50004, 'Email already exists.', 1;

                SET @NewAddressID = NULL;

                BEGIN TRY
                    BEGIN TRAN;

                    INSERT INTO dbo.tUser (FirstName, LastName, Title, Email, Phone, Company, Department, UserFacts, Bio, UserType)
                    VALUES (@FirstName, @LastName, @Title, @Email, @Phone, @Company, @Department, @UserFacts, @Bio, @UserType);

                    SET @NewUserID = SCOPE_IDENTITY();

                    EXEC dbo.usp_ActivityLog_Insert
                        @EntityType = 'User',
                        @EntityID   = @NewUserID,
                        @UserID     = @ActingUserID,
                        @Action     = 'Insert';

                    IF NULLIF(LTRIM(RTRIM(@Address1)), '') IS NOT NULL
                    BEGIN
                        DECLARE @AddrType VARCHAR(20) = COALESCE(NULLIF(LTRIM(RTRIM(@AddressType)), ''), 'Work');
                        DECLARE @AddrCountry VARCHAR(50) = COALESCE(NULLIF(LTRIM(RTRIM(@Country)), ''), 'US');

                        INSERT INTO dbo.tAddress (Address1, Address2, City, State, PostalCode, Country)
                        VALUES (
                            LTRIM(RTRIM(@Address1)),
                            @Address2,
                            @City,
                            @State,
                            @PostalCode,
                            @AddrCountry
                        );

                        SET @NewAddressID = SCOPE_IDENTITY();

                        EXEC dbo.usp_ActivityLog_Insert
                            @EntityType = 'Address',
                            @EntityID   = @NewAddressID,
                            @UserID     = @ActingUserID,
                            @Action     = 'Insert';

                        INSERT INTO dbo.tUserAddressRef (UserID, AddressID, AddressType, IsPrimary)
                        VALUES (@NewUserID, @NewAddressID, @AddrType, 1);

                        EXEC dbo.usp_ActivityLog_Insert
                            @EntityType = 'UserAddressRef',
                            @EntityID   = @NewUserID,
                            @UserID     = @ActingUserID,
                            @Action     = 'Insert';
                    END;

                    COMMIT TRAN;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
                    THROW;
                END CATCH
            END;
            """;

        await _db.ExecuteTextAsync(spSql, cancellationToken: cancellationToken);

        const string userCreateSql = """
            CREATE OR ALTER PROCEDURE dbo.usp_User_Create
                @FirstName                    VARCHAR(50),
                @LastName                     VARCHAR(50),
                @Title                        VARCHAR(100) = NULL,
                @Email                        VARCHAR(150),
                @Phone                        VARCHAR(30) = NULL,
                @Company                      VARCHAR(150) = NULL,
                @Department                   VARCHAR(100) = NULL,
                @UserFacts                    NVARCHAR(1000) = NULL,
                @Bio                          NVARCHAR(4000) = NULL,
                @UserType                     VARCHAR(20),
                @ActingUserID                INT,
                @NewUserID                   INT OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                BEGIN TRY
                    BEGIN TRAN;

                    INSERT INTO dbo.tUser (FirstName, LastName, Title, Email, Phone, Company, Department, UserFacts, Bio, UserType)
                    VALUES (@FirstName, @LastName, @Title, @Email, @Phone, @Company, @Department, @UserFacts, @Bio, @UserType);

                    SET @NewUserID = SCOPE_IDENTITY();

                    EXEC dbo.usp_ActivityLog_Insert
                        @EntityType = 'User',
                        @EntityID   = @NewUserID,
                        @UserID     = @ActingUserID,
                        @Action     = 'Insert';

                    COMMIT TRAN;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
                    THROW;
                END CATCH
            END;
            """;

        const string userUpdateSql = """
            CREATE OR ALTER PROCEDURE dbo.usp_User_Update
                @UserID                       INT,
                @FirstName                    VARCHAR(50) = NULL,
                @LastName                     VARCHAR(50) = NULL,
                @Title                        VARCHAR(100) = NULL,
                @Email                        VARCHAR(150) = NULL,
                @Phone                        VARCHAR(30) = NULL,
                @Company                      VARCHAR(150) = NULL,
                @Department                   VARCHAR(100) = NULL,
                @UserFacts                    NVARCHAR(1000) = NULL,
                @Bio                          NVARCHAR(4000) = NULL,
                @UserType                     VARCHAR(20) = NULL,
                @ActingUserID                INT
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                IF NOT EXISTS (SELECT 1 FROM dbo.tUser WHERE UserID = @UserID)
                    THROW 50001, 'tUser not found.', 1;

                BEGIN TRY
                    BEGIN TRAN;

                    UPDATE dbo.tUser
                       SET
                    FirstName = COALESCE(@FirstName, FirstName),
                    LastName = COALESCE(@LastName, LastName),
                    Title = COALESCE(@Title, Title),
                    Email = COALESCE(@Email, Email),
                    Phone = COALESCE(@Phone, Phone),
                    Company = COALESCE(@Company, Company),
                    Department = COALESCE(@Department, Department),
                    UserFacts = @UserFacts,
                    Bio = @Bio,
                    UserType = COALESCE(@UserType, UserType),
                    ModifiedDate = SYSUTCDATETIME()
                     WHERE UserID = @UserID;

                    EXEC dbo.usp_ActivityLog_Insert
                        @EntityType = 'User',
                        @EntityID   = @UserID,
                        @UserID     = @ActingUserID,
                        @Action     = 'Update';

                    COMMIT TRAN;
                END TRY
                BEGIN CATCH
                    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
                    THROW;
                END CATCH
            END;
            """;

        await _db.ExecuteTextAsync(userCreateSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(userUpdateSql, cancellationToken: cancellationToken);
        await _db.ExecuteTextAsync(UserDeleteSql, cancellationToken: cancellationToken);
    }

    public async Task<int> SaveUserAsync(User user, CancellationToken cancellationToken = default)
    {
        await EnsureUserTypeOptionsAsync(cancellationToken);

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
            SqlParameterFactory.NVarChar("@UserFacts", 1000, user.UserFacts),
            SqlParameterFactory.NVarChar("@Bio", 4000, user.Bio),
            SqlParameterFactory.VarChar("@UserType", 20, user.UserType),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId),
            output
        ], "@NewUserID", cancellationToken);

        return Convert.ToInt32(output.Value);
    }

    public async Task SaveUserBioAsync(int userId, string? bio, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            throw new InvalidOperationException("Save the user first, then add a bio.");
        }

        const string addColumnSql = """
            IF OBJECT_ID(N'dbo.tUser', N'U') IS NULL
                RETURN;

            IF COL_LENGTH(N'dbo.tUser', N'Bio') IS NULL
            BEGIN
                ALTER TABLE dbo.tUser ADD Bio NVARCHAR(4000) NULL;
            END;
            """;

        await _db.ExecuteTextAsync(addColumnSql, cancellationToken: cancellationToken);

        const string sql = """
            UPDATE dbo.tUser
               SET Bio = @Bio,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE UserID = @UserID;

            IF @@ROWCOUNT = 0
                THROW 50001, 'tUser not found.', 1;
            """;

        await _db.ExecuteTextAsync(
            sql,
            [
                SqlParameterFactory.Int("@UserID", userId),
                SqlParameterFactory.NVarChar("@Bio", 4000, bio),
            ],
            cancellationToken);
    }

    public async Task<int> CreateUserWithPrimaryAddressAsync(User user, UserAddress? primaryAddress, CancellationToken cancellationToken = default)
    {
        await EnsureUserTypeOptionsAsync(cancellationToken);

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
            SqlParameterFactory.NVarChar("@UserFacts", 1000, user.UserFacts),
            SqlParameterFactory.NVarChar("@Bio", 4000, user.Bio),
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

    public async Task DeleteUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        await _db.ExecuteTextAsync(UserDeleteSql, cancellationToken: cancellationToken);
        try
        {
            await _db.ExecuteAsync("dbo.usp_User_Delete",
            [
                SqlParameterFactory.Int("@UserID", userId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 50001 or 50002)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
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

    public async Task<IReadOnlyList<UserAddress>> ListUserAddressesAsync(int userId, CancellationToken cancellationToken = default)
    {
        await EnsureUserAddressMapSchemaAsync(cancellationToken);
        const string sql = """
            SELECT r.UserID,
                   r.AddressID,
                   r.AddressType,
                   a.Address1, a.Address2, a.City, a.State, a.PostalCode, a.Country,
                   a.Latitude, a.Longitude
              FROM dbo.tUserAddressRef AS r
              JOIN dbo.tAddress AS a ON a.AddressID = r.AddressID
             WHERE r.UserID = @UserID
               AND r.IsActive = 1
               AND a.IsActive = 1
             ORDER BY r.AddressType, r.IsPrimary DESC;
            """;
        return await _db.QueryTextAsync(sql, MapUserAddress, [SqlParameterFactory.Int("@UserID", userId)], cancellationToken);
    }

    public async Task<int> AddUserAddressAsync(UserAddress a, CancellationToken cancellationToken = default)
    {
        var output = SqlParameterFactory.OutputInt("@NewAddressID");
        await _db.ExecuteWithOutputIntAsync("dbo.usp_Address_Create",
        [
            SqlParameterFactory.VarCharAsWritten("@Address1", 200, a.Address1),
            SqlParameterFactory.VarCharAsWritten("@Address2", 200, a.Address2 ?? string.Empty),
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
            SqlParameterFactory.VarCharAsWritten("@Address2", 200, a.Address2 ?? string.Empty),
            SqlParameterFactory.VarCharAsWritten("@City", 100, a.City),
            SqlParameterFactory.VarChar("@State", 50, a.State),
            SqlParameterFactory.VarChar("@PostalCode", 20, a.PostalCode),
            SqlParameterFactory.VarCharAsWritten("@Country", 50, a.Country),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);

        const string typeSql = """
            UPDATE dbo.tUserAddressRef
               SET AddressType = @AddressType
             WHERE UserID = @UserID
               AND AddressID = @AddressID
               AND AddressType <> @AddressType;
            """;
        await _db.ExecuteTextAsync(typeSql,
        [
            SqlParameterFactory.Int("@UserID", a.UserID),
            SqlParameterFactory.Int("@AddressID", a.AddressID),
            SqlParameterFactory.VarCharAsWritten("@AddressType", 20, a.AddressType)
        ], cancellationToken);

        await EnsureUserAddressMapSchemaAsync(cancellationToken);
        await ClearAddressGeocodeAsync(a.AddressID, cancellationToken);
        return a.AddressID;
    }

    public Task EnsureUserAddressMapSchemaAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            IF COL_LENGTH('dbo.tAddress', 'Latitude') IS NULL
                ALTER TABLE dbo.tAddress ADD Latitude DECIMAL(9, 6) NULL;
            IF COL_LENGTH('dbo.tAddress', 'Longitude') IS NULL
                ALTER TABLE dbo.tAddress ADD Longitude DECIMAL(9, 6) NULL;
            IF COL_LENGTH('dbo.tAddress', 'GeocodedUtc') IS NULL
                ALTER TABLE dbo.tAddress ADD GeocodedUtc DATETIME2(0) NULL;
            """;
        return _db.ExecuteTextAsync(sql, cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<UserMapAddressRow>> ListUserMapAddressesAsync(
        string? department,
        string addressType,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                u.UserID,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS Name,
                ISNULL(u.Department, N'') AS Department,
                ISNULL(a.AddressID, 0) AS AddressID,
                r.AddressType,
                ISNULL(a.Address1, N'') AS Address1,
                ISNULL(a.Address2, N'') AS Address2,
                ISNULL(a.City, N'') AS City,
                ISNULL(a.State, N'') AS State,
                ISNULL(a.PostalCode, N'') AS PostalCode,
                ISNULL(a.Country, N'') AS Country,
                a.Latitude,
                a.Longitude,
                a.GeocodedUtc,
                a.ModifiedDate
            FROM dbo.tUser AS u
            LEFT JOIN dbo.tUserAddressRef AS r
                ON r.UserID = u.UserID
               AND r.IsActive = 1
               AND r.AddressType = @AddressType
            LEFT JOIN dbo.tAddress AS a
                ON a.AddressID = r.AddressID
               AND a.IsActive = 1
            WHERE u.IsActive = 1
              AND (
                    @Department = N''
                 OR LOWER(LTRIM(RTRIM(ISNULL(u.Department, N'')))) = LOWER(@Department)
              )
            ORDER BY u.LastName, u.FirstName, u.UserID;
            """;

        return _db.QueryTextAsync(sql, r => new UserMapAddressRow
        {
            UserID = r.GetInt32OrDefault("UserID"),
            Name = r.TryGetNullableString("Name") ?? string.Empty,
            Department = r.TryGetNullableString("Department") ?? string.Empty,
            AddressID = r.GetInt32OrDefault("AddressID"),
            AddressType = r.TryGetNullableString("AddressType") ?? addressType,
            Address1 = r.TryGetNullableString("Address1") ?? string.Empty,
            Address2 = r.TryGetNullableString("Address2") ?? string.Empty,
            City = r.TryGetNullableString("City") ?? string.Empty,
            State = r.TryGetNullableString("State") ?? string.Empty,
            PostalCode = r.TryGetNullableString("PostalCode") ?? string.Empty,
            Country = r.TryGetNullableString("Country") ?? string.Empty,
            Latitude = ReadNullableDouble(r, "Latitude"),
            Longitude = ReadNullableDouble(r, "Longitude"),
            GeocodedUtc = r.TryGetNullableDateTime("GeocodedUtc"),
            ModifiedDate = r.TryGetNullableDateTime("ModifiedDate")
        },
        [
            SqlParameterFactory.NVarCharMax("@Department", department?.Trim() ?? string.Empty),
            SqlParameterFactory.VarChar("@AddressType", 20, addressType)
        ], cancellationToken);
    }

    public Task SaveAddressGeocodeAsync(int addressId, double? latitude, double? longitude, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.tAddress
               SET Latitude = @Latitude,
                   Longitude = @Longitude,
                   GeocodedUtc = SYSUTCDATETIME()
             WHERE AddressID = @AddressID;
            """;
        return _db.ExecuteTextAsync(sql,
        [
            SqlParameterFactory.Int("@AddressID", addressId),
            SqlParameterFactory.Float("@Latitude", latitude),
            SqlParameterFactory.Float("@Longitude", longitude)
        ], cancellationToken);
    }

    public Task ClearAddressGeocodeAsync(int addressId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.tAddress
               SET Latitude = NULL,
                   Longitude = NULL,
                   GeocodedUtc = NULL
             WHERE AddressID = @AddressID;
            """;
        return _db.ExecuteTextAsync(sql, [SqlParameterFactory.Int("@AddressID", addressId)], cancellationToken);
    }

    public async Task<UserMapAddressRow?> GetAddressForGeocodeAsync(int addressId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT AddressID,
                   ISNULL(Address1, N'') AS Address1,
                   ISNULL(City, N'') AS City,
                   ISNULL(State, N'') AS State,
                   ISNULL(PostalCode, N'') AS PostalCode,
                   ISNULL(Country, N'') AS Country
              FROM dbo.tAddress
             WHERE AddressID = @AddressID
               AND IsActive = 1;
            """;
        var rows = await _db.QueryTextAsync(sql, static r => new UserMapAddressRow
        {
            AddressID = r.GetInt32OrDefault("AddressID"),
            Address1 = r.TryGetNullableString("Address1") ?? string.Empty,
            City = r.TryGetNullableString("City") ?? string.Empty,
            State = r.TryGetNullableString("State") ?? string.Empty,
            PostalCode = r.TryGetNullableString("PostalCode") ?? string.Empty,
            Country = r.TryGetNullableString("Country") ?? string.Empty
        }, [SqlParameterFactory.Int("@AddressID", addressId)], cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    private static double? ReadNullableDouble(Microsoft.Data.SqlClient.SqlDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (!string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return reader.IsDBNull(i) ? null : Convert.ToDouble(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture);
        }

        return null;
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

    public Task SoftDeleteProjectTypeAsync(int projectTypeId, CancellationToken cancellationToken = default) =>
        _db.ExecuteAsync("dbo.usp_ProjectType_SoftDelete",
        [
            SqlParameterFactory.Int("@ProjectTypeID", projectTypeId),
            SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
        ], cancellationToken);

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

    public async Task HardDeleteBusinessUnitAsync(int businessUnitId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.ExecuteAsync("dbo.usp_BusinessUnit_HardDelete",
            [
                SqlParameterFactory.Int("@BusinessUnitID", businessUnitId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 50001 or 50002)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    public async Task HardDeleteBusinessClassAsync(int businessClassId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.ExecuteAsync("dbo.usp_BusinessClass_HardDelete",
            [
                SqlParameterFactory.Int("@BusinessClassID", businessClassId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 50001 or 50002)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
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

    public async Task<int> SaveServiceGroupAsync(ServiceGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        var number = (group.ServiceGroupNumber ?? string.Empty).Trim();
        var name = (group.ServiceGroupName ?? string.Empty).Trim();
        var description = (group.ServiceGroupDescription ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Service group name is required.");
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            number = await GetNextServiceGroupNumberAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = name;
        }

        if (group.ServiceGroupID > 0)
        {
            await _db.ExecuteAsync("dbo.usp_ServiceGroup_Update",
            [
                SqlParameterFactory.Int("@ServiceGroupID", group.ServiceGroupID),
                SqlParameterFactory.VarChar("@ServiceGroupNumber", 50, number),
                SqlParameterFactory.VarChar("@ServiceGroupName", 100, name),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);

            await _db.ExecuteTextAsync(
                """
                UPDATE dbo.tServiceGroup
                   SET ServiceGroupDescription = @ServiceGroupDescription,
                       IsActive = @IsActive
                 WHERE ServiceGroupID = @ServiceGroupID;
                """,
                [
                    SqlParameterFactory.Int("@ServiceGroupID", group.ServiceGroupID),
                    SqlParameterFactory.VarChar("@ServiceGroupDescription", 255, description),
                    SqlParameterFactory.Bit("@IsActive", group.IsActive),
                ],
                cancellationToken);

            return group.ServiceGroupID;
        }

        // Create via text so Description (NOT NULL on tServiceGroup) is always set.
        var inserted = await _db.QueryTextAsync(
            """
            INSERT INTO dbo.tServiceGroup
                (ServiceGroupNumber, ServiceGroupName, ServiceGroupDescription, IsActive)
            OUTPUT INSERTED.ServiceGroupID AS ServiceGroupID
            VALUES
                (@ServiceGroupNumber, @ServiceGroupName, @ServiceGroupDescription, @IsActive);
            """,
            static r => r.GetInt32OrDefault("ServiceGroupID"),
            [
                SqlParameterFactory.VarChar("@ServiceGroupNumber", 50, number),
                SqlParameterFactory.VarChar("@ServiceGroupName", 100, name),
                SqlParameterFactory.VarChar("@ServiceGroupDescription", 255, description),
                SqlParameterFactory.Bit("@IsActive", group.IsActive),
            ],
            cancellationToken);

        var newId = inserted.FirstOrDefault();
        if (newId <= 0)
        {
            throw new InvalidOperationException("Service group create did not return a new id.");
        }

        await LogActivityAsync(
            "ServiceGroup",
            newId,
            "Insert",
            cancellationToken: cancellationToken);

        return newId;
    }

    private async Task<string> GetNextServiceGroupNumberAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.QueryTextAsync(
            """
            SELECT ISNULL(MAX(TRY_CONVERT(INT, NULLIF(LTRIM(RTRIM(ServiceGroupNumber)), N''))), 0) + 1 AS NextNumber
              FROM dbo.tServiceGroup;
            """,
            static r => r.GetInt32OrDefault("NextNumber"),
            cancellationToken: cancellationToken);
        var next = rows.FirstOrDefault();
        return next > 0 ? next.ToString() : "1";
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
                   ORDER BY r.ModifiedDate DESC, r.ClientRefID DESC
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

    public async Task HardDeleteClientAsync(int clientId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.ExecuteAsync("dbo.usp_Client_HardDelete",
            [
                SqlParameterFactory.Int("@ClientID", clientId),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 50001 or 50002)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
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

        var existingRefId = await ResolveClientRefIdForClassificationAsync(clientId, businessUnitId, cancellationToken);

        // Nothing to classify yet and no existing row to update — leave the client unclassified.
        if (businessUnitId is null && businessClassId is null && clientTypeId is null && existingRefId is null)
        {
            return;
        }

        try
        {
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
        catch (SqlException ex) when (ex.Number is 2601 or 2627
            && ex.Message.Contains("UX_tClientRef_Client_BU_Active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This client already has an active classification for that business unit. Each client can have only one active business unit (US, UK, LA, or Global) row.",
                ex);
        }
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

    /// <summary>
    /// Unique index <c>UX_tClientRef_Client_BU_Active</c> allows one active ref per
    /// (ClientID, BusinessUnitID). Update the matching BU row rather than moving another
    /// row onto a BU that already exists.
    /// </summary>
    private async Task<int?> ResolveClientRefIdForClassificationAsync(
        int clientId,
        int? businessUnitId,
        CancellationToken cancellationToken)
    {
        if (businessUnitId is > 0)
        {
            var matching = await GetClientRefIdForBusinessUnitAsync(clientId, businessUnitId.Value, cancellationToken);
            if (matching is > 0)
            {
                return matching;
            }

            var activeCount = await CountActiveClientRefsAsync(clientId, cancellationToken);
            if (activeCount > 1)
            {
                return null;
            }
        }

        return await GetPrimaryClientRefIdAsync(clientId, cancellationToken);
    }

    private async Task<int?> GetClientRefIdForBusinessUnitAsync(
        int clientId,
        int businessUnitId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) ClientRefID
              FROM dbo.tClientRef
             WHERE ClientID = @ClientID
               AND BusinessUnitID = @BusinessUnitID
               AND IsActive = 1
             ORDER BY ClientRefID;
            """;

        var rows = await _db.QueryTextAsync(sql, static r => r.GetInt32(0),
        [
            SqlParameterFactory.Int("@ClientID", clientId),
            SqlParameterFactory.Int("@BusinessUnitID", businessUnitId)
        ], cancellationToken);

        return rows.Count == 0 ? null : rows[0];
    }

    private async Task<int> CountActiveClientRefsAsync(int clientId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CAST(COUNT(*) AS int) AS RefCount
              FROM dbo.tClientRef
             WHERE ClientID = @ClientID
               AND IsActive = 1;
            """;

        var rows = await _db.QueryTextAsync(sql, static r => r.GetInt32(0),
        [
            SqlParameterFactory.Int("@ClientID", clientId)
        ], cancellationToken);
        return rows.Count == 0 ? 0 : rows[0];
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

    public async Task HardDeleteUserGroupAsync(int ugid, CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.ExecuteAsync("dbo.usp_UserGroup_HardDelete",
            [
                SqlParameterFactory.Int("@UserGroupID", ugid),
                SqlParameterFactory.Int("@ActingUserID", CmsDefaults.ActingUserId)
            ], cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 50001 or 50002)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
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
                u.Title,
                u.Email,
                u.Phone,
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

    public async Task<IReadOnlyList<ActivityLog>> GetActivityForEntityAsync(
        string entityType,
        int entityId,
        int top = 100,
        CancellationToken cancellationToken = default)
    {
        // Live USP signature: @EntityType, @EntityID, @FromDate, @ToDate (no @Top).
        var rows = await _db.QueryAsync(
            "dbo.usp_ActivityLog_GetByEntity",
            MapActivityLog,
            [
                SqlParameterFactory.VarChar("@EntityType", 50, entityType),
                SqlParameterFactory.Int("@EntityID", entityId)
            ],
            cancellationToken);

        if (top <= 0 || rows.Count <= top)
        {
            return rows;
        }

        return rows.Take(top).ToList();
    }

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
        SqlParameterFactory.NVarChar("@UserFacts", 1000, user.UserFacts),
        SqlParameterFactory.NVarChar("@Bio", 4000, user.Bio),
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
        Country = r.TryGetNullableString("Country") ?? "US",
        Latitude = ReadNullableDouble(r, "Latitude"),
        Longitude = ReadNullableDouble(r, "Longitude")
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
        Title = r.TryGetNullableString("Title"),
        Email = r.TryGetNullableString("Email"),
        Phone = r.TryGetNullableString("Phone"),
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
