CREATE OR ALTER PROCEDURE usp_ProjectRef_Update
    @ProjectRefID                 INT,
    @ProjectID                    INT = NULL,
    @ClientRefID                  INT = NULL,
    @ProjectTypeID                INT = NULL,
    @StatusID                     INT = NULL,
    @UserGroupID                  INT = NULL,
    @ActingUserID                INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectRef WHERE ProjectRefID = @ProjectRefID)
        THROW 50001, 'tProjectRef not found.', 1;

    BEGIN TRY
        BEGIN TRAN;

        UPDATE dbo.tProjectRef
           SET
        ProjectID = COALESCE(@ProjectID, ProjectID),
        ClientRefID = COALESCE(@ClientRefID, ClientRefID),
        ProjectTypeID = COALESCE(@ProjectTypeID, ProjectTypeID),
        StatusID = COALESCE(NULLIF(@StatusID, 0), StatusID),
        -- NULL or <=0 leaves the existing assignment unchanged (do not clear on bind miss).
        UserGroupID = CASE
            WHEN @UserGroupID IS NULL OR @UserGroupID <= 0 THEN UserGroupID
            ELSE @UserGroupID
        END,
        ModifiedDate = SYSUTCDATETIME()
         WHERE ProjectRefID = @ProjectRefID;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'ProjectRef',
            @EntityID   = @ProjectRefID,
            @UserID     = @ActingUserID,
            @Action     = 'Update';

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END
GO

EXEC dbo.usp_Create_vProjectList;
GO

SELECT ProjectID, UGName, UGID, UserGroupID, UserGroupName
  FROM dbo.vProject_Current
 WHERE ProjectID = 32;
GO
