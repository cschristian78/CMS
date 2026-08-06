SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE usp_Create_vProjectList
AS
BEGIN
    SET NOCOUNT ON;

    /*
      vProjectList — one row per active project, wide denormalized shape for the home /
      dashboard grid (same contract as the legacy dbo.vProject_Current).

      Client lifecycle status on the Main grid is StatusDescription from
      dbo.tClientRef.StatusID → dbo.tStatus (not tClient.ClientStatus / IsActive).

      Dynamic columns:
        - One column per active dbo.tUserRole.UserRoleName: members of the project''s
          inferred user group (dbo.tUserGroupRef), pivoted like the Phase I proc.
        - One column per catalog service assigned to the project (CategoryName_ServiceName),
          sourced through dbo.tProjectService → dbo.tServiceRef → dbo.tService /
          dbo.tServiceCategory, pivoted on Frequency (or ''Yes'' when blank).

      dbo.vProject_Current is recreated as SELECT * FROM dbo.vProjectList for callers
      that still invoke usp_Create_vProject_Current.
    */

    DECLARE @columns NVARCHAR(MAX);
    DECLARE @pivotColumns NVARCHAR(MAX);
    DECLARE @colServices NVARCHAR(MAX);
    DECLARE @roleSelect NVARCHAR(MAX) = N'';
    DECLARE @roleJoin NVARCHAR(MAX) = N'';
    DECLARE @serviceSelect NVARCHAR(MAX) = N'';
    DECLARE @serviceJoin NVARCHAR(MAX) = N'';
    DECLARE @sql NVARCHAR(MAX);

    SELECT @columns = STRING_AGG(
        'ISNULL(roleUsers.' + QUOTENAME(UserRoleName) + ', ''n/a'') AS ' + QUOTENAME(UserRoleName),
        ',')
    FROM (
        SELECT DISTINCT UserRoleName
        FROM dbo.tUserRole
        WHERE UserRoleName IS NOT NULL
          AND IsActive = 1
    ) r;

    SELECT @pivotColumns = STRING_AGG(QUOTENAME(UserRoleName), ',')
    FROM (
        SELECT DISTINCT UserRoleName
        FROM dbo.tUserRole
        WHERE UserRoleName IS NOT NULL
          AND IsActive = 1
    ) r;

    IF @columns IS NOT NULL AND @pivotColumns IS NOT NULL
    BEGIN
        SET @roleSelect = N',
    ' + @columns;

        SET @roleJoin = N'
LEFT JOIN
(
    SELECT UserGroupID, ' + @pivotColumns + N'
    FROM
    (
        SELECT
            ugr.UserGroupID,
            ur.UserRoleName,
            STRING_AGG(CONCAT(u.FirstName, '' '', u.LastName), ''; '') AS UserNames
        FROM dbo.tUserGroupRef ugr
        INNER JOIN dbo.tUser u ON u.UserID = ugr.UserID AND u.IsActive = 1
        INNER JOIN dbo.tUserRole ur ON ur.UserRoleID = ugr.UserRoleID AND ur.IsActive = 1
        GROUP BY ugr.UserGroupID, ur.UserRoleName
    ) src
    PIVOT
    (
        MAX(UserNames)
        FOR UserRoleName IN (' + @pivotColumns + N')
    ) pvt
) roleUsers ON COALESCE(pref.UserGroupID, projUg.UserGroupID) = roleUsers.UserGroupID';
    END;

    SELECT @colServices = STRING_AGG(CatService, ',')
    FROM (
        SELECT DISTINCT
            QUOTENAME(
                LEFT(
                    REPLACE(REPLACE(REPLACE(CONCAT(sc.CategoryName, '_', s.ServiceName), '/', '_'), ' ', '_'), '-', '_'),
                    128
                )
            ) AS CatService
        FROM dbo.tProjectService ps
        INNER JOIN dbo.tProjectRef pr ON pr.ProjectRefID = ps.ProjectRefID AND pr.IsActive = 1
        INNER JOIN dbo.tServiceRef sr ON sr.ServiceRefID = ps.ServiceRefID AND sr.IsActive = 1
        INNER JOIN dbo.tService s ON s.ServiceID = sr.ServiceID AND s.IsActive = 1
        INNER JOIN dbo.tServiceCategory sc ON sc.ServiceCategoryID = sr.ServiceCategoryID AND sc.IsActive = 1
        WHERE ps.IsActive = 1
          AND sc.ServiceCategoryDescription IS NOT NULL
          AND sc.CategoryName IS NOT NULL
          AND s.ServiceName IS NOT NULL
    ) s;

    IF @colServices IS NOT NULL
    BEGIN
        SELECT @serviceSelect = STRING_AGG('ISNULL(projectServices.' + CatService + ', ''n/a'') AS ' + CatService, ',')
        FROM (
            SELECT DISTINCT
                QUOTENAME(
                    LEFT(
                        REPLACE(REPLACE(REPLACE(CONCAT(sc.CategoryName, '_', s.ServiceName), '/', '_'), ' ', '_'), '-', '_'),
                        128
                    )
                ) AS CatService
            FROM dbo.tProjectService ps
            INNER JOIN dbo.tProjectRef pr ON pr.ProjectRefID = ps.ProjectRefID AND pr.IsActive = 1
            INNER JOIN dbo.tServiceRef sr ON sr.ServiceRefID = ps.ServiceRefID AND sr.IsActive = 1
            INNER JOIN dbo.tService s ON s.ServiceID = sr.ServiceID AND s.IsActive = 1
            INNER JOIN dbo.tServiceCategory sc ON sc.ServiceCategoryID = sr.ServiceCategoryID AND sc.IsActive = 1
            WHERE ps.IsActive = 1
              AND sc.ServiceCategoryDescription IS NOT NULL
              AND sc.CategoryName IS NOT NULL
              AND s.ServiceName IS NOT NULL
        ) s;

        SET @serviceSelect = N',
    ' + @serviceSelect;

        SET @serviceJoin = N'
LEFT JOIN
(
    SELECT ProjectID, ' + @colServices + N'
    FROM
    (
        SELECT
            pr.ProjectID,
            LEFT(REPLACE(REPLACE(REPLACE(CONCAT(sc.CategoryName, ''_'', s.ServiceName), ''/'', ''_''), '' '', ''_''), ''-'', ''_''), 128) AS CatService,
            COALESCE(NULLIF(ps.Frequency, ''''), ''Yes'') AS ServiceValue
        FROM dbo.tProjectService ps
        INNER JOIN dbo.tProjectRef pr ON pr.ProjectRefID = ps.ProjectRefID AND pr.IsActive = 1
        INNER JOIN dbo.tServiceRef sr ON sr.ServiceRefID = ps.ServiceRefID AND sr.IsActive = 1
        INNER JOIN dbo.tService s ON s.ServiceID = sr.ServiceID AND s.IsActive = 1
        INNER JOIN dbo.tServiceCategory sc ON sc.ServiceCategoryID = sr.ServiceCategoryID AND sc.IsActive = 1
        WHERE ps.IsActive = 1
          AND sc.ServiceCategoryDescription IS NOT NULL
          AND sc.CategoryName IS NOT NULL
          AND s.ServiceName IS NOT NULL
    ) src
    PIVOT
    (
        MAX(ServiceValue)
        FOR CatService IN (' + @colServices + N')
    ) pvt
) projectServices ON p.ProjectID = projectServices.ProjectID';
    END;

    SET @sql = N'
CREATE OR ALTER VIEW dbo.vProjectList
AS
SELECT
    bu.BusinessUnitName,
    BusinessClassName = COALESCE(NULLIF(LTRIM(RTRIM(bc.BusinessClassDescription)), N''''), bc.BusinessClassName),
    c.ClientID,
    c.ClientName,
    StatusDescription = CAST(stClient.StatusDescription AS NVARCHAR(50)),
    p.ProjectID,
    p.ProjectNumber,
    p.ProjectName,
    p.IsOnline,
    PTDescription = pt.ProjectTypeDescription,
    p.RenewalDate,
    p.DueDate,
    ProjectStatus = CASE WHEN p.IsActive = 1 THEN CAST(N''Active'' AS NVARCHAR(50)) ELSE CAST(N''Inactive'' AS NVARCHAR(50)) END,
    lr.RevenueID AS LatestRevenueID,
    lr.EffectiveDate AS LatestRevenueEffectiveDate,
    lr.Revenue AS LatestRevenue,
    RevenueTier = CASE
        WHEN ISNULL(lr.Revenue, 0) <= 0 THEN N''Unknown''
        WHEN lr.Revenue <= 250000 THEN N''Less Than $250K''
        WHEN lr.Revenue <= 500000 THEN N''Less Than $500K''
        WHEN lr.Revenue <= 1000000 THEN N''Less Than $1.0M''
        WHEN lr.Revenue <= 2500000 THEN N''Less Than $2.5M''
        WHEN lr.Revenue <= 5000000 THEN N''Less Than $5.0M''
        WHEN lr.Revenue <= 10000000 THEN N''Less Than $10.0M''
        ELSE N''Needs Reviewed''
    END,
    UGID = COALESCE(pref.UserGroupID, projUg.UserGroupID),
    UGName = COALESCE(ugPref.UserGroupName, ug.UserGroupName),
    UserGroupID = COALESCE(pref.UserGroupID, projUg.UserGroupID),
    UserGroupName = COALESCE(ugPref.UserGroupName, ug.UserGroupName),
    pref.ProjectRefID,
    p.CreatedDate,
    p.ModifiedDate,
    p.IsActive'
    + @roleSelect
    + @serviceSelect + N'
FROM dbo.tProject p
OUTER APPLY
(
    SELECT TOP (1)
        pr.ProjectRefID,
        pr.ClientRefID,
        pr.ProjectTypeID,
        pr.UserGroupID
    FROM dbo.tProjectRef pr
    WHERE pr.ProjectID = p.ProjectID
      AND pr.IsActive = 1
    ORDER BY pr.ProjectRefID
) pref
INNER JOIN dbo.tClientRef cr ON cr.ClientRefID = pref.ClientRefID AND cr.IsActive = 1
INNER JOIN dbo.tClient c ON c.ClientID = cr.ClientID
LEFT JOIN dbo.tStatus stClient ON stClient.StatusID = cr.StatusID
INNER JOIN dbo.tBusinessUnit bu ON bu.BusinessUnitID = cr.BusinessUnitID
INNER JOIN dbo.tBusinessClass bc ON bc.BusinessClassID = cr.BusinessClassID
INNER JOIN dbo.tProjectType pt ON pt.ProjectTypeID = pref.ProjectTypeID
LEFT JOIN dbo.tUserGroup ugPref ON ugPref.UserGroupID = pref.UserGroupID
OUTER APPLY
(
    SELECT TOP (1) ugr.UserGroupID
    FROM dbo.tProjectUser pu
    INNER JOIN dbo.tUserGroupRef ugr ON ugr.UserID = pu.UserID
    WHERE pu.ProjectRefID = pref.ProjectRefID
      AND pu.IsActive = 1
    GROUP BY ugr.UserGroupID
    ORDER BY COUNT(*) DESC, ugr.UserGroupID
) projUg
LEFT JOIN dbo.tUserGroup ug ON ug.UserGroupID = projUg.UserGroupID'
    + @roleJoin
    + @serviceJoin + N'
OUTER APPLY
(
    SELECT TOP (1)
        rev.RevenueID,
        rev.EffectiveDate,
        rev.Revenue
    FROM dbo.tProjectRevenue rev
    WHERE rev.ProjectRefID = pref.ProjectRefID
      AND rev.IsActive = 1
    ORDER BY rev.EffectiveDate DESC, rev.RevenueID DESC
) lr
WHERE p.IsActive = 1;';

    EXEC sys.sp_executesql @sql;

    EXEC(N'
CREATE OR ALTER VIEW dbo.vProject_Current
AS
SELECT *
  FROM dbo.vProjectList;
');
END;
GO
CREATE OR ALTER PROCEDURE usp_Create_vProject_Current
AS
BEGIN
    SET NOCOUNT ON;
    EXEC dbo.usp_Create_vProjectList;
END;
GO

--with Refs
GO
EXEC dbo.usp_Create_vProjectList;
GO
SELECT
    cr.ClientRefID,
    cr.ClientID,
    cr.StatusID,
    st.StatusDescription AS RefStatusDescription,
    v.StatusDescription AS ViewStatusDescription,
    v.ClientName,
    v.ProjectID
FROM dbo.tClientRef AS cr
LEFT JOIN dbo.tStatus AS st ON st.StatusID = cr.StatusID
LEFT JOIN dbo.vProject_Current AS v ON v.ClientID = cr.ClientID
WHERE cr.ClientRefID = 41 OR cr.ClientID = 75;
GO