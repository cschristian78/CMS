/*
  Permanently delete a user group from dbo.tUserGroup.

  Removes membership rows in dbo.tUserGroupRef only (does not delete tUser rows).

  Guard: refuses when any Active project (tProject.IsActive = 1 with active
  tProjectRef) still references this UserGroupID. Message tells the user to
  remove those projects first.

  When no active projects remain, clears remaining ProjectRef.UserGroupID
  references (nullable), deletes membership refs, then deletes the group row.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_HardDelete
    @UserGroupID  INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tUserGroup WHERE UserGroupID = @UserGroupID)
        THROW 50001, 'tUserGroup not found.', 1;

    DECLARE @ActiveProjectCount INT =
    (
        SELECT COUNT(DISTINCT p.ProjectID)
          FROM dbo.tProject AS p
         INNER JOIN dbo.tProjectRef AS pr
            ON pr.ProjectID = p.ProjectID
           AND pr.IsActive = 1
         WHERE pr.UserGroupID = @UserGroupID
           AND p.IsActive = 1
    );

    IF @ActiveProjectCount > 0
        THROW 50002, 'This user group cannot be deleted because it is associated with active projects. Please remove the project before deleting the user group.', 1;

    DECLARE @UserGroupName VARCHAR(100);
    SELECT @UserGroupName = UserGroupName
      FROM dbo.tUserGroup
     WHERE UserGroupID = @UserGroupID;

    BEGIN TRY
        BEGIN TRAN;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'UserGroup',
            @EntityID   = @UserGroupID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove',
            @FieldName  = NULL,
            @OldValue   = @UserGroupName,
            @NewValue   = NULL;

        UPDATE dbo.tProjectRef
           SET UserGroupID = NULL
         WHERE UserGroupID = @UserGroupID;

        DELETE FROM dbo.tUserGroupRef
         WHERE UserGroupID = @UserGroupID;

        DELETE FROM dbo.tUserGroup
         WHERE UserGroupID = @UserGroupID;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
