using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

public sealed class AuthRepository : IAuthRepository
{
    private readonly IDbExecutor _db;

    public AuthRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task EnsureLoginPwSchemaAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            IF COL_LENGTH('dbo.tUser', 'LoginPw') IS NULL
            BEGIN
                ALTER TABLE dbo.tUser
                    ADD LoginPw VARCHAR(500) NOT NULL
                        CONSTRAINT DF_tUser_LoginPw DEFAULT ('none');
            END
            ELSE IF EXISTS (
                SELECT 1
                  FROM sys.columns
                 WHERE object_id = OBJECT_ID(N'dbo.tUser')
                   AND name = N'LoginPw'
                   AND max_length > 0
                   AND max_length < 500
            )
            BEGIN
                ALTER TABLE dbo.tUser ALTER COLUMN LoginPw VARCHAR(500) NOT NULL;
            END;

            IF COL_LENGTH('dbo.tUser', 'LoginCount') IS NULL
            BEGIN
                ALTER TABLE dbo.tUser
                    ADD LoginCount INT NOT NULL
                        CONSTRAINT DF_tUser_LoginCount DEFAULT (0);
            END;

            IF COL_LENGTH('dbo.tUser', 'LastLoginUtc') IS NULL
            BEGIN
                ALTER TABLE dbo.tUser
                    ADD LastLoginUtc DATETIME2(0) NULL;
            END;
            """;

        await _db.ExecuteTextAsync(sql, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<AuthCatUserDto>> ListCatLoginUsersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoginPwSchemaAsync(cancellationToken);

        const string sql = """
            SELECT
                u.UserID,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS UserFullName,
                u.Email,
                CASE
                    WHEN LOWER(LTRIM(RTRIM(ISNULL(u.LoginPw, N'none')))) = N'none' THEN CAST(1 AS bit)
                    ELSE CAST(0 AS bit)
                END AS MustSetPassword
              FROM dbo.tUser AS u
             WHERE u.IsActive = 1
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND NULLIF(LTRIM(RTRIM(u.Email)), N'') IS NOT NULL
             ORDER BY UserFullName, u.UserID;
            """;

        return await _db.QueryTextAsync(
            sql,
            static r => new AuthCatUserDto
            {
                UserID = r.GetInt32OrDefault("UserID"),
                UserFullName = r.TryGetNullableString("UserFullName") ?? string.Empty,
                Email = (r.TryGetNullableString("Email") ?? string.Empty).Trim(),
                MustSetPassword = r.TryGetNullableBoolean("MustSetPassword") ?? true,
            },
            cancellationToken: cancellationToken);
    }

    public async Task<AuthLoginUserRow?> FindActiveCatUserByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        await EnsureLoginPwSchemaAsync(cancellationToken);

        var normalized = (email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        const string sql = """
            SELECT TOP (1)
                u.UserID,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS UserFullName,
                u.Email,
                ISNULL(u.LoginPw, N'none') AS LoginPw,
                ISNULL(NULLIF(LTRIM(RTRIM(u.UserType)), N''), N'Basic') AS UserType,
                CAST(u.IsActive AS bit) AS IsActive
              FROM dbo.tUser AS u
             WHERE u.IsActive = 1
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND LOWER(LTRIM(RTRIM(u.Email))) = LOWER(@Email);
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            static r => new AuthLoginUserRow
            {
                UserID = r.GetInt32OrDefault("UserID"),
                UserFullName = r.TryGetNullableString("UserFullName") ?? string.Empty,
                Email = (r.TryGetNullableString("Email") ?? string.Empty).Trim(),
                LoginPw = r.TryGetNullableString("LoginPw") ?? "none",
                UserType = r.TryGetNullableString("UserType") ?? "Basic",
                IsActive = r.TryGetNullableBoolean("IsActive") ?? false,
            },
            [SqlParameterFactory.VarChar("@Email", 150, normalized)],
            cancellationToken);

        return rows.FirstOrDefault();
    }

    public async Task<string?> GetUserTypeAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            return null;
        }

        const string sql = """
            SELECT TOP (1)
                ISNULL(NULLIF(LTRIM(RTRIM(u.UserType)), N''), N'Basic') AS UserType
              FROM dbo.tUser AS u
             WHERE u.UserID = @UserID
               AND u.IsActive = 1;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            static r => r.TryGetNullableString("UserType") ?? "Basic",
            [SqlParameterFactory.Int("@UserID", userId)],
            cancellationToken);

        return rows.FirstOrDefault();
    }

    public async Task SetLoginPasswordAsync(int userId, string password, CancellationToken cancellationToken = default)
    {
        await EnsureLoginPwSchemaAsync(cancellationToken);

        const string sql = """
            UPDATE dbo.tUser
               SET LoginPw = @LoginPw,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE UserID = @UserID
               AND IsActive = 1
               AND UPPER(LTRIM(RTRIM(Department))) = N'CAT';
            """;

        await _db.ExecuteTextAsync(
            sql,
            [
                SqlParameterFactory.Int("@UserID", userId),
                SqlParameterFactory.VarCharAsWritten("@LoginPw", 500, password),
            ],
            cancellationToken);
    }

    public async Task RecordSuccessfulLoginAsync(int userId, CancellationToken cancellationToken = default)
    {
        await EnsureLoginPwSchemaAsync(cancellationToken);

        const string sql = """
            UPDATE dbo.tUser
               SET LoginCount = ISNULL(LoginCount, 0) + 1,
                   LastLoginUtc = SYSUTCDATETIME(),
                   ModifiedDate = SYSUTCDATETIME()
             WHERE UserID = @UserID
               AND IsActive = 1;
            """;

        await _db.ExecuteTextAsync(
            sql,
            [SqlParameterFactory.Int("@UserID", userId)],
            cancellationToken);
    }
}
