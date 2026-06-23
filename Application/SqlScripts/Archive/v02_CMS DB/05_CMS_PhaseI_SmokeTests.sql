
/*
CMS Phase I Smoke Test Harness
Run after:
01_CMS_PhaseI_DDL.sql
02_CMS_PhaseI_Views.sql
03_CMS_PhaseI_SeedData.sql
04_CMS_PhaseI_StoredProcedures.sql

This script creates minimal test data. It is wrapped in a transaction and rolls back by default.
Change ROLLBACK to COMMIT when you want to persist sample records.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

DECLARE
    @BU INT,
    @BC INT,
    @CC INT,
    @CT INT,
    @PT INT,
    @SC INT,
    @Role INT,
    @InternalUserID INT,
    @ClientID INT,
    @UGID INT,
    @ProjectID INT,
    @ServiceID INT,
    @RevenueID INT,
    @NoteID INT,
    @ActiveStatusID INT,
    @ProjectStatusID INT;

EXEC dbo.usp_BusinessUnit_GetOrCreate 'Segment 1', 'Primary segment', NULL, @BU OUTPUT;
EXEC dbo.usp_BusinessClass_GetOrCreate 'Property', 'Property business class', @BU, NULL, @BC OUTPUT;
EXEC dbo.usp_ClientCategory_GetOrCreate 'Strategic', 'Strategic account', NULL, @CC OUTPUT;
EXEC dbo.usp_ClientType_GetOrCreate 'Carrier', 'Insurance carrier', NULL, @CT OUTPUT;
EXEC dbo.usp_ProjectType_GetOrCreate 'RNWL', 'Renewal', NULL, @PT OUTPUT;
EXEC dbo.usp_ServiceCategory_GetOrCreate 'Cat Modeling', 'Modeling', NULL, @SC OUTPUT;
EXEC dbo.usp_UserRole_GetOrCreate 'Analytics Lead', 'Analytics owner', NULL, NULL, NULL, NULL, NULL, @Role OUTPUT;

SELECT @ActiveStatusID = StatusID FROM dbo.tStatus WHERE StatusCode = 'A';
SELECT @ProjectStatusID = StatusID FROM dbo.tStatus WHERE StatusCode = 'S';

EXEC dbo.usp_User_Add
    @FirstName='Test',
    @LastName='Owner',
    @Title='Analytics Lead',
    @Email='test.owner@example.com',
    @UserType='Internal',
    @BusinessUnitID=@BU,
    @StatusID=@ActiveStatusID,
    @ActingUserID=NULL,
    @NewUserID=@InternalUserID OUTPUT;

EXEC dbo.usp_UserGroup_Add
    @UserGroupNumber='UG-TEST-001',
    @UGName='Test User Group',
    @UserRoleID=@Role,
    @StatusID=@ActiveStatusID,
    @ActingUserID=@InternalUserID,
    @NewUGID=@UGID OUTPUT;

EXEC dbo.usp_UserGroup_AddMember
    @UGID=@UGID,
    @UserID=@InternalUserID,
    @UserRoleID=@Role,
    @StatusID=@ActiveStatusID,
    @ActingUserID=@InternalUserID;

EXEC dbo.usp_Client_Add
    @BUName='Segment 1',
    @BCName='Property',
    @CCName='Strategic',
    @CTName='Carrier',
    @ClientName='Acme Test Client',
    @ClientShortName='Acme',
    @StatusCode='A',
    @ActingUserID=@InternalUserID,
    @NewClientID=@ClientID OUTPUT;

EXEC dbo.usp_Project_Add
    @ProjectNumber='P-TEST-001',
    @ProjectName='Acme Renewal Cat Model',
    @ClientID=@ClientID,
    @UGID=@UGID,
    @ProjectTypeID=@PT,
    @RenewalDate='2026-01-01',
    @DueDate='2026-02-01',
    @StatusID=@ProjectStatusID,
    @OwnerUserID=@InternalUserID,
    @ActingUserID=@InternalUserID,
    @NewProjectID=@ProjectID OUTPUT;

EXEC dbo.usp_Service_Add
    @ServiceName='RMS Model Run',
    @ServiceDescription='RMS catastrophe modeling run',
    @ServiceCategoryID=@SC,
    @StatusID=@ActiveStatusID,
    @ActingUserID=@InternalUserID,
    @NewServiceID=@ServiceID OUTPUT;

EXEC dbo.usp_Project_AddService
    @ProjectID=@ProjectID,
    @ServiceID=@ServiceID,
    @Frequency='One-time',
    @StartDate='2026-01-05',
    @ActingUserID=@InternalUserID;

EXEC dbo.usp_Project_AddRevenue
    @ProjectID=@ProjectID,
    @EffectiveDate='2026-01-01',
    @Revenue=100000.00,
    @ChangeReason='Initial estimate',
    @RecordedBy=@InternalUserID,
    @ActingUserID=@InternalUserID,
    @NewRevenueID=@RevenueID OUTPUT;

EXEC dbo.usp_Project_AddNote
    @ProjectID=@ProjectID,
    @UserID=@InternalUserID,
    @NoteType='Call',
    @NoteText=N'Discussed renewal terms.',
    @IsPinned=1,
    @ActingUserID=@InternalUserID,
    @NewNoteID=@NoteID OUTPUT;

EXEC dbo.usp_Client_Get @ClientID;
EXEC dbo.usp_Project_GetFull @ProjectID;
EXEC dbo.usp_Client_GetSummary @ClientID;

ROLLBACK;
-- COMMIT;
