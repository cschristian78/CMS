/*
  Permanently delete a business unit from dbo.tBusinessUnit.

  Guard: refuses when any Active project (tProject.IsActive = 1) is linked through
  tProjectRef -> tClientRef.BusinessUnitID. Message tells the user to remove those
  projects first.

  When no active projects remain, clears remaining ClientRef FK references to this
  BusinessUnitID (nullable), optionally clears tBusinessClass.BusinessUnitID when
  that column exists, then deletes the row.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessUnit_HardDelete
    @BusinessUnitID INT,
    @ActingUserID   INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessUnit WHERE BusinessUnitID = @BusinessUnitID)
        THROW 50001, 'tBusinessUnit not found.', 1;

    DECLARE @ActiveProjectCount INT =
    (
        SELECT COUNT(DISTINCT p.ProjectID)
          FROM dbo.tProject AS p
         INNER JOIN dbo.tProjectRef AS pr
            ON pr.ProjectID = p.ProjectID
           AND pr.IsActive = 1
         INNER JOIN dbo.tClientRef AS cr
            ON cr.ClientRefID = pr.ClientRefID
         WHERE cr.BusinessUnitID = @BusinessUnitID
           AND p.IsActive = 1
    );

    IF @ActiveProjectCount > 0
        THROW 50002, 'This business unit cannot be deleted because it is associated with active projects. Please remove the project before deleting the business unit.', 1;

    DECLARE @BusinessUnitName VARCHAR(100);
    SELECT @BusinessUnitName = BusinessUnitName
      FROM dbo.tBusinessUnit
     WHERE BusinessUnitID = @BusinessUnitID;

    BEGIN TRY
        BEGIN TRAN;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessUnit',
            @EntityID   = @BusinessUnitID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove',
            @FieldName  = NULL,
            @OldValue   = @BusinessUnitName,
            @NewValue   = NULL;

        UPDATE dbo.tClientRef
           SET BusinessUnitID = NULL
         WHERE BusinessUnitID = @BusinessUnitID;

        IF COL_LENGTH(N'dbo.tBusinessClass', N'BusinessUnitID') IS NOT NULL
        BEGIN
            DECLARE @clearBcSql nvarchar(400) =
                N'UPDATE dbo.tBusinessClass SET BusinessUnitID = NULL WHERE BusinessUnitID = @BusinessUnitID';
            EXEC sys.sp_executesql
                @clearBcSql,
                N'@BusinessUnitID INT',
                @BusinessUnitID = @BusinessUnitID;
        END;

        DELETE FROM dbo.tBusinessUnit
         WHERE BusinessUnitID = @BusinessUnitID;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
