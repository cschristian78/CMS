/*
  Non-project time for Time Management (TM).
  Categories: Meetings, Holidays, PTO, Travel, Training, CSI,
  Moodys MAT, Verisk MAT, Peril Advisory, Administrative.
  One active row per (user, category, work date).

  Idempotent: safe to re-run.
*/
SET NOCOUNT ON;
GO

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
END
GO

IF EXISTS (
    SELECT 1
      FROM sys.check_constraints
     WHERE name = N'CK_tUserTimeEntry_Category'
       AND parent_object_id = OBJECT_ID(N'dbo.tUserTimeEntry'))
BEGIN
    ALTER TABLE dbo.tUserTimeEntry DROP CONSTRAINT CK_tUserTimeEntry_Category;
END
GO

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
END
GO

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
END
GO
