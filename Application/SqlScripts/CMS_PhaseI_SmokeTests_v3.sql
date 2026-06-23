/*
CMS Phase I v3 Smoke Test Harness
Run against BMS_CMS after deploying CMS_PhaseI_All_v3.sql.

Validates core admin/list SPs and home views. Wrapped in a transaction; rolls back by default.
Set @Persist = 1 to COMMIT sample rows instead.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Persist BIT = 0;
DECLARE @ActingUserID INT = 1;

DECLARE
    @NewBusinessUnitID INT,
    @NewBusinessClassID INT,
    @NewClientTypeID INT,
    @NewProjectTypeID INT,
    @NewServiceCategoryID INT,
    @NewUserRoleID INT,
    @NewUserID INT,
    @NewUserGroupID INT,
    @NewServiceID INT,
    @NewServiceRefID INT,
    @NewAddressID INT,
    @JoinedDate DATETIME2(0) = SYSUTCDATETIME(),
    @Err NVARCHAR(4000);

BEGIN TRY
    BEGIN TRAN;

    IF NOT EXISTS (SELECT 1 FROM dbo.tUser WHERE UserID = @ActingUserID)
    BEGIN
        SET IDENTITY_INSERT dbo.tUser ON;
        INSERT INTO dbo.tUser (UserID, FirstName, LastName, Email, UserType, IsActive)
        VALUES (@ActingUserID, 'System', 'Admin', 'system.admin@example.com', 'Internal', 1);
        SET IDENTITY_INSERT dbo.tUser OFF;
    END;

    EXEC dbo.usp_BusinessUnit_Create
        @BusinessUnitName = 'Smoke Test BU',
        @BusinessUnitDescription = 'Smoke test segment',
        @ActingUserID = @ActingUserID,
        @NewBusinessUnitID = @NewBusinessUnitID OUTPUT;

    EXEC dbo.usp_BusinessClass_Create
        @BusinessClassName = 'Smoke Test Class',
        @BusinessClassDescription = 'Smoke test class',
        @ActingUserID = @ActingUserID,
        @NewBusinessClassID = @NewBusinessClassID OUTPUT;

    EXEC dbo.usp_ClientType_Create
        @ClientTypeName = 'Smoke Carrier',
        @ClientTypeDescription = 'Smoke test client type',
        @ActingUserID = @ActingUserID,
        @NewClientTypeID = @NewClientTypeID OUTPUT;

    EXEC dbo.usp_ProjectType_Create
        @ProjectTypeCode = 'SMK',
        @ProjectTypeDescription = 'Smoke project type',
        @ActingUserID = @ActingUserID,
        @NewProjectTypeID = @NewProjectTypeID OUTPUT;

    EXEC dbo.usp_ServiceCategory_Create
        @CategoryName = 'Smoke Modeling',
        @CatFunction = 'Modeling',
        @ActingUserID = @ActingUserID,
        @NewServiceCategoryID = @NewServiceCategoryID OUTPUT;

    EXEC dbo.usp_UserRole_Create
        @UserRoleName = 'Smoke Analytics Lead',
        @RoleDescription = 'Smoke test role',
        @ActingUserID = @ActingUserID,
        @NewUserRoleID = @NewUserRoleID OUTPUT;

    EXEC dbo.usp_User_Create
        @FirstName = 'Smoke',
        @LastName = 'Tester',
        @Title = 'Analyst',
        @Email = 'smoke.tester@example.com',
        @Phone = NULL,
        @Company = 'BMS',
        @Department = 'CSI',
        @UserType = 'Internal',
        @ActingUserID = @ActingUserID,
        @NewUserID = @NewUserID OUTPUT;

    EXEC dbo.usp_Address_Create
        @Address1 = '123 Smoke St',
        @Address2 = NULL,
        @City = 'Boston',
        @State = 'MA',
        @PostalCode = '02101',
        @Country = 'US',
        @ActingUserID = @ActingUserID,
        @NewAddressID = @NewAddressID OUTPUT;

    EXEC dbo.usp_UserAddressRef_Add
        @UserID = @NewUserID,
        @AddressID = @NewAddressID,
        @AddressType = 'Work',
        @IsPrimary = 1,
        @ActingUserID = @ActingUserID;

    DECLARE @BulkUserID INT, @BulkAddressID INT;
    EXEC dbo.usp_User_CreateWithPrimaryAddress
        @FirstName = 'Bulk',
        @LastName = 'Import',
        @Title = 'Analyst',
        @Email = 'bulk.import@example.com',
        @Phone = NULL,
        @Company = 'BMS',
        @Department = 'CSI',
        @UserType = 'Internal',
        @AddressType = 'Work',
        @Address1 = '456 Bulk Ave',
        @City = 'Boston',
        @State = 'MA',
        @PostalCode = '02102',
        @Country = 'US',
        @ActingUserID = @ActingUserID,
        @NewUserID = @BulkUserID OUTPUT,
        @NewAddressID = @BulkAddressID OUTPUT;

    IF @BulkUserID IS NULL OR @BulkAddressID IS NULL
        THROW 50001, 'usp_User_CreateWithPrimaryAddress did not return IDs', 1;

    EXEC dbo.usp_UserGroup_Create
        @UserGroupNumber = 'UG-SMOKE-001',
        @UserGroupName = 'Smoke User Group',
        @ActingUserID = @ActingUserID,
        @NewUserGroupID = @NewUserGroupID OUTPUT;

    EXEC dbo.usp_UserGroupRef_Add
        @UserGroupID = @NewUserGroupID,
        @UserID = @NewUserID,
        @UserRoleID = @NewUserRoleID,
        @JoinedDate = @JoinedDate,
        @ActingUserID = @ActingUserID;

    EXEC dbo.usp_Service_Create
        @ServiceName = 'Smoke RMS Run',
        @ServiceDescription = 'Smoke test service',
        @ActingUserID = @ActingUserID,
        @NewServiceID = @NewServiceID OUTPUT;

    EXEC dbo.usp_ServiceRef_Create
        @ServiceCategoryID = @NewServiceCategoryID,
        @ServiceGroupID = NULL,
        @ServiceID = @NewServiceID,
        @ActingUserID = @ActingUserID,
        @NewServiceRefID = @NewServiceRefID OUTPUT;

    -- Admin list SPs (read-only checks)
    IF OBJECT_ID('dbo.usp_User_ListWithRefs', 'P') IS NULL THROW 50001, 'Missing usp_User_ListWithRefs', 1;
    IF OBJECT_ID('dbo.usp_Service_ListWithRefs', 'P') IS NULL THROW 50001, 'Missing usp_Service_ListWithRefs', 1;

    EXEC dbo.usp_User_ListWithRefs @ActiveOnly = 0;
    EXEC dbo.usp_UserGroup_List @ActiveOnly = 0;
    EXEC dbo.usp_Service_ListWithRefs @ActiveOnly = 0;
    EXEC dbo.usp_UserRole_ListWithRefs @ActiveOnly = 0;
    EXEC dbo.usp_BusinessUnit_ListWithRefs @ActiveOnly = 0;
    EXEC dbo.usp_BusinessClass_List @ActiveOnly = 0;

    -- Home / project list views
    IF OBJECT_ID('dbo.usp_Create_vProjectList', 'P') IS NOT NULL
        EXEC dbo.usp_Create_vProjectList;

    IF OBJECT_ID('dbo.vProjectList', 'V') IS NULL THROW 50001, 'Missing vProjectList view', 1;
    IF OBJECT_ID('dbo.vProject_Current', 'V') IS NULL THROW 50001, 'Missing vProject_Current view', 1;

    DECLARE @ProjectListRows BIGINT = (SELECT COUNT_BIG(*) FROM dbo.vProjectList);
    DECLARE @HomeTileClients BIGINT = (SELECT COUNT_BIG(*) FROM dbo.v_HomeTile_ActiveClientCount);
    DECLARE @HomeTileProjects BIGINT = (SELECT COUNT_BIG(*) FROM dbo.v_HomeTile_ActiveProjectCount);

    PRINT CONCAT('SMOKE OK: UserID=', @NewUserID, ', UserGroupID=', @NewUserGroupID, ', ServiceID=', @NewServiceID);
    PRINT CONCAT('SMOKE OK: vProjectList rows=', @ProjectListRows, ', home tiles clients=', @HomeTileClients, ', projects=', @HomeTileProjects);

    IF @Persist = 1
        COMMIT TRAN;
    ELSE
        ROLLBACK TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    SET @Err = ERROR_MESSAGE();
    THROW 50001, @Err, 1;
END CATCH;
