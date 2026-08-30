/*
  Permanently delete a client and related rows from the database.

  Guard: refuses when any Active project (tProject.IsActive = 1) is linked through
  tProjectRef -> tClientRef.ClientID. Message tells the user to remove those
  projects first.

  When no active projects remain:
    - hard-deletes any remaining (inactive) projects for this client
    - deletes tClientTag rows
    - deletes tClient (tClientRef cascades via FK_tClientRef_ClientID)
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_HardDelete
    @ClientID     INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientID = @ClientID)
        THROW 50001, 'tClient not found.', 1;

    DECLARE @ActiveProjectCount INT =
    (
        SELECT COUNT(DISTINCT p.ProjectID)
          FROM dbo.tProject AS p
         INNER JOIN dbo.tProjectRef AS pr
            ON pr.ProjectID = p.ProjectID
           AND pr.IsActive = 1
         INNER JOIN dbo.tClientRef AS cr
            ON cr.ClientRefID = pr.ClientRefID
         WHERE cr.ClientID = @ClientID
           AND p.IsActive = 1
    );

    IF @ActiveProjectCount > 0
        THROW 50002, 'This client cannot be deleted because it is associated with active projects. Please remove the project before deleting the client.', 1;

    DECLARE @ClientName VARCHAR(150);
    SELECT @ClientName = ClientName
      FROM dbo.tClient
     WHERE ClientID = @ClientID;

    BEGIN TRY
        BEGIN TRAN;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'Client',
            @EntityID   = @ClientID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove',
            @FieldName  = NULL,
            @OldValue   = @ClientName,
            @NewValue   = NULL;

        DECLARE @Projects TABLE (ProjectID INT NOT NULL PRIMARY KEY);

        INSERT INTO @Projects (ProjectID)
        SELECT DISTINCT p.ProjectID
          FROM dbo.tProject AS p
         INNER JOIN dbo.tProjectRef AS pr
            ON pr.ProjectID = p.ProjectID
         INNER JOIN dbo.tClientRef AS cr
            ON cr.ClientRefID = pr.ClientRefID
         WHERE cr.ClientID = @ClientID;

        IF EXISTS (SELECT 1 FROM @Projects)
        BEGIN
            IF OBJECT_ID(N'dbo.tProjectTimeEntry', N'U') IS NOT NULL
            BEGIN
                DELETE te
                  FROM dbo.tProjectTimeEntry AS te
                 INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = te.ProjectRefID
                 INNER JOIN @Projects AS x ON x.ProjectID = pr.ProjectID;
            END;

            IF OBJECT_ID(N'dbo.tProjectCalendarItem', N'U') IS NOT NULL
            BEGIN
                DELETE ci
                  FROM dbo.tProjectCalendarItem AS ci
                 INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = ci.ProjectRefID
                 INNER JOIN @Projects AS x ON x.ProjectID = pr.ProjectID;
            END;

            IF OBJECT_ID(N'dbo.tProjectFile', N'U') IS NOT NULL
            BEGIN
                DELETE pf
                  FROM dbo.tProjectFile AS pf
                 INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = pf.ProjectRefID
                 INNER JOIN @Projects AS x ON x.ProjectID = pr.ProjectID;
            END;

            IF OBJECT_ID(N'dbo.tProjectTag', N'U') IS NOT NULL
            BEGIN
                DELETE pt
                  FROM dbo.tProjectTag AS pt
                 INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectRefID = pt.ProjectRefID
                 INNER JOIN @Projects AS x ON x.ProjectID = pr.ProjectID;
            END;

            DELETE pr
              FROM dbo.tProjectRef AS pr
             INNER JOIN @Projects AS x ON x.ProjectID = pr.ProjectID;

            DELETE p
              FROM dbo.tProject AS p
             INNER JOIN @Projects AS x ON x.ProjectID = p.ProjectID;
        END;

        IF OBJECT_ID(N'dbo.tClientTag', N'U') IS NOT NULL
        BEGIN
            DELETE FROM dbo.tClientTag
             WHERE ClientID = @ClientID;
        END;

        DELETE FROM dbo.tClient
         WHERE ClientID = @ClientID;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
