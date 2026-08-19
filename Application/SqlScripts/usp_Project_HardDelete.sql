/*
  Permanent (hard) delete of a project and its child rows.

  SQL impact — live FKs on BMS_CMS (Aug 2026):

    Parent: dbo.tProject
      tProjectRef.ProjectID  -> NO_ACTION   (must delete ProjectRef before Project)

    Parent: dbo.tProjectRef
      tProjectNote           -> CASCADE     (deleted with tProjectRef)
      tProjectRevenue        -> CASCADE
      tProjectService        -> CASCADE
      tProjectUser           -> CASCADE
      tProjectCalendarItem   -> NO_ACTION   (delete first)
      tProjectFile           -> NO_ACTION   (delete first; blobs cleaned in app)
      tProjectTag            -> NO_ACTION   (delete first)
      tProjectTimeEntry      -> NO_ACTION   (delete first)

    Parent: dbo.tProjectCalendarItem
      tProjectTimeEntry.CalendarItemID -> NO_ACTION
        so time entries are deleted before calendar items.

  Not deleted:
    - dbo.tActivityLog rows (no FK to tProject). This proc writes Action='Remove'
      (allowed by CK_tActivityLog_Action). Historical Insert/Update/SoftDelete rows remain.
    - dbo.tClient / tClientRef — clients are not owned by a project.
    - Azure blobs — the app deletes blob paths captured before this proc runs.

  Safe to re-run (CREATE OR ALTER). Does not require IsActive=1 (works after soft-delete).
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_HardDelete
    @ProjectID    INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProject WHERE ProjectID = @ProjectID)
        THROW 50001, 'tProject not found.', 1;

    DECLARE @ProjectNumber VARCHAR(50);
    DECLARE @ProjectName VARCHAR(200);

    SELECT
        @ProjectNumber = ProjectNumber,
        @ProjectName = ProjectName
      FROM dbo.tProject
     WHERE ProjectID = @ProjectID;

    BEGIN TRY
        BEGIN TRAN;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Project',
            @EntityID   = @ProjectID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove',
            @FieldName  = NULL,
            @OldValue   = @ProjectNumber,
            @NewValue   = @ProjectName;

        IF OBJECT_ID(N'dbo.tProjectTimeEntry', N'U') IS NOT NULL
        BEGIN
            DELETE te
              FROM dbo.tProjectTimeEntry AS te
             INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = te.ProjectRefID
             WHERE pr.ProjectID = @ProjectID;
        END;

        IF OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NOT NULL
        BEGIN
            DELETE ci
              FROM dbo.tProjectCalendarItem AS ci
             INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = ci.ProjectRefID
             WHERE pr.ProjectID = @ProjectID;
        END;

        IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
        BEGIN
            DELETE pf
              FROM dbo.tProjectFile AS pf
             INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = pf.ProjectRefID
             WHERE pr.ProjectID = @ProjectID;
        END;

        IF OBJECT_ID(N'dbo.tProjectTag', N'U') IS NOT NULL
        BEGIN
            DELETE pt
              FROM dbo.tProjectTag AS pt
             INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = pt.ProjectRefID
             WHERE pr.ProjectID = @ProjectID;
        END;

        DELETE FROM dbo.tProjectRef
         WHERE ProjectID = @ProjectID;

        DELETE FROM dbo.tProject
         WHERE ProjectID = @ProjectID;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
