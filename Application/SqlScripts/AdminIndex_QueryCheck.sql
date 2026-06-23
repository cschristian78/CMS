-- Simulates every DB call in GET /api/admin (AdminService.GetIndexAsync)
SET NOCOUNT ON;
SET XACT_ABORT ON;

PRINT '1 users picklist';
SELECT u.UserID, u.FirstName, u.LastName, u.IsActive, u.CreatedDate, u.ModifiedDate
FROM dbo.tUser u ORDER BY u.LastName;

PRINT '2 user roles';
EXEC dbo.usp_UserRole_List @ActiveOnly = 0;

PRINT '3 user groups (admin repo)';
SELECT ug.UserGroupID, ug.UserGroupNumber, ug.UserGroupName, ug.IsActive
FROM dbo.tUserGroup ug WHERE ug.IsActive = 1;

PRINT '4 user group summaries';
SELECT ug.UserGroupID, ug.UserGroupNumber, ug.UserGroupName,
       (SELECT COUNT(1) FROM dbo.tUserGroupRef ugr WHERE ugr.UserGroupID = ug.UserGroupID) AS MemberCount
FROM dbo.tUserGroup ug WHERE ug.IsActive = 1;

PRINT '5 project types';
EXEC dbo.usp_ProjectType_List;

PRINT '6 business units';
EXEC dbo.usp_BusinessUnit_List @ActiveOnly = 0;

PRINT '7 business classes';
EXEC dbo.usp_BusinessClass_List @ActiveOnly = 0;

PRINT '8 client types';
EXEC dbo.usp_ClientType_List @ActiveOnly = 0;

PRINT '9 service categories';
EXEC dbo.usp_ServiceCategory_List @ActiveOnly = 0;

PRINT '10 tags';
EXEC dbo.usp_Tag_List @ActiveOnly = 0;

PRINT '11 services (admin repo)';
SELECT s.ServiceID, s.ServiceName, s.IsActive, sr.ServiceCategoryID, sc.CategoryName
FROM dbo.tService s
OUTER APPLY (
    SELECT TOP (1) r.ServiceRefID, r.ServiceCategoryID
    FROM dbo.tServiceRef r WHERE r.ServiceID = s.ServiceID AND r.IsActive = 1
    ORDER BY r.ServiceRefID
) sr
LEFT JOIN dbo.tServiceCategory sc ON sc.ServiceCategoryID = sr.ServiceCategoryID
WHERE s.IsActive = 1;

PRINT 'ADMIN INDEX QUERY CHECK OK';
