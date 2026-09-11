using ClientManagementSystem.Models;
using Microsoft.Data.SqlClient;

namespace ClientManagementSystem.Data;

public sealed class TmsRepository : ITmsRepository
{
    private readonly IDbExecutor _db;

    public TmsRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task EnsureUserTimeEntrySchemaAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.tUserTimeEntry', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.tUserTimeEntry
                (
                    UserTimeEntryID  INT             IDENTITY(1,1) NOT NULL
                        CONSTRAINT PK_tUserTimeEntry PRIMARY KEY CLUSTERED,
                    UserID           INT             NOT NULL
                        CONSTRAINT FK_tUserTimeEntry_UserID
                            REFERENCES dbo.tUser (UserID),
                    Category         VARCHAR(40)     NOT NULL,
                    WorkDate         DATE            NOT NULL,
                    Hours            DECIMAL(5,2)    NOT NULL
                        CONSTRAINT CK_tUserTimeEntry_Hours CHECK (Hours >= 0 AND Hours <= 24),
                    EntryMode        VARCHAR(20)     NOT NULL
                        CONSTRAINT DF_tUserTimeEntry_EntryMode DEFAULT ('Exact')
                        CONSTRAINT CK_tUserTimeEntry_EntryMode CHECK (EntryMode IN ('Exact', 'Allocated')),
                    Notes            NVARCHAR(500)   NULL,
                    IsActive         BIT             NOT NULL
                        CONSTRAINT DF_tUserTimeEntry_IsActive DEFAULT (1),
                    CreatedDate      DATETIME2(0)    NOT NULL
                        CONSTRAINT DF_tUserTimeEntry_CreatedDate DEFAULT (SYSUTCDATETIME()),
                    ModifiedDate     DATETIME2(0)    NULL
                );
            END;

            IF EXISTS (
                SELECT 1
                  FROM sys.check_constraints
                 WHERE name = N'CK_tUserTimeEntry_Category'
                   AND parent_object_id = OBJECT_ID(N'dbo.tUserTimeEntry'))
            BEGIN
                ALTER TABLE dbo.tUserTimeEntry DROP CONSTRAINT CK_tUserTimeEntry_Category;
            END;

            IF OBJECT_ID(N'dbo.tUserTimeEntry', N'U') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1
                      FROM sys.check_constraints
                     WHERE name = N'CK_tUserTimeEntry_Category'
                       AND parent_object_id = OBJECT_ID(N'dbo.tUserTimeEntry'))
            BEGIN
                ALTER TABLE dbo.tUserTimeEntry WITH CHECK
                ADD CONSTRAINT CK_tUserTimeEntry_Category
                CHECK (Category IN (
                    'Meetings', 'Holidays', 'PTO', 'Travel',
                    'Training', 'CSI', 'Moodys MAT', 'Verisk MAT',
                    'Peril Advisory', 'Administrative'));
            END;

            IF OBJECT_ID(N'dbo.tUserTimeEntry', N'U') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1
                      FROM sys.indexes
                     WHERE name = N'UX_tUserTimeEntry_ActiveDayCategory'
                       AND object_id = OBJECT_ID(N'dbo.tUserTimeEntry'))
            BEGIN
                CREATE UNIQUE INDEX UX_tUserTimeEntry_ActiveDayCategory
                    ON dbo.tUserTimeEntry (UserID, Category, WorkDate)
                    WHERE IsActive = 1;
            END;
            """;

        await _db.ExecuteTextAsync(sql, cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<TmsCatUser>> ListCatUsersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                u.UserID,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS UserFullName,
                u.Email
              FROM dbo.tUser AS u
             WHERE u.IsActive = 1
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
             ORDER BY UserFullName, u.UserID;
            """;

        return _db.QueryTextAsync(
            sql,
            static r => new TmsCatUser
            {
                UserID = r.GetInt32OrDefault("UserID"),
                UserFullName = r.TryGetNullableString("UserFullName") ?? string.Empty,
                Email = r.TryGetNullableString("Email"),
            },
            cancellationToken: cancellationToken);
    }

    public Task<IReadOnlyList<TmsUserProject>> ListProjectsForUserAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT
                p.ProjectID,
                pr.ProjectRefID,
                ISNULL(p.ProjectName, N'') AS ProjectName,
                c.ClientName,
                CAST(ISNULL(p.IsOnline, 0) AS BIT) AS IsOnline
              FROM dbo.tUserGroupRef AS ugr
              INNER JOIN dbo.tProjectRef AS pr
                ON pr.UserGroupID = ugr.UserGroupID
               AND pr.IsActive = 1
              INNER JOIN dbo.tProject AS p
                ON p.ProjectID = pr.ProjectID
               AND p.IsActive = 1
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
             WHERE ugr.UserID = @UserID
             ORDER BY IsOnline DESC, c.ClientName, ProjectName, p.ProjectID;
            """;

        return _db.QueryTextAsync(
            sql,
            static r => new TmsUserProject
            {
                ProjectID = r.GetInt32OrDefault("ProjectID"),
                ProjectRefID = r.GetInt32OrDefault("ProjectRefID"),
                ProjectName = r.TryGetNullableString("ProjectName") ?? string.Empty,
                ClientName = r.TryGetNullableString("ClientName"),
                IsOnline = r.TryGetNullableBoolean("IsOnline") ?? false,
            },
            [SqlParameterFactory.Int("@UserID", userId)],
            cancellationToken);
    }

    public async Task<IReadOnlyList<UserTimeEntry>> ListOtherTimeForWeekAsync(
        int userId,
        DateTime weekStartDate,
        CancellationToken cancellationToken = default)
    {
        await EnsureUserTimeEntrySchemaAsync(cancellationToken);

        var weekEnd = weekStartDate.Date.AddDays(6);
        const string sql = """
            SELECT
                te.UserTimeEntryID,
                te.UserID,
                te.Category,
                te.WorkDate,
                te.Hours,
                te.EntryMode,
                te.Notes
              FROM dbo.tUserTimeEntry AS te
             WHERE te.UserID = @UserID
               AND te.IsActive = 1
               AND te.WorkDate >= @WeekStartDate
               AND te.WorkDate <= @WeekEndDate
             ORDER BY te.Category, te.WorkDate;
            """;

        return await _db.QueryTextAsync(
            sql,
            MapOtherEntry,
            [
                SqlParameterFactory.Int("@UserID", userId),
                SqlParameterFactory.Date("@WeekStartDate", weekStartDate.Date),
                SqlParameterFactory.Date("@WeekEndDate", weekEnd),
            ],
            cancellationToken);
    }

    public async Task SaveOtherTimeForWeekAsync(
        int userId,
        DateTime weekStartDate,
        IReadOnlyList<UserTimeEntry> entries,
        CancellationToken cancellationToken = default)
    {
        await EnsureUserTimeEntrySchemaAsync(cancellationToken);

        var monday = weekStartDate.Date;
        var weekEnd = monday.AddDays(6);
        var incoming = (entries ?? [])
            .Where(e => TmsOtherTimeCategories.IsValid(e.Category))
            .Select(e => new
            {
                Category = TmsOtherTimeCategories.Normalize(e.Category),
                WorkDate = e.WorkDate.Date,
                Hours = e.Hours < 0 ? 0m : (e.Hours > 24m ? 24m : e.Hours),
                EntryMode = string.Equals(e.EntryMode, "Allocated", StringComparison.OrdinalIgnoreCase)
                    ? "Allocated"
                    : "Exact",
                Notes = e.Notes,
            })
            .Where(e => e.WorkDate >= monday && e.WorkDate <= weekEnd)
            .GroupBy(e => (e.Category, e.WorkDate))
            .Select(g => g.Last())
            .ToList();

        var existing = await ListOtherTimeForWeekAsync(userId, monday, cancellationToken);
        var touched = new HashSet<(string Category, DateTime WorkDate)>();

        foreach (var row in incoming)
        {
            touched.Add((row.Category, row.WorkDate));
            if (row.Hours <= 0m)
            {
                await SoftDeleteOtherAsync(userId, row.Category, row.WorkDate, cancellationToken);
                continue;
            }

            await UpsertOtherAsync(
                userId,
                row.Category,
                row.WorkDate,
                row.Hours,
                row.EntryMode,
                row.Notes,
                cancellationToken);
        }

        foreach (var row in existing)
        {
            var key = (TmsOtherTimeCategories.Normalize(row.Category), row.WorkDate.Date);
            if (!touched.Contains(key))
            {
                await SoftDeleteOtherAsync(userId, key.Item1, key.Item2, cancellationToken);
            }
        }
    }

    private Task UpsertOtherAsync(
        int userId,
        string category,
        DateTime workDate,
        decimal hours,
        string entryMode,
        string? notes,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @ExistingID INT =
            (
                SELECT TOP (1) UserTimeEntryID
                  FROM dbo.tUserTimeEntry
                 WHERE UserID = @UserID
                   AND Category = @Category
                   AND WorkDate = @WorkDate
                   AND IsActive = 1
            );

            IF @ExistingID IS NOT NULL
            BEGIN
                UPDATE dbo.tUserTimeEntry
                   SET Hours = @Hours,
                       EntryMode = @EntryMode,
                       Notes = @Notes,
                       ModifiedDate = SYSUTCDATETIME()
                 WHERE UserTimeEntryID = @ExistingID;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.tUserTimeEntry
                    (UserID, Category, WorkDate, Hours, EntryMode, Notes, IsActive)
                VALUES
                    (@UserID, @Category, @WorkDate, @Hours, @EntryMode, @Notes, 1);
            END
            """;

        return _db.ExecuteTextAsync(
            sql,
            [
                SqlParameterFactory.Int("@UserID", userId),
                SqlParameterFactory.VarChar("@Category", 40, category),
                SqlParameterFactory.Date("@WorkDate", workDate.Date),
                SqlParameterFactory.Decimal("@Hours", hours),
                SqlParameterFactory.VarChar("@EntryMode", 20, entryMode),
                SqlParameterFactory.NVarChar("@Notes", 500, notes),
            ],
            cancellationToken);
    }

    private Task SoftDeleteOtherAsync(
        int userId,
        string category,
        DateTime workDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.tUserTimeEntry
               SET IsActive = 0,
                   ModifiedDate = SYSUTCDATETIME()
             WHERE UserID = @UserID
               AND Category = @Category
               AND WorkDate = @WorkDate
               AND IsActive = 1;
            """;

        return _db.ExecuteTextAsync(
            sql,
            [
                SqlParameterFactory.Int("@UserID", userId),
                SqlParameterFactory.VarChar("@Category", 40, category),
                SqlParameterFactory.Date("@WorkDate", workDate.Date),
            ],
            cancellationToken);
    }

    private static UserTimeEntry MapOtherEntry(SqlDataReader r) => new()
    {
        UserTimeEntryID = r.GetInt32OrDefault("UserTimeEntryID"),
        UserID = r.GetInt32OrDefault("UserID"),
        Category = r.TryGetNullableString("Category") ?? string.Empty,
        WorkDate = r.GetDateTimeOrDefault("WorkDate"),
        Hours = r.GetNullableDecimal("Hours") ?? 0m,
        EntryMode = r.TryGetNullableString("EntryMode") ?? "Exact",
        Notes = r.TryGetNullableString("Notes"),
    };

    public async Task<TmsReportResponse> GetReportAsync(
        int year,
        int? userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureUserTimeEntrySchemaAsync(cancellationToken);

        if (year < 2000 || year > 2100)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        var catUsers = await ListCatUsersAsync(cancellationToken);
        if (userId is > 0 && !catUsers.Any(u => u.UserID == userId.Value))
        {
            throw new InvalidOperationException("The selected user is not an active CAT department member.");
        }

        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = new DateTime(year, 12, 31);
        var priorYear = year - 1;
        var priorStart = new DateTime(priorYear, 1, 1);
        var priorEnd = new DateTime(priorYear, 12, 31);

        var availableYears = await ListAvailableYearsAsync(cancellationToken);
        if (availableYears.Count == 0)
        {
            availableYears = [year, priorYear];
        }

        var byAnalystProject = await QueryNamedHoursAsync(
            """
            SELECT
                u.UserID AS Id,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS Label,
                CAST(NULL AS NVARCHAR(200)) AS Detail,
                SUM(te.Hours) AS Hours,
                COUNT(DISTINCT p.ProjectID) AS Cnt
              FROM dbo.tProjectTimeEntry AS te
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = te.ProjectRefID AND pr.IsActive = 1
              INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
              INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
             WHERE te.IsActive = 1
               AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND (@UserID IS NULL OR te.UserID = @UserID)
             GROUP BY u.UserID, u.FirstName, u.LastName
             ORDER BY Hours DESC, Label
            """,
            yearStart,
            yearEnd,
            userId,
            cancellationToken);

        var byAnalystOther = await QueryNamedHoursAsync(
            """
            SELECT
                u.UserID AS Id,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS Label,
                CAST(NULL AS NVARCHAR(200)) AS Detail,
                SUM(te.Hours) AS Hours,
                0 AS Cnt
              FROM dbo.tUserTimeEntry AS te
              INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
             WHERE te.IsActive = 1
               AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND (@UserID IS NULL OR te.UserID = @UserID)
             GROUP BY u.UserID, u.FirstName, u.LastName
             ORDER BY Hours DESC, Label
            """,
            yearStart,
            yearEnd,
            userId,
            cancellationToken);

        var byAnalyst = MergeAnalystHours(byAnalystProject, byAnalystOther);

        var byCategory = await QueryNamedHoursAsync(
            """
            SELECT
                CAST(NULL AS INT) AS Id,
                te.Category AS Label,
                CAST(NULL AS NVARCHAR(200)) AS Detail,
                SUM(te.Hours) AS Hours,
                0 AS Cnt
              FROM dbo.tUserTimeEntry AS te
              INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
             WHERE te.IsActive = 1
               AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND (@UserID IS NULL OR te.UserID = @UserID)
             GROUP BY te.Category
             ORDER BY Hours DESC, Label
            """,
            yearStart,
            yearEnd,
            userId,
            cancellationToken);

        var byClient = await QueryNamedHoursAsync(
            """
            SELECT
                c.ClientID AS Id,
                ISNULL(c.ClientName, N'(No client)') AS Label,
                CAST(NULL AS NVARCHAR(200)) AS Detail,
                SUM(te.Hours) AS Hours,
                COUNT(DISTINCT p.ProjectID) AS Cnt
              FROM dbo.tProjectTimeEntry AS te
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = te.ProjectRefID AND pr.IsActive = 1
              INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
              INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
             WHERE te.IsActive = 1
               AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND (@UserID IS NULL OR te.UserID = @UserID)
             GROUP BY c.ClientID, c.ClientName
             ORDER BY Hours DESC, Label
            """,
            yearStart,
            yearEnd,
            userId,
            cancellationToken);

        var byProject = await QueryNamedHoursAsync(
            """
            SELECT
                p.ProjectID AS Id,
                ISNULL(p.ProjectName, N'(No project)') AS Label,
                ISNULL(c.ClientName, N'') AS Detail,
                SUM(te.Hours) AS Hours,
                0 AS Cnt
              FROM dbo.tProjectTimeEntry AS te
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = te.ProjectRefID AND pr.IsActive = 1
              INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
              INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
             WHERE te.IsActive = 1
               AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND (@UserID IS NULL OR te.UserID = @UserID)
             GROUP BY p.ProjectID, p.ProjectName, c.ClientName
             ORDER BY Hours DESC, Label
            """,
            yearStart,
            yearEnd,
            userId,
            cancellationToken);

        var byTask = await QueryNamedHoursAsync(
            """
            SELECT TOP (40)
                ci.CalendarItemID AS Id,
                ISNULL(ci.Title, N'(No task)') AS Label,
                ISNULL(ci.ItemType, N'Task') AS Detail,
                SUM(te.Hours) AS Hours,
                0 AS Cnt
              FROM dbo.tProjectTimeEntry AS te
              INNER JOIN dbo.tProjectCalendarItem AS ci ON ci.CalendarItemID = te.CalendarItemID
              INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
             WHERE te.IsActive = 1
               AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND (@UserID IS NULL OR te.UserID = @UserID)
             GROUP BY ci.CalendarItemID, ci.Title, ci.ItemType
             ORDER BY Hours DESC, Label
            """,
            yearStart,
            yearEnd,
            userId,
            cancellationToken);

        var byMonth = await QueryMonthHoursAsync(yearStart, yearEnd, userId, cancellationToken);
        var byMonthPrior = await QueryMonthHoursAsync(priorStart, priorEnd, userId, cancellationToken);

        var modeMix = await QueryEntryModeMixAsync(yearStart, yearEnd, userId, cancellationToken);
        var incompleteWeeks = await QueryIncompleteWeeksAsync(yearStart, yearEnd, userId, cancellationToken);

        var kpis = BuildKpis(
            year,
            userId,
            byAnalyst,
            byClient,
            byProject,
            incompleteWeeks,
            modeMix.ExactHours,
            modeMix.AllocatedHours);

        var priorAnalystProject = await QueryNamedHoursAsync(
            """
            SELECT
                u.UserID AS Id,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS Label,
                CAST(NULL AS NVARCHAR(200)) AS Detail,
                SUM(te.Hours) AS Hours,
                COUNT(DISTINCT p.ProjectID) AS Cnt
              FROM dbo.tProjectTimeEntry AS te
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = te.ProjectRefID AND pr.IsActive = 1
              INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
              INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
             WHERE te.IsActive = 1
               AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND (@UserID IS NULL OR te.UserID = @UserID)
             GROUP BY u.UserID, u.FirstName, u.LastName
             ORDER BY Hours DESC, Label
            """,
            priorStart,
            priorEnd,
            userId,
            cancellationToken);
        var priorAnalystOther = await QueryNamedHoursAsync(
            """
            SELECT
                u.UserID AS Id,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS Label,
                CAST(NULL AS NVARCHAR(200)) AS Detail,
                SUM(te.Hours) AS Hours,
                0 AS Cnt
              FROM dbo.tUserTimeEntry AS te
              INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
             WHERE te.IsActive = 1
               AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND (@UserID IS NULL OR te.UserID = @UserID)
             GROUP BY u.UserID, u.FirstName, u.LastName
             ORDER BY Hours DESC, Label
            """,
            priorStart,
            priorEnd,
            userId,
            cancellationToken);
        var priorAnalyst = MergeAnalystHours(priorAnalystProject, priorAnalystOther);
        var priorMode = await QueryEntryModeMixAsync(priorStart, priorEnd, userId, cancellationToken);
        var priorIncomplete = await QueryIncompleteWeeksAsync(priorStart, priorEnd, userId, cancellationToken);
        var priorClients = await QueryNamedHoursAsync(
            """
            SELECT
                c.ClientID AS Id,
                ISNULL(c.ClientName, N'(No client)') AS Label,
                CAST(NULL AS NVARCHAR(200)) AS Detail,
                SUM(te.Hours) AS Hours,
                COUNT(DISTINCT p.ProjectID) AS Cnt
              FROM dbo.tProjectTimeEntry AS te
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = te.ProjectRefID AND pr.IsActive = 1
              INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
              INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
             WHERE te.IsActive = 1
               AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND (@UserID IS NULL OR te.UserID = @UserID)
             GROUP BY c.ClientID, c.ClientName
             ORDER BY Hours DESC, Label
            """,
            priorStart,
            priorEnd,
            userId,
            cancellationToken);
        var priorProjects = await QueryNamedHoursAsync(
            """
            SELECT
                p.ProjectID AS Id,
                ISNULL(p.ProjectName, N'(No project)') AS Label,
                ISNULL(c.ClientName, N'') AS Detail,
                SUM(te.Hours) AS Hours,
                0 AS Cnt
              FROM dbo.tProjectTimeEntry AS te
              INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = te.ProjectRefID AND pr.IsActive = 1
              INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
              INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
             WHERE te.IsActive = 1
               AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
               AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
               AND (@UserID IS NULL OR te.UserID = @UserID)
             GROUP BY p.ProjectID, p.ProjectName, c.ClientName
             ORDER BY Hours DESC, Label
            """,
            priorStart,
            priorEnd,
            userId,
            cancellationToken);

        var priorKpis = BuildKpis(
            priorYear,
            userId,
            priorAnalyst,
            priorClients,
            priorProjects,
            priorIncomplete,
            priorMode.ExactHours,
            priorMode.AllocatedHours);

        return new TmsReportResponse
        {
            Year = year,
            UserID = userId is > 0 ? userId : null,
            PriorYear = priorYear,
            AvailableYears = availableYears,
            CatUsers = catUsers,
            Kpis = kpis,
            PriorYearKpis = priorKpis,
            ByAnalyst = byAnalyst,
            ByCategory = byCategory,
            ByClient = byClient,
            ByProject = byProject,
            ByTask = byTask,
            ByMonth = byMonth,
            ByMonthPriorYear = byMonthPrior,
            IncompleteWeeks = incompleteWeeks,
        };
    }

    private async Task<IReadOnlyList<int>> ListAvailableYearsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT y.Yr
              FROM (
                    SELECT YEAR(te.WorkDate) AS Yr
                      FROM dbo.tProjectTimeEntry AS te
                     WHERE te.IsActive = 1
                    UNION
                    SELECT YEAR(ute.WorkDate) AS Yr
                      FROM dbo.tUserTimeEntry AS ute
                     WHERE ute.IsActive = 1
                   ) AS y
             WHERE y.Yr IS NOT NULL
             ORDER BY y.Yr DESC;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            static r => r.GetInt32OrDefault("Yr"),
            cancellationToken: cancellationToken);
        return rows.Where(y => y >= 2000 && y <= 2100).ToList();
    }

    private Task<IReadOnlyList<TmsNamedHours>> QueryNamedHoursAsync(
        string sql,
        DateTime fromDate,
        DateTime toDate,
        int? userId,
        CancellationToken cancellationToken)
    {
        return _db.QueryTextAsync(
            sql,
            static r => new TmsNamedHours
            {
                Id = r.TryGetNullableInt32("Id"),
                Label = r.TryGetNullableString("Label") ?? string.Empty,
                Detail = r.TryGetNullableString("Detail"),
                Hours = r.GetNullableDecimal("Hours") ?? 0m,
                Count = r.GetInt32OrDefault("Cnt"),
            },
            [
                SqlParameterFactory.Date("@FromDate", fromDate.Date),
                SqlParameterFactory.Date("@ToDate", toDate.Date),
                SqlParameterFactory.Int("@UserID", userId is > 0 ? userId : null),
            ],
            cancellationToken);
    }

    private async Task<IReadOnlyList<TmsMonthHours>> QueryMonthHoursAsync(
        DateTime fromDate,
        DateTime toDate,
        int? userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                CONVERT(char(7), x.WorkDate, 126) AS MonthKey,
                SUM(x.ProjectHours) AS ProjectHours,
                SUM(x.OtherHours) AS OtherHours
              FROM (
                    SELECT te.WorkDate, te.Hours AS ProjectHours, CAST(0 AS DECIMAL(18,2)) AS OtherHours
                      FROM dbo.tProjectTimeEntry AS te
                      INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
                     WHERE te.IsActive = 1
                       AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
                       AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
                       AND (@UserID IS NULL OR te.UserID = @UserID)
                    UNION ALL
                    SELECT ute.WorkDate, CAST(0 AS DECIMAL(18,2)) AS ProjectHours, ute.Hours AS OtherHours
                      FROM dbo.tUserTimeEntry AS ute
                      INNER JOIN dbo.tUser AS u ON u.UserID = ute.UserID AND u.IsActive = 1
                     WHERE ute.IsActive = 1
                       AND ute.WorkDate >= @FromDate AND ute.WorkDate <= @ToDate
                       AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
                       AND (@UserID IS NULL OR ute.UserID = @UserID)
                   ) AS x
             GROUP BY CONVERT(char(7), x.WorkDate, 126)
             ORDER BY MonthKey;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            static r =>
            {
                var key = r.TryGetNullableString("MonthKey") ?? string.Empty;
                var project = r.GetNullableDecimal("ProjectHours") ?? 0m;
                var other = r.GetNullableDecimal("OtherHours") ?? 0m;
                var label = key;
                if (DateTime.TryParse(key + "-01", out var dt))
                {
                    label = dt.ToString("MMM yyyy");
                }
                return new TmsMonthHours
                {
                    MonthKey = key,
                    Label = label,
                    ProjectHours = project,
                    OtherHours = other,
                    TotalHours = project + other,
                };
            },
            [
                SqlParameterFactory.Date("@FromDate", fromDate.Date),
                SqlParameterFactory.Date("@ToDate", toDate.Date),
                SqlParameterFactory.Int("@UserID", userId is > 0 ? userId : null),
            ],
            cancellationToken);

        return rows;
    }

    private async Task<(decimal ExactHours, decimal AllocatedHours)> QueryEntryModeMixAsync(
        DateTime fromDate,
        DateTime toDate,
        int? userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                SUM(CASE WHEN x.EntryMode = 'Allocated' THEN x.Hours ELSE 0 END) AS AllocatedHours,
                SUM(CASE WHEN x.EntryMode = 'Allocated' THEN 0 ELSE x.Hours END) AS ExactHours
              FROM (
                    SELECT te.Hours, te.EntryMode
                      FROM dbo.tProjectTimeEntry AS te
                      INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
                     WHERE te.IsActive = 1
                       AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
                       AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
                       AND (@UserID IS NULL OR te.UserID = @UserID)
                    UNION ALL
                    SELECT ute.Hours, ute.EntryMode
                      FROM dbo.tUserTimeEntry AS ute
                      INNER JOIN dbo.tUser AS u ON u.UserID = ute.UserID AND u.IsActive = 1
                     WHERE ute.IsActive = 1
                       AND ute.WorkDate >= @FromDate AND ute.WorkDate <= @ToDate
                       AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
                       AND (@UserID IS NULL OR ute.UserID = @UserID)
                   ) AS x;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            static r => (
                Exact: r.GetNullableDecimal("ExactHours") ?? 0m,
                Allocated: r.GetNullableDecimal("AllocatedHours") ?? 0m
            ),
            [
                SqlParameterFactory.Date("@FromDate", fromDate.Date),
                SqlParameterFactory.Date("@ToDate", toDate.Date),
                SqlParameterFactory.Int("@UserID", userId is > 0 ? userId : null),
            ],
            cancellationToken);

        return rows.Count > 0 ? rows[0] : (0m, 0m);
    }

    private async Task<IReadOnlyList<TmsIncompleteWeek>> QueryIncompleteWeeksAsync(
        DateTime fromDate,
        DateTime toDate,
        int? userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                x.UserID,
                LTRIM(RTRIM(CONCAT(MAX(x.FirstName), N' ', MAX(x.LastName)))) AS UserFullName,
                x.WorkDate,
                SUM(x.Hours) AS Hours
              FROM (
                    SELECT te.UserID, u.FirstName, u.LastName, te.WorkDate, te.Hours
                      FROM dbo.tProjectTimeEntry AS te
                      INNER JOIN dbo.tUser AS u ON u.UserID = te.UserID AND u.IsActive = 1
                     WHERE te.IsActive = 1
                       AND te.WorkDate >= @FromDate AND te.WorkDate <= @ToDate
                       AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
                       AND (@UserID IS NULL OR te.UserID = @UserID)
                    UNION ALL
                    SELECT ute.UserID, u.FirstName, u.LastName, ute.WorkDate, ute.Hours
                      FROM dbo.tUserTimeEntry AS ute
                      INNER JOIN dbo.tUser AS u ON u.UserID = ute.UserID AND u.IsActive = 1
                     WHERE ute.IsActive = 1
                       AND ute.WorkDate >= @FromDate AND ute.WorkDate <= @ToDate
                       AND UPPER(LTRIM(RTRIM(u.Department))) = N'CAT'
                       AND (@UserID IS NULL OR ute.UserID = @UserID)
                   ) AS x
             GROUP BY x.UserID, x.WorkDate;
            """;

        var days = await _db.QueryTextAsync(
            sql,
            static r => (
                UserID: r.GetInt32OrDefault("UserID"),
                Name: r.TryGetNullableString("UserFullName") ?? string.Empty,
                WorkDate: r.GetDateTimeOrDefault("WorkDate").Date,
                Hours: r.GetNullableDecimal("Hours") ?? 0m
            ),
            [
                SqlParameterFactory.Date("@FromDate", fromDate.Date),
                SqlParameterFactory.Date("@ToDate", toDate.Date),
                SqlParameterFactory.Int("@UserID", userId is > 0 ? userId : null),
            ],
            cancellationToken);

        static DateTime MondayOf(DateTime d)
        {
            var diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return d.AddDays(-diff);
        }

        return days
            .GroupBy(d => (d.UserID, Week: MondayOf(d.WorkDate)))
            .Select(g => new TmsIncompleteWeek
            {
                UserID = g.Key.UserID,
                UserFullName = g.Select(x => x.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                    ?? $"User {g.Key.UserID}",
                WeekStartDate = g.Key.Week,
                Hours = g.Sum(x => x.Hours),
            })
            .Where(w => w.Hours > 0m && w.Hours < 32m)
            .OrderByDescending(w => w.WeekStartDate)
            .ThenBy(w => w.UserFullName)
            .Take(100)
            .ToList();
    }

    private static IReadOnlyList<TmsNamedHours> MergeAnalystHours(
        IReadOnlyList<TmsNamedHours> project,
        IReadOnlyList<TmsNamedHours> other)
    {
        var map = new Dictionary<int, TmsNamedHours>();
        foreach (var row in project)
        {
            var id = row.Id ?? 0;
            if (id <= 0)
            {
                continue;
            }
            map[id] = new TmsNamedHours
            {
                Id = id,
                Label = row.Label,
                ProjectHours = row.Hours,
                OtherHours = 0m,
                Hours = row.Hours,
                Count = row.Count,
            };
        }

        foreach (var row in other)
        {
            var id = row.Id ?? 0;
            if (id <= 0)
            {
                continue;
            }
            if (map.TryGetValue(id, out var existing))
            {
                existing.OtherHours = row.Hours;
                existing.Hours = existing.ProjectHours + existing.OtherHours;
            }
            else
            {
                map[id] = new TmsNamedHours
                {
                    Id = id,
                    Label = row.Label,
                    ProjectHours = 0m,
                    OtherHours = row.Hours,
                    Hours = row.Hours,
                    Count = 0,
                };
            }
        }

        return map.Values.OrderByDescending(x => x.Hours).ThenBy(x => x.Label).ToList();
    }

    private static TmsReportKpis BuildKpis(
        int year,
        int? userId,
        IReadOnlyList<TmsNamedHours> byAnalyst,
        IReadOnlyList<TmsNamedHours> byClient,
        IReadOnlyList<TmsNamedHours> byProject,
        IReadOnlyList<TmsIncompleteWeek> incompleteWeeks,
        decimal exactHours,
        decimal allocatedHours)
    {
        var projectHours = byAnalyst.Sum(a => a.ProjectHours);
        var otherHours = byAnalyst.Sum(a => a.OtherHours);
        var total = projectHours + otherHours;
        var analystCount = userId is > 0 ? 1 : Math.Max(byAnalyst.Count, 0);
        if (analystCount == 0 && userId is > 0)
        {
            analystCount = 1;
        }

        // Capacity: 40h × 52 weeks × analysts (full-year planning capacity).
        var capacity = analystCount * 40m * 52m;
        var utilization = capacity > 0 ? Math.Round(total / capacity * 100m, 1) : 0m;
        var projectShare = total > 0 ? Math.Round(projectHours / total * 100m, 1) : 0m;

        return new TmsReportKpis
        {
            TotalHours = total,
            ProjectHours = projectHours,
            OtherHours = otherHours,
            ProjectSharePct = projectShare,
            AnalystCount = analystCount,
            ProjectCount = byProject.Count,
            ClientCount = byClient.Count,
            CapacityHours = capacity,
            UtilizationPct = utilization,
            IncompleteWeekCount = incompleteWeeks.Count,
            ExactHours = exactHours,
            AllocatedHours = allocatedHours,
        };
    }
}
