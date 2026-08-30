/*
  Permanently delete a business class from dbo.tBusinessClass.

  Guard: refuses when any Active project (tProject.IsActive = 1) is linked through
  tProjectRef -> tClientRef.BusinessClassID. Message tells the user to remove those
  projects first.

  When no active projects remain, clears remaining ClientRef FK references to this
  BusinessClassID (nullable), then deletes the row.
*/
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessClass_HardDelete
    @BusinessClassID INT,
    @ActingUserID    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessClass WHERE BusinessClassID = @BusinessClassID)
        THROW 50001, 'tBusinessClass not found.', 1;

    DECLARE @ActiveProjectCount INT =
    (
        SELECT COUNT(DISTINCT p.ProjectID)
          FROM dbo.tProject AS p
         INNER JOIN dbo.tProjectRef AS pr
            ON pr.ProjectID = p.ProjectID
           AND pr.IsActive = 1
         INNER JOIN dbo.tClientRef AS cr
            ON cr.ClientRefID = pr.ClientRefID
         WHERE cr.BusinessClassID = @BusinessClassID
           AND p.IsActive = 1
    );

    IF @ActiveProjectCount > 0
        THROW 50002, 'This business class cannot be deleted because it is associated with active projects. Please remove the project before deleting the business class.', 1;

    DECLARE @BusinessClassName VARCHAR(100);
    SELECT @BusinessClassName = BusinessClassName
      FROM dbo.tBusinessClass
     WHERE BusinessClassID = @BusinessClassID;

    BEGIN TRY
        BEGIN TRAN;

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'BusinessClass',
            @EntityID   = @BusinessClassID,
            @UserID     = @ActingUserID,
            @Action     = 'Remove',
            @FieldName  = NULL,
            @OldValue   = @BusinessClassName,
            @NewValue   = NULL;

        UPDATE dbo.tClientRef
           SET BusinessClassID = NULL
         WHERE BusinessClassID = @BusinessClassID;

        DELETE FROM dbo.tBusinessClass
         WHERE BusinessClassID = @BusinessClassID;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
