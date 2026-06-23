
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* ============================================================
   Cross-cutting helpers
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_ActivityLog_Insert
    @EntityType VARCHAR(50),
    @EntityID INT,
    @UserID INT,
    @Action VARCHAR(30),
    @FieldName VARCHAR(100) = NULL,
    @OldValue NVARCHAR(MAX) = NULL,
    @NewValue NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.tActivityLog(EntityType, EntityID, UserID, Action, FieldName, OldValue, NewValue)
    VALUES (@EntityType, @EntityID, @UserID, @Action, @FieldName, @OldValue, @NewValue);
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Status_ValidateForEntity
    @StatusID INT,
    @EntityType VARCHAR(30),
    @IsValid BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @IsValid =
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM dbo.tStatus s
            JOIN dbo.tStatusAppliesTo sat ON sat.StatusID = s.StatusID
            WHERE s.StatusID = @StatusID
              AND s.IsActive = 1
              AND (sat.EntityType = @EntityType OR sat.EntityType = 'Universal')
        )
        THEN 1 ELSE 0 END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Status_GetByCode
    @StatusCode VARCHAR(20),
    @StatusTypeID INT,
    @StatusID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT @StatusID = s.StatusID
    FROM dbo.tStatus s
    WHERE s.StatusCode = @StatusCode
      AND s.StatusTypeID = @StatusTypeID
      AND s.IsActive = 1;

    IF @StatusID IS NULL
        THROW 50001, 'Status code not found for supplied StatusTypeID.', 1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Status_ListByEntity
    @EntityType VARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT s.StatusID, s.StatusCode, s.StatusDescription, st.TypeName AS StatusType
    FROM dbo.tStatus s
    JOIN dbo.tStatusType st ON s.StatusTypeID = st.StatusTypeID
    JOIN dbo.tStatusAppliesTo sat ON sat.StatusID = s.StatusID
    WHERE s.IsActive = 1
      AND (sat.EntityType = @EntityType OR sat.EntityType = 'Universal')
    ORDER BY s.StatusCode;
END;
GO

/* ============================================================
   Lookup GetOrCreate + list procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_BusinessUnit_GetOrCreate
    @BUName VARCHAR(100),
    @BUDescription VARCHAR(255) = NULL,
    @ActingUserID INT = NULL,
    @BusinessUnitID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NULLIF(LTRIM(RTRIM(@BUName)), '') IS NULL
        THROW 50002, 'BUName is required.', 1;

    SELECT @BusinessUnitID = BusinessUnitID
    FROM dbo.tBusinessUnit WITH (UPDLOCK, HOLDLOCK)
    WHERE BUName = @BUName;

    IF @BusinessUnitID IS NULL
    BEGIN
        INSERT dbo.tBusinessUnit(BUName, BUDescription)
        VALUES (@BUName, @BUDescription);
        SET @BusinessUnitID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert 'BusinessUnit', @BusinessUnitID, @ActingUserID, 'Insert', NULL, NULL, @BUName;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessUnit_List
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT BusinessUnitID, BUName, BUDescription, IsActive, CreatedDate, ModifiedDate
    FROM dbo.tBusinessUnit
    WHERE @IncludeInactive = 1 OR IsActive = 1
    ORDER BY BUName;
END;
GO


CREATE OR ALTER PROCEDURE dbo.usp_BusinessUnit_Update
    @BusinessUnitID INT,
    @BUName VARCHAR(100) = NULL,
    @BUDescription VARCHAR(255) = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessUnit WHERE BusinessUnitID = @BusinessUnitID)
        THROW 50079, 'BusinessUnit not found.', 1;

    IF @BUName IS NOT NULL AND EXISTS
    (
        SELECT 1
        FROM dbo.tBusinessUnit
        WHERE BUName = @BUName
          AND BusinessUnitID <> @BusinessUnitID
    )
        THROW 50080, 'BusinessUnit name already exists.', 1;

    UPDATE dbo.tBusinessUnit
    SET BUName = COALESCE(@BUName, BUName),
        BUDescription = COALESCE(@BUDescription, BUDescription),
        ModifiedDate = SYSUTCDATETIME()
    WHERE BusinessUnitID = @BusinessUnitID;

    EXEC dbo.usp_ActivityLog_Insert 'BusinessUnit', @BusinessUnitID, @ActingUserID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessUnit_SetActive
    @BusinessUnitID INT,
    @IsActive BIT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    IF @IsActive = 0 AND EXISTS (SELECT 1 FROM dbo.tBusinessClass WHERE BusinessUnitID = @BusinessUnitID)
        THROW 50003, 'Cannot deactivate: BusinessClasses reference this BusinessUnit.', 1;

    UPDATE dbo.tBusinessUnit
    SET IsActive = @IsActive, ModifiedDate = SYSUTCDATETIME()
    WHERE BusinessUnitID = @BusinessUnitID;

    IF @@ROWCOUNT = 0 THROW 50004, 'BusinessUnit not found.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'BusinessUnit', @BusinessUnitID, @ActingUserID, 'SetActive', 'IsActive', NULL, @IsActive;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessClass_GetOrCreate
    @BCName VARCHAR(100),
    @BCDescription VARCHAR(255) = NULL,
    @BusinessUnitID INT,
    @ActingUserID INT = NULL,
    @BusinessClassID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF NULLIF(LTRIM(RTRIM(@BCName)), '') IS NULL
        THROW 50005, 'BCName is required.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tBusinessUnit WHERE BusinessUnitID = @BusinessUnitID AND IsActive = 1)
        THROW 50006, 'BusinessUnit not found or inactive.', 1;

    SELECT @BusinessClassID = BusinessClassID
    FROM dbo.tBusinessClass WITH (UPDLOCK, HOLDLOCK)
    WHERE BusinessUnitID = @BusinessUnitID AND BCName = @BCName;

    IF @BusinessClassID IS NULL
    BEGIN
        INSERT dbo.tBusinessClass(BCName, BCDescription, BusinessUnitID)
        VALUES (@BCName, @BCDescription, @BusinessUnitID);
        SET @BusinessClassID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert 'BusinessClass', @BusinessClassID, @ActingUserID, 'Insert', NULL, NULL, @BCName;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessClass_List
    @BusinessUnitID INT = NULL,
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT bc.BusinessClassID, bc.BCName, bc.BCDescription, bc.BusinessUnitID, bu.BUName, bu.IsActive AS BusinessUnitIsActive
    FROM dbo.tBusinessClass bc
    JOIN dbo.tBusinessUnit bu ON bc.BusinessUnitID = bu.BusinessUnitID
    WHERE (@BusinessUnitID IS NULL OR bc.BusinessUnitID = @BusinessUnitID)
      AND (@IncludeInactive = 1 OR bu.IsActive = 1)
    ORDER BY bu.BUName, bc.BCName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_BusinessClass_Update
    @BusinessClassID INT,
    @BCName VARCHAR(100) = NULL,
    @BCDescription VARCHAR(255) = NULL,
    @BusinessUnitID INT = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @OldBusinessUnitID INT;
    SELECT @OldBusinessUnitID = BusinessUnitID FROM dbo.tBusinessClass WHERE BusinessClassID = @BusinessClassID;
    IF @OldBusinessUnitID IS NULL THROW 50007, 'BusinessClass not found.', 1;

    IF @BusinessUnitID IS NOT NULL AND @BusinessUnitID <> @OldBusinessUnitID
       AND EXISTS (SELECT 1 FROM dbo.tClient WHERE BusinessClassID = @BusinessClassID AND IsDeleted = 0)
        THROW 50008, 'Cannot change BusinessUnitID: active clients reference this BusinessClass.', 1;

    UPDATE dbo.tBusinessClass
    SET BCName = COALESCE(@BCName, BCName),
        BCDescription = COALESCE(@BCDescription, BCDescription),
        BusinessUnitID = COALESCE(@BusinessUnitID, BusinessUnitID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE BusinessClassID = @BusinessClassID;

    EXEC dbo.usp_ActivityLog_Insert 'BusinessClass', @BusinessClassID, @ActingUserID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientCategory_GetOrCreate
    @CCName VARCHAR(100),
    @CCDescription VARCHAR(255) = NULL,
    @ActingUserID INT = NULL,
    @ClientCategoryID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@CCName)), '') IS NULL THROW 50009, 'CCName is required.', 1;

    SELECT @ClientCategoryID = ClientCategoryID
    FROM dbo.tClientCategory WITH (UPDLOCK, HOLDLOCK)
    WHERE CCName = @CCName;

    IF @ClientCategoryID IS NULL
    BEGIN
        INSERT dbo.tClientCategory(CCName, CCDescription) VALUES (@CCName, @CCDescription);
        SET @ClientCategoryID = SCOPE_IDENTITY();
        EXEC dbo.usp_ActivityLog_Insert 'ClientCategory', @ClientCategoryID, @ActingUserID, 'Insert', NULL, NULL, @CCName;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientCategory_List
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ClientCategoryID, CCName, CCDescription, IsActive
    FROM dbo.tClientCategory
    WHERE @IncludeInactive = 1 OR IsActive = 1
    ORDER BY CCName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientType_GetOrCreate
    @CTName VARCHAR(100),
    @CTDescription VARCHAR(255) = NULL,
    @ActingUserID INT = NULL,
    @ClientTypeID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@CTName)), '') IS NULL THROW 50010, 'CTName is required.', 1;

    SELECT @ClientTypeID = ClientTypeID
    FROM dbo.tClientType WITH (UPDLOCK, HOLDLOCK)
    WHERE CTName = @CTName;

    IF @ClientTypeID IS NULL
    BEGIN
        INSERT dbo.tClientType(CTName, CTDescription) VALUES (@CTName, @CTDescription);
        SET @ClientTypeID = SCOPE_IDENTITY();
        EXEC dbo.usp_ActivityLog_Insert 'ClientType', @ClientTypeID, @ActingUserID, 'Insert', NULL, NULL, @CTName;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ClientType_List
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ClientTypeID, CTName, CTDescription, IsActive
    FROM dbo.tClientType
    WHERE @IncludeInactive = 1 OR IsActive = 1
    ORDER BY CTName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectType_GetOrCreate
    @PTCode VARCHAR(20),
    @PTDescription VARCHAR(255) = NULL,
    @ActingUserID INT = NULL,
    @ProjectTypeID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@PTCode)), '') IS NULL THROW 50011, 'PTCode is required.', 1;

    SELECT @ProjectTypeID = ProjectTypeID
    FROM dbo.tProjectType WITH (UPDLOCK, HOLDLOCK)
    WHERE PTCode = @PTCode;

    IF @ProjectTypeID IS NULL
    BEGIN
        INSERT dbo.tProjectType(PTCode, PTDescription) VALUES (@PTCode, @PTDescription);
        SET @ProjectTypeID = SCOPE_IDENTITY();
        EXEC dbo.usp_ActivityLog_Insert 'ProjectType', @ProjectTypeID, @ActingUserID, 'Insert', NULL, NULL, @PTCode;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ProjectType_List
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ProjectTypeID, PTCode, PTDescription, IsActive
    FROM dbo.tProjectType
    WHERE @IncludeInactive = 1 OR IsActive = 1
    ORDER BY PTCode;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ServiceCategory_GetOrCreate
    @CategoryName VARCHAR(100),
    @CatFunction VARCHAR(100) = NULL,
    @ActingUserID INT = NULL,
    @ServiceCategoryID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@CategoryName)), '') IS NULL THROW 50012, 'CategoryName is required.', 1;

    SELECT @ServiceCategoryID = ServiceCategoryID
    FROM dbo.tServiceCategory WITH (UPDLOCK, HOLDLOCK)
    WHERE CategoryName = @CategoryName;

    IF @ServiceCategoryID IS NULL
    BEGIN
        INSERT dbo.tServiceCategory(CategoryName, CatFunction) VALUES (@CategoryName, @CatFunction);
        SET @ServiceCategoryID = SCOPE_IDENTITY();
        EXEC dbo.usp_ActivityLog_Insert 'ServiceCategory', @ServiceCategoryID, @ActingUserID, 'Insert', NULL, NULL, @CategoryName;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ServiceCategory_List
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ServiceCategoryID, CategoryName, CatFunction, IsActive
    FROM dbo.tServiceCategory
    WHERE @IncludeInactive = 1 OR IsActive = 1
    ORDER BY CategoryName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserRole_GetOrCreate
    @UserRoleName VARCHAR(100),
    @RoleDescription VARCHAR(255) = NULL,
    @Tag1 VARCHAR(50) = NULL,
    @Tag2 VARCHAR(50) = NULL,
    @Tag3 VARCHAR(50) = NULL,
    @StatusID INT = NULL,
    @ActingUserID INT = NULL,
    @UserRoleID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@UserRoleName)), '') IS NULL THROW 50013, 'UserRoleName is required.', 1;

    SELECT @UserRoleID = UserRoleID
    FROM dbo.tUserRole WITH (UPDLOCK, HOLDLOCK)
    WHERE UserRoleName = @UserRoleName;

    IF @UserRoleID IS NULL
    BEGIN
        INSERT dbo.tUserRole(UserRoleName, RoleDescription, Tag1, Tag2, Tag3, StatusID)
        VALUES (@UserRoleName, @RoleDescription, @Tag1, @Tag2, @Tag3, @StatusID);
        SET @UserRoleID = SCOPE_IDENTITY();
        EXEC dbo.usp_ActivityLog_Insert 'UserRole', @UserRoleID, @ActingUserID, 'Insert', NULL, NULL, @UserRoleName;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserRole_List
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserRoleID, UserRoleName, RoleDescription, Tag1, Tag2, Tag3, StatusID, IsActive
    FROM dbo.tUserRole
    WHERE @IncludeInactive = 1 OR IsActive = 1
    ORDER BY UserRoleName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Tag_GetOrCreate
    @TagName VARCHAR(50),
    @TagScope VARCHAR(30),
    @ActingUserID INT = NULL,
    @TagID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NULLIF(LTRIM(RTRIM(@TagName)), '') IS NULL THROW 50014, 'TagName is required.', 1;
    IF NULLIF(LTRIM(RTRIM(@TagScope)), '') IS NULL THROW 50015, 'TagScope is required.', 1;

    SELECT @TagID = TagID
    FROM dbo.tTag WITH (UPDLOCK, HOLDLOCK)
    WHERE TagName = @TagName AND TagScope = @TagScope;

    IF @TagID IS NULL
    BEGIN
        INSERT dbo.tTag(TagName, TagScope) VALUES (@TagName, @TagScope);
        SET @TagID = SCOPE_IDENTITY();
        EXEC dbo.usp_ActivityLog_Insert 'Tag', @TagID, @ActingUserID, 'Insert', NULL, NULL, @TagName;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Tag_List
    @TagScope VARCHAR(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TagID, TagName, TagScope, CreatedDate
    FROM dbo.tTag
    WHERE @TagScope IS NULL OR TagScope = @TagScope
    ORDER BY TagScope, TagName;
END;
GO

/* ============================================================
   Client procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_Client_Add
    @BUName VARCHAR(100),
    @BCName VARCHAR(100),
    @CCName VARCHAR(100),
    @CTName VARCHAR(100),
    @ClientName VARCHAR(150),
    @ClientShortName VARCHAR(50) = NULL,
    @StatusCode VARCHAR(20),
    @ActingUserID INT,
    @NewClientID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @BusinessUnitID INT, @BusinessClassID INT, @ClientCategoryID INT, @ClientTypeID INT, @StatusID INT;
    BEGIN TRY
        BEGIN TRAN;

        EXEC dbo.usp_BusinessUnit_GetOrCreate @BUName, NULL, @ActingUserID, @BusinessUnitID OUTPUT;
        EXEC dbo.usp_BusinessClass_GetOrCreate @BCName, NULL, @BusinessUnitID, @ActingUserID, @BusinessClassID OUTPUT;
        EXEC dbo.usp_ClientCategory_GetOrCreate @CCName, NULL, @ActingUserID, @ClientCategoryID OUTPUT;
        EXEC dbo.usp_ClientType_GetOrCreate @CTName, NULL, @ActingUserID, @ClientTypeID OUTPUT;

        SELECT TOP (1) @StatusID = s.StatusID
        FROM dbo.tStatus s
        JOIN dbo.tStatusType st ON s.StatusTypeID = st.StatusTypeID
        WHERE s.StatusCode = @StatusCode AND st.TypeName IN ('Client','Universal') AND s.IsActive = 1
        ORDER BY CASE WHEN st.TypeName = 'Client' THEN 0 ELSE 1 END;

        IF @StatusID IS NULL THROW 50016, 'Client status code not found.', 1;

        IF EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientName = @ClientName AND IsDeleted = 0)
            THROW 50017, 'Client name already exists.', 1;

        INSERT dbo.tClient(ClientName, ClientShortName, BusinessClassID, ClientCategoryID, ClientTypeID, StatusID)
        VALUES (@ClientName, @ClientShortName, @BusinessClassID, @ClientCategoryID, @ClientTypeID, @StatusID);

        SET @NewClientID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert 'Client', @NewClientID, @ActingUserID, 'Insert', NULL, NULL, @ClientName;

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_Get
    @ClientID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM dbo.vClient_WithBU
    WHERE ClientID = @ClientID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_Update
    @ClientID INT,
    @ClientName VARCHAR(150) = NULL,
    @ClientShortName VARCHAR(50) = NULL,
    @BusinessClassID INT = NULL,
    @ClientCategoryID INT = NULL,
    @ClientTypeID INT = NULL,
    @StatusID INT = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientID = @ClientID AND IsDeleted = 0)
        THROW 50018, 'Client not found or deleted.', 1;

    IF @ClientName IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientName = @ClientName AND ClientID <> @ClientID AND IsDeleted = 0)
        THROW 50019, 'Client name already exists.', 1;

    IF @BusinessClassID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.tBusinessClass WHERE BusinessClassID = @BusinessClassID)
        THROW 50020, 'BusinessClass not found.', 1;

    IF @StatusID IS NOT NULL
    BEGIN
        DECLARE @IsValid BIT;
        EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'Client', @IsValid OUTPUT;
        IF @IsValid = 0 THROW 50021, 'Status not valid for Client entity.', 1;
    END

    UPDATE dbo.tClient
    SET ClientName = COALESCE(@ClientName, ClientName),
        ClientShortName = COALESCE(@ClientShortName, ClientShortName),
        BusinessClassID = COALESCE(@BusinessClassID, BusinessClassID),
        ClientCategoryID = COALESCE(@ClientCategoryID, ClientCategoryID),
        ClientTypeID = COALESCE(@ClientTypeID, ClientTypeID),
        StatusID = COALESCE(@StatusID, StatusID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE ClientID = @ClientID;

    EXEC dbo.usp_ActivityLog_Insert 'Client', @ClientID, @ActingUserID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_List
    @BusinessUnitID INT = NULL,
    @ClientCategoryID INT = NULL,
    @ClientTypeID INT = NULL,
    @StatusID INT = NULL,
    @SearchText VARCHAR(150) = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 SET @PageSize = 50;

    SELECT *
    FROM dbo.vClient_WithBU
    WHERE (@BusinessUnitID IS NULL OR BusinessUnitID = @BusinessUnitID)
      AND (@ClientCategoryID IS NULL OR ClientCategoryID = @ClientCategoryID)
      AND (@ClientTypeID IS NULL OR ClientTypeID = @ClientTypeID)
      AND (@StatusID IS NULL OR StatusID = @StatusID)
      AND (@SearchText IS NULL OR ClientName LIKE '%' + @SearchText + '%' OR ClientShortName LIKE '%' + @SearchText + '%')
    ORDER BY ClientName
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_SoftDelete
    @ClientID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.tClient
    SET IsDeleted = 1, DeletedDate = SYSUTCDATETIME(), ModifiedDate = SYSUTCDATETIME()
    WHERE ClientID = @ClientID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50022, 'Client not found or already deleted.', 1;

    EXEC dbo.usp_ActivityLog_Insert 'Client', @ClientID, @ActingUserID, 'SoftDelete', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_Restore
    @ClientID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ClientName VARCHAR(150);
    SELECT @ClientName = ClientName FROM dbo.tClient WHERE ClientID = @ClientID AND IsDeleted = 1;

    IF @ClientName IS NULL THROW 50023, 'Deleted client not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tClient WHERE ClientName = @ClientName AND IsDeleted = 0)
        THROW 50024, 'Cannot restore: active client with same ClientName exists.', 1;

    UPDATE dbo.tClient
    SET IsDeleted = 0, DeletedDate = NULL, ModifiedDate = SYSUTCDATETIME()
    WHERE ClientID = @ClientID;

    EXEC dbo.usp_ActivityLog_Insert 'Client', @ClientID, @ActingUserID, 'Restore', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_GetProjects
    @ClientID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM dbo.vProject_Current
    WHERE ClientID = @ClientID
    ORDER BY DueDate DESC, ProjectNumber;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Client_GetSummary
    @ClientID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        c.ClientID,
        c.ClientName,
        COUNT(DISTINCT p.ProjectID) AS ActiveProjectCount,
        SUM(COALESCE(vc.LatestRevenue, 0)) AS TotalLatestRevenue,
        COUNT(DISTINCT n.NoteID) AS OpenNoteCount,
        MAX(al.ActionDate) AS LastActivityDate
    FROM dbo.vClient c
    LEFT JOIN dbo.vProject_Current vc ON vc.ClientID = c.ClientID
    LEFT JOIN dbo.vProject p ON p.ProjectID = vc.ProjectID
    LEFT JOIN dbo.tProjectNote n ON n.ProjectID = p.ProjectID
    LEFT JOIN dbo.tActivityLog al ON al.EntityType = 'Client' AND al.EntityID = c.ClientID
    WHERE c.ClientID = @ClientID
    GROUP BY c.ClientID, c.ClientName;
END;
GO

/* ============================================================
   Project procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_Project_Add
    @ProjectNumber VARCHAR(50),
    @ProjectName VARCHAR(200),
    @ClientID INT,
    @UGID INT,
    @ProjectTypeID INT,
    @RenewalDate DATE = NULL,
    @DueDate DATE = NULL,
    @StatusID INT,
    @OwnerUserID INT,
    @ActingUserID INT,
    @NewProjectID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM dbo.tProject WHERE ProjectNumber = @ProjectNumber AND IsDeleted = 0)
        THROW 50025, 'Project number already exists.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vClient WHERE ClientID = @ClientID)
        THROW 50026, 'Client not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUserGroup WHERE UGID = @UGID)
        THROW 50027, 'UserGroup not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.tProjectType WHERE ProjectTypeID = @ProjectTypeID AND IsActive = 1)
        THROW 50028, 'ProjectType not found or inactive.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @OwnerUserID)
        THROW 50029, 'Owner user not found or deleted.', 1;

    DECLARE @IsValid BIT;
    EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'Project', @IsValid OUTPUT;
    IF @IsValid = 0 THROW 50030, 'Status not valid for Project entity.', 1;

    INSERT dbo.tProject(ProjectNumber, ProjectName, ClientID, UGID, ProjectTypeID, RenewalDate, DueDate, StatusID, OwnerUserID)
    VALUES (@ProjectNumber, @ProjectName, @ClientID, @UGID, @ProjectTypeID, @RenewalDate, @DueDate, @StatusID, @OwnerUserID);

    SET @NewProjectID = SCOPE_IDENTITY();

    EXEC dbo.usp_ActivityLog_Insert 'Project', @NewProjectID, @ActingUserID, 'Insert', NULL, NULL, @ProjectNumber;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_Get
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        p.*,
        (SELECT COUNT(*) FROM dbo.tProjectService ps WHERE ps.ProjectID = p.ProjectID) AS ServiceCount,
        (SELECT COUNT(*) FROM dbo.tProjectNote pn WHERE pn.ProjectID = p.ProjectID) AS NoteCount
    FROM dbo.vProject_Current p
    WHERE p.ProjectID = @ProjectID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_Update
    @ProjectID INT,
    @ProjectNumber VARCHAR(50) = NULL,
    @ProjectName VARCHAR(200) = NULL,
    @UGID INT = NULL,
    @ProjectTypeID INT = NULL,
    @RenewalDate DATE = NULL,
    @DueDate DATE = NULL,
    @StatusID INT = NULL,
    @OwnerUserID INT = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.vProject WHERE ProjectID = @ProjectID)
        THROW 50031, 'Project not found or deleted.', 1;
    IF @ProjectNumber IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tProject WHERE ProjectNumber = @ProjectNumber AND ProjectID <> @ProjectID AND IsDeleted = 0)
        THROW 50032, 'Project number already exists.', 1;
    IF @StatusID IS NOT NULL
    BEGIN
        DECLARE @IsValid BIT;
        EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'Project', @IsValid OUTPUT;
        IF @IsValid = 0 THROW 50033, 'Status not valid for Project entity.', 1;
    END

    UPDATE dbo.tProject
    SET ProjectNumber = COALESCE(@ProjectNumber, ProjectNumber),
        ProjectName = COALESCE(@ProjectName, ProjectName),
        UGID = COALESCE(@UGID, UGID),
        ProjectTypeID = COALESCE(@ProjectTypeID, ProjectTypeID),
        RenewalDate = COALESCE(@RenewalDate, RenewalDate),
        DueDate = COALESCE(@DueDate, DueDate),
        StatusID = COALESCE(@StatusID, StatusID),
        OwnerUserID = COALESCE(@OwnerUserID, OwnerUserID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE ProjectID = @ProjectID;

    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, @ActingUserID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_List
    @ClientID INT = NULL,
    @UGID INT = NULL,
    @ProjectTypeID INT = NULL,
    @StatusID INT = NULL,
    @OwnerUserID INT = NULL,
    @DueDateFrom DATE = NULL,
    @DueDateTo DATE = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 SET @PageSize = 50;

    SELECT *
    FROM dbo.vProject_Current
    WHERE (@ClientID IS NULL OR ClientID = @ClientID)
      AND (@UGID IS NULL OR UGID = @UGID)
      AND (@ProjectTypeID IS NULL OR ProjectTypeID = @ProjectTypeID)
      AND (@StatusID IS NULL OR StatusID = @StatusID)
      AND (@OwnerUserID IS NULL OR OwnerUserID = @OwnerUserID)
      AND (@DueDateFrom IS NULL OR DueDate >= @DueDateFrom)
      AND (@DueDateTo IS NULL OR DueDate <= @DueDateTo)
    ORDER BY DueDate DESC, ProjectNumber
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_SoftDelete
    @ProjectID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tProject
    SET IsDeleted = 1, DeletedDate = SYSUTCDATETIME(), ModifiedDate = SYSUTCDATETIME()
    WHERE ProjectID = @ProjectID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50034, 'Project not found or already deleted.', 1;

    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, @ActingUserID, 'SoftDelete', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_Restore
    @ProjectID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ProjectNumber VARCHAR(50);
    SELECT @ProjectNumber = ProjectNumber FROM dbo.tProject WHERE ProjectID = @ProjectID AND IsDeleted = 1;

    IF @ProjectNumber IS NULL THROW 50035, 'Deleted project not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tProject WHERE ProjectNumber = @ProjectNumber AND IsDeleted = 0)
        THROW 50036, 'Cannot restore: active project with same ProjectNumber exists.', 1;

    UPDATE dbo.tProject
    SET IsDeleted = 0, DeletedDate = NULL, ModifiedDate = SYSUTCDATETIME()
    WHERE ProjectID = @ProjectID;

    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, @ActingUserID, 'Restore', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_AddRevenue
    @ProjectID INT,
    @EffectiveDate DATE = NULL,
    @Revenue DECIMAL(18,2),
    @ChangeReason VARCHAR(255) = NULL,
    @RecordedBy INT,
    @ActingUserID INT,
    @NewRevenueID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @EffectiveDate IS NULL SET @EffectiveDate = CONVERT(DATE, SYSUTCDATETIME());
    IF NOT EXISTS (SELECT 1 FROM dbo.vProject WHERE ProjectID = @ProjectID)
        THROW 50037, 'Project not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @RecordedBy)
        THROW 50038, 'RecordedBy user not found or deleted.', 1;

    INSERT dbo.tProjectRevenue(ProjectID, EffectiveDate, Revenue, ChangeReason, RecordedBy)
    VALUES (@ProjectID, @EffectiveDate, @Revenue, @ChangeReason, @RecordedBy);

    SET @NewRevenueID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, @ActingUserID, 'AddRevenue', 'Revenue', NULL, @Revenue;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_GetRevenueHistory
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT pr.*, CONCAT(u.FirstName, ' ', u.LastName) AS RecordedByName
    FROM dbo.tProjectRevenue pr
    JOIN dbo.tUser u ON pr.RecordedBy = u.UserID
    WHERE pr.ProjectID = @ProjectID
    ORDER BY pr.EffectiveDate DESC, pr.RevenueID DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_AddService
    @ProjectID INT,
    @ServiceID INT,
    @Frequency VARCHAR(20),
    @StartDate DATE,
    @EndDate DATE = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.vProject WHERE ProjectID = @ProjectID)
        THROW 50039, 'Project not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vService WHERE ServiceID = @ServiceID)
        THROW 50040, 'Service not found or deleted.', 1;

    INSERT dbo.tProjectService(ProjectID, ServiceID, Frequency, StartDate, EndDate)
    VALUES (@ProjectID, @ServiceID, @Frequency, @StartDate, @EndDate);

    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, @ActingUserID, 'AddService', 'ServiceID', NULL, @ServiceID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_RemoveService
    @ProjectID INT,
    @ServiceID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.tProjectService WHERE ProjectID = @ProjectID AND ServiceID = @ServiceID;
    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, @ActingUserID, 'RemoveService', 'ServiceID', @ServiceID, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_AddNote
    @ProjectID INT,
    @UserID INT,
    @NoteType VARCHAR(30),
    @NoteText NVARCHAR(MAX),
    @IsPinned BIT = 0,
    @ActingUserID INT,
    @NewNoteID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.vProject WHERE ProjectID = @ProjectID)
        THROW 50041, 'Project not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @UserID)
        THROW 50042, 'User not found or deleted.', 1;

    INSERT dbo.tProjectNote(ProjectID, UserID, NoteType, NoteText, IsPinned)
    VALUES (@ProjectID, @UserID, @NoteType, @NoteText, @IsPinned);

    SET @NewNoteID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, @ActingUserID, 'AddNote', 'NoteID', NULL, @NewNoteID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_UpdateNote
    @NoteID INT,
    @NoteText NVARCHAR(MAX) = NULL,
    @NoteType VARCHAR(30) = NULL,
    @IsPinned BIT = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tProjectNote
    SET NoteText = COALESCE(@NoteText, NoteText),
        NoteType = COALESCE(@NoteType, NoteType),
        IsPinned = COALESCE(@IsPinned, IsPinned),
        ModifiedDate = SYSUTCDATETIME()
    WHERE NoteID = @NoteID;

    IF @@ROWCOUNT = 0 THROW 50043, 'Note not found.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'ProjectNote', @NoteID, @ActingUserID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_DeleteNote
    @NoteID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ProjectID INT;
    SELECT @ProjectID = ProjectID FROM dbo.tProjectNote WHERE NoteID = @NoteID;
    DELETE FROM dbo.tProjectNote WHERE NoteID = @NoteID;
    EXEC dbo.usp_ActivityLog_Insert 'Project', @ProjectID, @ActingUserID, 'DeleteNote', 'NoteID', @NoteID, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_ListNotes
    @ProjectID INT,
    @NoteType VARCHAR(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT pn.*, CONCAT(u.FirstName, ' ', u.LastName) AS UserName
    FROM dbo.tProjectNote pn
    JOIN dbo.tUser u ON pn.UserID = u.UserID
    WHERE pn.ProjectID = @ProjectID
      AND (@NoteType IS NULL OR pn.NoteType = @NoteType)
    ORDER BY pn.IsPinned DESC, pn.NoteDate DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Project_GetFull
    @ProjectID INT
AS
BEGIN
    SET NOCOUNT ON;

    EXEC dbo.usp_Project_Get @ProjectID;

    SELECT s.ServiceID, s.ServiceName, sc.CategoryName, ps.Frequency, ps.StartDate, ps.EndDate, s.IsDeleted
    FROM dbo.tProjectService ps
    JOIN dbo.tService s ON ps.ServiceID = s.ServiceID
    JOIN dbo.tServiceCategory sc ON s.ServiceCategoryID = sc.ServiceCategoryID
    WHERE ps.ProjectID = @ProjectID
    ORDER BY sc.CategoryName, s.ServiceName;

    SELECT TOP (5) *
    FROM dbo.tProjectNote
    WHERE ProjectID = @ProjectID
    ORDER BY IsPinned DESC, NoteDate DESC;

    SELECT TOP (5) *
    FROM dbo.tActivityLog
    WHERE EntityType = 'Project' AND EntityID = @ProjectID
    ORDER BY ActionDate DESC;
END;
GO

/* ============================================================
   User procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_User_Add
    @FirstName VARCHAR(50),
    @LastName VARCHAR(50),
    @Title VARCHAR(100) = NULL,
    @Email VARCHAR(150),
    @Phone VARCHAR(30) = NULL,
    @Company VARCHAR(150) = NULL,
    @Department VARCHAR(100) = NULL,
    @UserType VARCHAR(20),
    @BusinessUnitID INT = NULL,
    @ClientID INT = NULL,
    @StatusID INT,
    @ActingUserID INT,
    @NewUserID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @UserType NOT IN ('Internal','External')
        THROW 50044, 'UserType must be Internal or External.', 1;
    IF (@UserType = 'Internal' AND (@BusinessUnitID IS NULL OR @ClientID IS NOT NULL))
        THROW 50045, 'Internal users require BusinessUnitID and must not have ClientID.', 1;
    IF (@UserType = 'External' AND (@ClientID IS NULL OR @BusinessUnitID IS NOT NULL))
        THROW 50046, 'External users require ClientID and must not have BusinessUnitID.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tUser WHERE Email = @Email AND IsDeleted = 0)
        THROW 50047, 'Email already exists.', 1;

    DECLARE @IsValid BIT;
    EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'User', @IsValid OUTPUT;
    IF @IsValid = 0 THROW 50048, 'Status not valid for User entity.', 1;

    INSERT dbo.tUser(FirstName, LastName, Title, Email, Phone, Company, Department, UserType, BusinessUnitID, ClientID, StatusID)
    VALUES (@FirstName, @LastName, @Title, @Email, @Phone, @Company, @Department, @UserType, @BusinessUnitID, @ClientID, @StatusID);

    SET @NewUserID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'User', @NewUserID, @ActingUserID, 'Insert', NULL, NULL, @Email;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_Get
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.*,
        bu.BUName,
        c.ClientName,
        s.StatusCode,
        s.StatusDescription
    FROM dbo.vUser u
    LEFT JOIN dbo.tBusinessUnit bu ON u.BusinessUnitID = bu.BusinessUnitID
    LEFT JOIN dbo.tClient c ON u.ClientID = c.ClientID
    JOIN dbo.tStatus s ON u.StatusID = s.StatusID
    WHERE u.UserID = @UserID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_Update
    @UserID INT,
    @FirstName VARCHAR(50) = NULL,
    @LastName VARCHAR(50) = NULL,
    @Title VARCHAR(100) = NULL,
    @Email VARCHAR(150) = NULL,
    @Phone VARCHAR(30) = NULL,
    @Company VARCHAR(150) = NULL,
    @Department VARCHAR(100) = NULL,
    @UserType VARCHAR(20) = NULL,
    @BusinessUnitID INT = NULL,
    @ClientID INT = NULL,
    @StatusID INT = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @UserID)
        THROW 50049, 'User not found or deleted.', 1;
    IF @Email IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tUser WHERE Email = @Email AND UserID <> @UserID AND IsDeleted = 0)
        THROW 50050, 'Email already exists.', 1;

    DECLARE @FinalUserType VARCHAR(20), @FinalBusinessUnitID INT, @FinalClientID INT;
    SELECT
        @FinalUserType = COALESCE(@UserType, UserType),
        @FinalBusinessUnitID = CASE WHEN @UserType = 'External' THEN NULL ELSE COALESCE(@BusinessUnitID, BusinessUnitID) END,
        @FinalClientID = CASE WHEN @UserType = 'Internal' THEN NULL ELSE COALESCE(@ClientID, ClientID) END
    FROM dbo.tUser
    WHERE UserID = @UserID;

    IF @FinalUserType NOT IN ('Internal','External')
        THROW 50051, 'UserType must be Internal or External.', 1;
    IF (@FinalUserType = 'Internal' AND (@FinalBusinessUnitID IS NULL OR @FinalClientID IS NOT NULL))
        THROW 50052, 'Internal users require BusinessUnitID and must not have ClientID.', 1;
    IF (@FinalUserType = 'External' AND (@FinalClientID IS NULL OR @FinalBusinessUnitID IS NOT NULL))
        THROW 50053, 'External users require ClientID and must not have BusinessUnitID.', 1;

    IF @StatusID IS NOT NULL
    BEGIN
        DECLARE @IsValid BIT;
        EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'User', @IsValid OUTPUT;
        IF @IsValid = 0 THROW 50054, 'Status not valid for User entity.', 1;
    END

    UPDATE dbo.tUser
    SET FirstName = COALESCE(@FirstName, FirstName),
        LastName = COALESCE(@LastName, LastName),
        Title = COALESCE(@Title, Title),
        Email = COALESCE(@Email, Email),
        Phone = COALESCE(@Phone, Phone),
        Company = COALESCE(@Company, Company),
        Department = COALESCE(@Department, Department),
        UserType = @FinalUserType,
        BusinessUnitID = @FinalBusinessUnitID,
        ClientID = @FinalClientID,
        StatusID = COALESCE(@StatusID, StatusID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE UserID = @UserID;

    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, @ActingUserID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_List
    @UserType VARCHAR(20) = NULL,
    @BusinessUnitID INT = NULL,
    @ClientID INT = NULL,
    @StatusID INT = NULL,
    @SearchText VARCHAR(150) = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 SET @PageSize = 50;

    SELECT u.*, bu.BUName, c.ClientName, s.StatusCode
    FROM dbo.vUser u
    LEFT JOIN dbo.tBusinessUnit bu ON u.BusinessUnitID = bu.BusinessUnitID
    LEFT JOIN dbo.tClient c ON u.ClientID = c.ClientID
    JOIN dbo.tStatus s ON u.StatusID = s.StatusID
    WHERE (@UserType IS NULL OR u.UserType = @UserType)
      AND (@BusinessUnitID IS NULL OR u.BusinessUnitID = @BusinessUnitID)
      AND (@ClientID IS NULL OR u.ClientID = @ClientID)
      AND (@StatusID IS NULL OR u.StatusID = @StatusID)
      AND (@SearchText IS NULL OR u.FirstName LIKE '%' + @SearchText + '%' OR u.LastName LIKE '%' + @SearchText + '%' OR u.Email LIKE '%' + @SearchText + '%')
    ORDER BY u.LastName, u.FirstName
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_SoftDelete
    @UserID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.vProject WHERE OwnerUserID = @UserID)
        THROW 50055, 'Cannot delete: user owns active projects.', 1;

    UPDATE dbo.tUser
    SET IsDeleted = 1, DeletedDate = SYSUTCDATETIME(), ModifiedDate = SYSUTCDATETIME()
    WHERE UserID = @UserID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50056, 'User not found or already deleted.', 1;

    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, @ActingUserID, 'SoftDelete', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_Restore
    @UserID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Email VARCHAR(150);
    SELECT @Email = Email FROM dbo.tUser WHERE UserID = @UserID AND IsDeleted = 1;
    IF @Email IS NULL THROW 50057, 'Deleted user not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tUser WHERE Email = @Email AND IsDeleted = 0)
        THROW 50058, 'Cannot restore: active user with same Email exists.', 1;

    UPDATE dbo.tUser
    SET IsDeleted = 0, DeletedDate = NULL, ModifiedDate = SYSUTCDATETIME()
    WHERE UserID = @UserID;

    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, @ActingUserID, 'Restore', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_AddAddress
    @UserID INT,
    @AddressType VARCHAR(20),
    @Address1 VARCHAR(200),
    @Address2 VARCHAR(200) = NULL,
    @City VARCHAR(100),
    @State VARCHAR(50) = NULL,
    @PostalCode VARCHAR(20) = NULL,
    @Country VARCHAR(50),
    @ActingUserID INT,
    @NewAddressID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @UserID)
        THROW 50059, 'User not found or deleted.', 1;

    INSERT dbo.tUserAddress(UserID, AddressType, Address1, Address2, City, State, PostalCode, Country)
    VALUES (@UserID, @AddressType, @Address1, @Address2, @City, @State, @PostalCode, @Country);

    SET @NewAddressID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, @ActingUserID, 'AddAddress', 'AddressID', NULL, @NewAddressID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_UpdateAddress
    @AddressID INT,
    @AddressType VARCHAR(20) = NULL,
    @Address1 VARCHAR(200) = NULL,
    @Address2 VARCHAR(200) = NULL,
    @City VARCHAR(100) = NULL,
    @State VARCHAR(50) = NULL,
    @PostalCode VARCHAR(20) = NULL,
    @Country VARCHAR(50) = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserID INT;
    SELECT @UserID = UserID FROM dbo.tUserAddress WHERE AddressID = @AddressID;
    IF @UserID IS NULL THROW 50060, 'Address not found.', 1;

    UPDATE dbo.tUserAddress
    SET AddressType = COALESCE(@AddressType, AddressType),
        Address1 = COALESCE(@Address1, Address1),
        Address2 = COALESCE(@Address2, Address2),
        City = COALESCE(@City, City),
        State = COALESCE(@State, State),
        PostalCode = COALESCE(@PostalCode, PostalCode),
        Country = COALESCE(@Country, Country),
        ModifiedDate = SYSUTCDATETIME()
    WHERE AddressID = @AddressID;

    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, @ActingUserID, 'UpdateAddress', 'AddressID', @AddressID, @AddressID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_DeleteAddress
    @AddressID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserID INT;
    SELECT @UserID = UserID FROM dbo.tUserAddress WHERE AddressID = @AddressID;
    DELETE FROM dbo.tUserAddress WHERE AddressID = @AddressID;
    EXEC dbo.usp_ActivityLog_Insert 'User', @UserID, @ActingUserID, 'DeleteAddress', 'AddressID', @AddressID, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_ListAddresses
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.tUserAddress WHERE UserID = @UserID ORDER BY AddressType, AddressID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_GetWorkload
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ug.*
    FROM dbo.vUserGroup ug
    JOIN dbo.tUserGroupUser ugu ON ug.UGID = ugu.UGID
    WHERE ugu.UserID = @UserID;

    SELECT *
    FROM dbo.vProject_Current
    WHERE OwnerUserID = @UserID
    ORDER BY DueDate;

    SELECT StatusCode, COUNT(*) AS ProjectCount
    FROM dbo.vProject_Current
    WHERE OwnerUserID = @UserID
    GROUP BY StatusCode;
END;
GO

/* ============================================================
   User group procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_Add
    @UserGroupNumber VARCHAR(50),
    @UGName VARCHAR(100),
    @UserRoleID INT = NULL,
    @StatusID INT,
    @ActingUserID INT,
    @NewUGID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.tUserGroup WHERE UserGroupNumber = @UserGroupNumber AND IsDeleted = 0)
        THROW 50061, 'UserGroupNumber already exists.', 1;

    DECLARE @IsValid BIT;
    EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'UserGroup', @IsValid OUTPUT;
    IF @IsValid = 0 THROW 50062, 'Status not valid for UserGroup entity.', 1;

    INSERT dbo.tUserGroup(UserGroupNumber, UGName, UserRoleID, StatusID)
    VALUES (@UserGroupNumber, @UGName, @UserRoleID, @StatusID);

    SET @NewUGID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @NewUGID, @ActingUserID, 'Insert', NULL, NULL, @UserGroupNumber;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_Get
    @UGID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ug.*, ur.UserRoleName, COUNT(ugu.UserID) AS MemberCount
    FROM dbo.vUserGroup ug
    LEFT JOIN dbo.tUserRole ur ON ug.UserRoleID = ur.UserRoleID
    LEFT JOIN dbo.tUserGroupUser ugu ON ug.UGID = ugu.UGID
    WHERE ug.UGID = @UGID
    GROUP BY ug.UGID, ug.UserGroupNumber, ug.UGName, ug.UserRoleID, ug.StatusID, ug.CreatedDate, ug.ModifiedDate, ug.IsDeleted, ug.DeletedDate, ur.UserRoleName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_Update
    @UGID INT,
    @UserGroupNumber VARCHAR(50) = NULL,
    @UGName VARCHAR(100) = NULL,
    @UserRoleID INT = NULL,
    @StatusID INT = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @UserGroupNumber IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tUserGroup WHERE UserGroupNumber = @UserGroupNumber AND UGID <> @UGID AND IsDeleted = 0)
        THROW 50063, 'UserGroupNumber already exists.', 1;

    UPDATE dbo.tUserGroup
    SET UserGroupNumber = COALESCE(@UserGroupNumber, UserGroupNumber),
        UGName = COALESCE(@UGName, UGName),
        UserRoleID = COALESCE(@UserRoleID, UserRoleID),
        StatusID = COALESCE(@StatusID, StatusID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE UGID = @UGID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50064, 'UserGroup not found or deleted.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, @ActingUserID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_List
    @UserRoleID INT = NULL,
    @StatusID INT = NULL,
    @SearchText VARCHAR(100) = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ug.*, ur.UserRoleName
    FROM dbo.vUserGroup ug
    LEFT JOIN dbo.tUserRole ur ON ug.UserRoleID = ur.UserRoleID
    WHERE (@UserRoleID IS NULL OR ug.UserRoleID = @UserRoleID)
      AND (@StatusID IS NULL OR ug.StatusID = @StatusID)
      AND (@SearchText IS NULL OR ug.UGName LIKE '%' + @SearchText + '%' OR ug.UserGroupNumber LIKE '%' + @SearchText + '%')
    ORDER BY ug.UserGroupNumber
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_SoftDelete
    @UGID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.vProject WHERE UGID = @UGID)
        THROW 50065, 'Cannot delete: user group is referenced by active projects.', 1;

    UPDATE dbo.tUserGroup
    SET IsDeleted = 1, DeletedDate = SYSUTCDATETIME(), ModifiedDate = SYSUTCDATETIME()
    WHERE UGID = @UGID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50066, 'UserGroup not found or already deleted.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, @ActingUserID, 'SoftDelete', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_Restore
    @UGID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @UserGroupNumber VARCHAR(50);
    SELECT @UserGroupNumber = UserGroupNumber FROM dbo.tUserGroup WHERE UGID = @UGID AND IsDeleted = 1;
    IF @UserGroupNumber IS NULL THROW 50067, 'Deleted UserGroup not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tUserGroup WHERE UserGroupNumber = @UserGroupNumber AND IsDeleted = 0)
        THROW 50068, 'Cannot restore: active UserGroup with same UserGroupNumber exists.', 1;

    UPDATE dbo.tUserGroup
    SET IsDeleted = 0, DeletedDate = NULL, ModifiedDate = SYSUTCDATETIME()
    WHERE UGID = @UGID;

    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, @ActingUserID, 'Restore', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_AddMember
    @UGID INT,
    @UserID INT,
    @UserRoleID INT = NULL,
    @StatusID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUserGroup WHERE UGID = @UGID)
        THROW 50069, 'UserGroup not found or deleted.', 1;
    IF NOT EXISTS (SELECT 1 FROM dbo.vUser WHERE UserID = @UserID)
        THROW 50070, 'User not found or deleted.', 1;

    INSERT dbo.tUserGroupUser(UGID, UserID, UserRoleID, StatusID)
    VALUES (@UGID, @UserID, @UserRoleID, @StatusID);

    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, @ActingUserID, 'AddMember', 'UserID', NULL, @UserID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_RemoveMember
    @UGID INT,
    @UserID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.tUserGroupUser WHERE UGID = @UGID AND UserID = @UserID;
    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, @ActingUserID, 'RemoveMember', 'UserID', @UserID, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_UpdateMember
    @UGID INT,
    @UserID INT,
    @UserRoleID INT = NULL,
    @StatusID INT = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tUserGroupUser
    SET UserRoleID = COALESCE(@UserRoleID, UserRoleID),
        StatusID = COALESCE(@StatusID, StatusID)
    WHERE UGID = @UGID AND UserID = @UserID;

    IF @@ROWCOUNT = 0 THROW 50071, 'UserGroup member not found.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'UserGroup', @UGID, @ActingUserID, 'UpdateMember', 'UserID', @UserID, @UserID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_UserGroup_ListMembers
    @UGID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        ugu.UGID,
        ugu.UserID,
        u.FirstName,
        u.LastName,
        u.Email,
        ugu.UserRoleID,
        ur.UserRoleName,
        ugu.StatusID,
        s.StatusCode,
        ugu.JoinedDate
    FROM dbo.tUserGroupUser ugu
    JOIN dbo.vUser u ON ugu.UserID = u.UserID
    LEFT JOIN dbo.tUserRole ur ON ugu.UserRoleID = ur.UserRoleID
    JOIN dbo.tStatus s ON ugu.StatusID = s.StatusID
    WHERE ugu.UGID = @UGID
    ORDER BY u.LastName, u.FirstName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_User_ListGroups
    @UserID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ug.*
    FROM dbo.vUserGroup ug
    JOIN dbo.tUserGroupUser ugu ON ug.UGID = ugu.UGID
    WHERE ugu.UserID = @UserID
    ORDER BY ug.UserGroupNumber;
END;
GO

/* ============================================================
   Service procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_Service_Add
    @ServiceName VARCHAR(150),
    @ServiceDescription VARCHAR(255) = NULL,
    @ServiceCategoryID INT,
    @StatusID INT,
    @ActingUserID INT,
    @NewServiceID INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.tService WHERE ServiceName = @ServiceName AND IsDeleted = 0)
        THROW 50072, 'ServiceName already exists.', 1;

    DECLARE @IsValid BIT;
    EXEC dbo.usp_Status_ValidateForEntity @StatusID, 'Service', @IsValid OUTPUT;
    IF @IsValid = 0 THROW 50073, 'Status not valid for Service entity.', 1;

    INSERT dbo.tService(ServiceName, ServiceDescription, ServiceCategoryID, StatusID)
    VALUES (@ServiceName, @ServiceDescription, @ServiceCategoryID, @StatusID);

    SET @NewServiceID = SCOPE_IDENTITY();
    EXEC dbo.usp_ActivityLog_Insert 'Service', @NewServiceID, @ActingUserID, 'Insert', NULL, NULL, @ServiceName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_Get
    @ServiceID INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT s.*, sc.CategoryName, sc.CatFunction, st.StatusCode,
           STRING_AGG(t.TagName, ', ') AS Tags
    FROM dbo.vService s
    JOIN dbo.tServiceCategory sc ON s.ServiceCategoryID = sc.ServiceCategoryID
    JOIN dbo.tStatus st ON s.StatusID = st.StatusID
    LEFT JOIN dbo.tServiceTag stg ON s.ServiceID = stg.ServiceID
    LEFT JOIN dbo.tTag t ON stg.TagID = t.TagID
    WHERE s.ServiceID = @ServiceID
    GROUP BY s.ServiceID, s.ServiceName, s.ServiceDescription, s.ServiceCategoryID, s.StatusID, s.CreatedDate, s.ModifiedDate, s.IsDeleted, s.DeletedDate, sc.CategoryName, sc.CatFunction, st.StatusCode;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_Update
    @ServiceID INT,
    @ServiceName VARCHAR(150) = NULL,
    @ServiceDescription VARCHAR(255) = NULL,
    @ServiceCategoryID INT = NULL,
    @StatusID INT = NULL,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    IF @ServiceName IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.tService WHERE ServiceName = @ServiceName AND ServiceID <> @ServiceID AND IsDeleted = 0)
        THROW 50074, 'ServiceName already exists.', 1;

    UPDATE dbo.tService
    SET ServiceName = COALESCE(@ServiceName, ServiceName),
        ServiceDescription = COALESCE(@ServiceDescription, ServiceDescription),
        ServiceCategoryID = COALESCE(@ServiceCategoryID, ServiceCategoryID),
        StatusID = COALESCE(@StatusID, StatusID),
        ModifiedDate = SYSUTCDATETIME()
    WHERE ServiceID = @ServiceID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50075, 'Service not found or deleted.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'Service', @ServiceID, @ActingUserID, 'Update', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_List
    @ServiceCategoryID INT = NULL,
    @StatusID INT = NULL,
    @TagID INT = NULL,
    @SearchText VARCHAR(150) = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    SELECT DISTINCT s.*, sc.CategoryName, st.StatusCode
    FROM dbo.vService s
    JOIN dbo.tServiceCategory sc ON s.ServiceCategoryID = sc.ServiceCategoryID
    JOIN dbo.tStatus st ON s.StatusID = st.StatusID
    LEFT JOIN dbo.tServiceTag stg ON s.ServiceID = stg.ServiceID
    WHERE (@ServiceCategoryID IS NULL OR s.ServiceCategoryID = @ServiceCategoryID)
      AND (@StatusID IS NULL OR s.StatusID = @StatusID)
      AND (@TagID IS NULL OR stg.TagID = @TagID)
      AND (@SearchText IS NULL OR s.ServiceName LIKE '%' + @SearchText + '%' OR s.ServiceDescription LIKE '%' + @SearchText + '%')
    ORDER BY s.ServiceName
    OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_SoftDelete
    @ServiceID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.tService
    SET IsDeleted = 1, DeletedDate = SYSUTCDATETIME(), ModifiedDate = SYSUTCDATETIME()
    WHERE ServiceID = @ServiceID AND IsDeleted = 0;

    IF @@ROWCOUNT = 0 THROW 50076, 'Service not found or already deleted.', 1;
    EXEC dbo.usp_ActivityLog_Insert 'Service', @ServiceID, @ActingUserID, 'SoftDelete', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_Restore
    @ServiceID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @ServiceName VARCHAR(150);
    SELECT @ServiceName = ServiceName FROM dbo.tService WHERE ServiceID = @ServiceID AND IsDeleted = 1;
    IF @ServiceName IS NULL THROW 50077, 'Deleted Service not found.', 1;
    IF EXISTS (SELECT 1 FROM dbo.tService WHERE ServiceName = @ServiceName AND IsDeleted = 0)
        THROW 50078, 'Cannot restore: active Service with same ServiceName exists.', 1;

    UPDATE dbo.tService
    SET IsDeleted = 0, DeletedDate = NULL, ModifiedDate = SYSUTCDATETIME()
    WHERE ServiceID = @ServiceID;

    EXEC dbo.usp_ActivityLog_Insert 'Service', @ServiceID, @ActingUserID, 'Restore', NULL, NULL, NULL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_AddTag
    @ServiceID INT,
    @TagID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.tServiceTag(ServiceID, TagID) VALUES (@ServiceID, @TagID);
    EXEC dbo.usp_ActivityLog_Insert 'Service', @ServiceID, @ActingUserID, 'AddTag', 'TagID', NULL, @TagID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Service_RemoveTag
    @ServiceID INT,
    @TagID INT,
    @ActingUserID INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.tServiceTag WHERE ServiceID = @ServiceID AND TagID = @TagID;
    EXEC dbo.usp_ActivityLog_Insert 'Service', @ServiceID, @ActingUserID, 'RemoveTag', 'TagID', @TagID, NULL;
END;
GO

/* ============================================================
   Activity log and revenue report procedures
   ============================================================ */

CREATE OR ALTER PROCEDURE dbo.usp_ActivityLog_GetByEntity
    @EntityType VARCHAR(50),
    @EntityID INT,
    @FromDate DATETIME = NULL,
    @ToDate DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT al.*, CONCAT(u.FirstName, ' ', u.LastName) AS UserName
    FROM dbo.tActivityLog al
    LEFT JOIN dbo.tUser u ON al.UserID = u.UserID
    WHERE al.EntityType = @EntityType
      AND al.EntityID = @EntityID
      AND (@FromDate IS NULL OR al.ActionDate >= @FromDate)
      AND (@ToDate IS NULL OR al.ActionDate <= @ToDate)
    ORDER BY al.ActionDate DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_ActivityLog_GetByUser
    @UserID INT,
    @FromDate DATETIME = NULL,
    @ToDate DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT *
    FROM dbo.tActivityLog
    WHERE UserID = @UserID
      AND (@FromDate IS NULL OR ActionDate >= @FromDate)
      AND (@ToDate IS NULL OR ActionDate <= @ToDate)
    ORDER BY ActionDate DESC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Revenue_TotalsByPeriod
    @FromDate DATE,
    @ToDate DATE,
    @ClientID INT = NULL,
    @BusinessUnitID INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH LatestInWindow AS
    (
        SELECT
            p.ProjectID,
            p.ClientID,
            vc.BusinessUnitID,
            pr.Revenue,
            pr.EffectiveDate,
            ROW_NUMBER() OVER (PARTITION BY p.ProjectID ORDER BY pr.EffectiveDate DESC, pr.RevenueID DESC) AS rn
        FROM dbo.vProject p
        JOIN dbo.vClient_WithBU vc ON p.ClientID = vc.ClientID
        JOIN dbo.tProjectRevenue pr ON p.ProjectID = pr.ProjectID
        WHERE pr.EffectiveDate BETWEEN @FromDate AND @ToDate
          AND (@ClientID IS NULL OR p.ClientID = @ClientID)
          AND (@BusinessUnitID IS NULL OR vc.BusinessUnitID = @BusinessUnitID)
    )
    SELECT BusinessUnitID, ClientID, SUM(Revenue) AS TotalRevenue, COUNT(*) AS ProjectCount
    FROM LatestInWindow
    WHERE rn = 1
    GROUP BY BusinessUnitID, ClientID;
END;
GO
