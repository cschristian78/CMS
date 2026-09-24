using ClientManagementSystem.Models;

namespace ClientManagementSystem.Data;

public interface ICatAiConversationRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatAiConversationSummary>> ListAsync(int userId, CancellationToken cancellationToken = default);

    Task<CatAiConversationDetail?> GetAsync(int conversationId, int userId, CancellationToken cancellationToken = default);

    Task<int> CreateAsync(int userId, string title, CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(int conversationId, int userId, CancellationToken cancellationToken = default);

    Task AppendMessageAsync(
        int conversationId,
        int userId,
        string role,
        string content,
        string? toolName,
        CancellationToken cancellationToken = default);

    Task TouchAsync(int conversationId, int userId, string? titleIfEmpty, CancellationToken cancellationToken = default);

    Task<(bool Active, string? Kind)> GetWizardAsync(int conversationId, int userId, CancellationToken cancellationToken = default);

    Task SetWizardAsync(int conversationId, int userId, string? wizardKind, bool active, CancellationToken cancellationToken = default);
}

public sealed class CatAiConversationRepository : ICatAiConversationRepository
{
    private readonly IDbExecutor _db;
    private static int _schemaEnsured;

    public CatAiConversationRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        // Idempotent DDL — always safe to re-run so additive wizard columns apply after upgrades.
        const string sql = """
            IF OBJECT_ID(N'dbo.tCatAiConversation', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.tCatAiConversation (
                    ConversationID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tCatAiConversation PRIMARY KEY,
                    UserID INT NOT NULL,
                    Title NVARCHAR(200) NOT NULL CONSTRAINT DF_tCatAiConversation_Title DEFAULT (N'New chat'),
                    CreatedUtc DATETIME2(0) NOT NULL CONSTRAINT DF_tCatAiConversation_Created DEFAULT (SYSUTCDATETIME()),
                    ModifiedUtc DATETIME2(0) NOT NULL CONSTRAINT DF_tCatAiConversation_Modified DEFAULT (SYSUTCDATETIME()),
                    IsActive BIT NOT NULL CONSTRAINT DF_tCatAiConversation_IsActive DEFAULT (1),
                    WizardActive BIT NOT NULL CONSTRAINT DF_tCatAiConversation_WizardActive DEFAULT (0),
                    WizardKind NVARCHAR(50) NULL
                );
                CREATE INDEX IX_tCatAiConversation_UserID ON dbo.tCatAiConversation (UserID, IsActive, ModifiedUtc DESC);
            END;

            IF COL_LENGTH(N'dbo.tCatAiConversation', N'WizardActive') IS NULL
            BEGIN
                ALTER TABLE dbo.tCatAiConversation
                    ADD WizardActive BIT NOT NULL
                        CONSTRAINT DF_tCatAiConversation_WizardActive DEFAULT (0);
            END;

            IF COL_LENGTH(N'dbo.tCatAiConversation', N'WizardKind') IS NULL
            BEGIN
                ALTER TABLE dbo.tCatAiConversation ADD WizardKind NVARCHAR(50) NULL;
            END;

            IF OBJECT_ID(N'dbo.tCatAiMessage', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.tCatAiMessage (
                    MessageID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tCatAiMessage PRIMARY KEY,
                    ConversationID INT NOT NULL,
                    Role NVARCHAR(20) NOT NULL,
                    Content NVARCHAR(MAX) NOT NULL,
                    ToolName NVARCHAR(100) NULL,
                    CreatedUtc DATETIME2(0) NOT NULL CONSTRAINT DF_tCatAiMessage_Created DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT FK_tCatAiMessage_Conversation FOREIGN KEY (ConversationID)
                        REFERENCES dbo.tCatAiConversation (ConversationID)
                );
                CREATE INDEX IX_tCatAiMessage_ConversationID ON dbo.tCatAiMessage (ConversationID, MessageID);
            END;
            """;

        try
        {
            await _db.ExecuteTextAsync(sql, cancellationToken: cancellationToken);
            Interlocked.Exchange(ref _schemaEnsured, 1);
        }
        catch
        {
            Interlocked.Exchange(ref _schemaEnsured, 0);
            throw;
        }
    }

    public async Task<IReadOnlyList<CatAiConversationSummary>> ListAsync(int userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            SELECT ConversationID, Title, CreatedUtc, ModifiedUtc,
                   CAST(ISNULL(WizardActive, 0) AS BIT), WizardKind
              FROM dbo.tCatAiConversation
             WHERE UserID = @UserID AND IsActive = 1
             ORDER BY ModifiedUtc DESC, ConversationID DESC;
            """;

        return await _db.QueryTextAsync(
            sql,
            r => new CatAiConversationSummary
            {
                ConversationId = r.GetInt32(0),
                Title = r.IsDBNull(1) ? "New chat" : r.GetString(1),
                CreatedUtc = new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc)),
                ModifiedUtc = new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc)),
                WizardActive = !r.IsDBNull(4) && r.GetBoolean(4),
                WizardKind = r.IsDBNull(5) ? null : r.GetString(5),
            },
            [SqlParameterFactory.Int("@UserID", userId)],
            cancellationToken);
    }

    public async Task<CatAiConversationDetail?> GetAsync(int conversationId, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string headerSql = """
            SELECT ConversationID, Title,
                   CAST(ISNULL(WizardActive, 0) AS BIT), WizardKind
              FROM dbo.tCatAiConversation
             WHERE ConversationID = @ConversationID AND UserID = @UserID AND IsActive = 1;
            """;

        var headers = await _db.QueryTextAsync(
            headerSql,
            r => new
            {
                Id = r.GetInt32(0),
                Title = r.IsDBNull(1) ? "New chat" : r.GetString(1),
                WizardActive = !r.IsDBNull(2) && r.GetBoolean(2),
                WizardKind = r.IsDBNull(3) ? null : r.GetString(3),
            },
            [
                SqlParameterFactory.Int("@ConversationID", conversationId),
                SqlParameterFactory.Int("@UserID", userId),
            ],
            cancellationToken);

        var header = headers.FirstOrDefault();
        if (header is null)
        {
            return null;
        }

        const string msgSql = """
            SELECT MessageID, Role, Content, ToolName, CreatedUtc
              FROM dbo.tCatAiMessage
             WHERE ConversationID = @ConversationID
             ORDER BY MessageID;
            """;

        var messages = await _db.QueryTextAsync(
            msgSql,
            r => new CatAiStoredMessage
            {
                MessageId = r.GetInt32(0),
                Role = r.GetString(1),
                Content = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                ToolName = r.IsDBNull(3) ? null : r.GetString(3),
                CreatedUtc = new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc)),
            },
            [SqlParameterFactory.Int("@ConversationID", conversationId)],
            cancellationToken);

        return new CatAiConversationDetail
        {
            ConversationId = header.Id,
            Title = header.Title,
            WizardActive = header.WizardActive,
            WizardKind = header.WizardKind,
            Messages = messages,
        };
    }

    public async Task<int> CreateAsync(int userId, string title, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            INSERT INTO dbo.tCatAiConversation (UserID, Title)
            OUTPUT INSERTED.ConversationID
            VALUES (@UserID, @Title);
            """;

        var safeTitle = string.IsNullOrWhiteSpace(title) ? "New chat" : title.Trim();
        if (safeTitle.Length > 200)
        {
            safeTitle = safeTitle[..200];
        }

        var ids = await _db.QueryTextAsync(
            sql,
            r => r.GetInt32(0),
            [
                SqlParameterFactory.Int("@UserID", userId),
                SqlParameterFactory.NVarChar("@Title", 200, safeTitle),
            ],
            cancellationToken);

        return ids.FirstOrDefault();
    }

    public async Task SoftDeleteAsync(int conversationId, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.tCatAiConversation
               SET IsActive = 0, ModifiedUtc = SYSUTCDATETIME(),
                   WizardActive = 0, WizardKind = NULL
             WHERE ConversationID = @ConversationID AND UserID = @UserID;
            """;

        await _db.ExecuteTextAsync(
            sql,
            [
                SqlParameterFactory.Int("@ConversationID", conversationId),
                SqlParameterFactory.Int("@UserID", userId),
            ],
            cancellationToken);
    }

    public async Task AppendMessageAsync(
        int conversationId,
        int userId,
        string role,
        string content,
        string? toolName,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string ownedSql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.tCatAiConversation
                 WHERE ConversationID = @ConversationID AND UserID = @UserID AND IsActive = 1
            ) THEN 1 ELSE 0 END AS INT);
            """;

        var owned = await _db.QueryTextAsync(
            ownedSql,
            r => r.GetInt32(0),
            [
                SqlParameterFactory.Int("@ConversationID", conversationId),
                SqlParameterFactory.Int("@UserID", userId),
            ],
            cancellationToken);

        if (owned.FirstOrDefault() != 1)
        {
            throw new InvalidOperationException("Conversation was not found.");
        }

        const string insertSql = """
            INSERT INTO dbo.tCatAiMessage (ConversationID, Role, Content, ToolName)
            VALUES (@ConversationID, @Role, @Content, @ToolName);

            UPDATE dbo.tCatAiConversation
               SET ModifiedUtc = SYSUTCDATETIME()
             WHERE ConversationID = @ConversationID;
            """;

        await _db.ExecuteTextAsync(
            insertSql,
            [
                SqlParameterFactory.Int("@ConversationID", conversationId),
                SqlParameterFactory.NVarChar("@Role", 20, role),
                SqlParameterFactory.NVarCharMax("@Content", content ?? string.Empty),
                SqlParameterFactory.NVarChar("@ToolName", 100, string.IsNullOrWhiteSpace(toolName) ? null : toolName.Trim()),
            ],
            cancellationToken);
    }

    public async Task TouchAsync(int conversationId, int userId, string? titleIfEmpty, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(titleIfEmpty))
        {
            const string sql = """
                UPDATE dbo.tCatAiConversation
                   SET ModifiedUtc = SYSUTCDATETIME()
                 WHERE ConversationID = @ConversationID AND UserID = @UserID AND IsActive = 1;
                """;
            await _db.ExecuteTextAsync(
                sql,
                [
                    SqlParameterFactory.Int("@ConversationID", conversationId),
                    SqlParameterFactory.Int("@UserID", userId),
                ],
                cancellationToken);
            return;
        }

        var title = titleIfEmpty.Trim();
        if (title.Length > 200)
        {
            title = title[..200];
        }

        const string sqlTitle = """
            UPDATE dbo.tCatAiConversation
               SET ModifiedUtc = SYSUTCDATETIME(),
                   Title = CASE WHEN Title = N'New chat' OR Title IS NULL OR LTRIM(RTRIM(Title)) = N'' THEN @Title ELSE Title END
             WHERE ConversationID = @ConversationID AND UserID = @UserID AND IsActive = 1;
            """;

        await _db.ExecuteTextAsync(
            sqlTitle,
            [
                SqlParameterFactory.Int("@ConversationID", conversationId),
                SqlParameterFactory.Int("@UserID", userId),
                SqlParameterFactory.NVarChar("@Title", 200, title),
            ],
            cancellationToken);
    }

    public async Task<(bool Active, string? Kind)> GetWizardAsync(
        int conversationId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            SELECT CAST(ISNULL(WizardActive, 0) AS BIT), WizardKind
              FROM dbo.tCatAiConversation
             WHERE ConversationID = @ConversationID AND UserID = @UserID AND IsActive = 1;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            r => (
                Active: !r.IsDBNull(0) && r.GetBoolean(0),
                Kind: r.IsDBNull(1) ? null : r.GetString(1)
            ),
            [
                SqlParameterFactory.Int("@ConversationID", conversationId),
                SqlParameterFactory.Int("@UserID", userId),
            ],
            cancellationToken);

        return rows.FirstOrDefault();
    }

    public async Task SetWizardAsync(
        int conversationId,
        int userId,
        string? wizardKind,
        bool active,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.tCatAiConversation
               SET WizardActive = @WizardActive,
                   WizardKind = @WizardKind,
                   ModifiedUtc = SYSUTCDATETIME()
             WHERE ConversationID = @ConversationID AND UserID = @UserID AND IsActive = 1;
            """;

        await _db.ExecuteTextAsync(
            sql,
            [
                SqlParameterFactory.Int("@ConversationID", conversationId),
                SqlParameterFactory.Int("@UserID", userId),
                SqlParameterFactory.Bit("@WizardActive", active),
                SqlParameterFactory.NVarChar("@WizardKind", 50, active ? wizardKind : null),
            ],
            cancellationToken);
    }
}
