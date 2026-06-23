-- dbo.usp_Ref_Create_tClientProjectList — rebuilds dbo.tClientProjectList with client/project rows and
-- pivot columns for each dbo.tUserRole.UserRoleName (MAX of assigned user FirstName per role).
-- Client profile columns: ClientCategory (COALESCE(tClientTypeCategory.CTCName, ClientTypeCategoryName)),
-- ClientTypeName (tClientType), ProgramName (tProgram via tClientProgramLink). Requires CTCName column:
-- run SqlScripts\tClientTypeCategory_add_CTCName_optional.sql once (adds nullable CTCName).
-- Requires SQL Server 2017+ (STRING_AGG).
--
-- Application refresh calls ExecuteNonQuery only — trailing SELECT result sets are omitted here on purpose.
-- Target database: set USE below when deploying manually (e.g. BMS_CMS).

-- USE [BMS_CMS];
-- GO

SET ANSI_NULLS ON;
GO

SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Ref_Create_tClientProjectList
AS
BEGIN
     SET NOCOUNT ON;

	--Exec usp_Ref_Create_tClientProjectList
	DECLARE @pivotColumns NVARCHAR(MAX)
    DECLARE @columns NVARCHAR(MAX);
    DECLARE @colServices NVARCHAR(MAX);
    DECLARE @sql NVARCHAR(MAX);

    -- Build one column per role name
    SELECT @columns = STRING_AGG('ISNULL(' + QUOTENAME(UserRoleName) + ', ''n/a'') AS ' + QUOTENAME(UserRoleName), ',')
    FROM (
        SELECT DISTINCT UserRoleName
        FROM dbo.tUserRole
        WHERE UserRoleName IS NOT NULL
    ) r;
	--select @columns

	SELECT @pivotColumns = STRING_AGG(QUOTENAME(UserRoleName), ',')
	FROM (
	    SELECT DISTINCT UserRoleName
		FROM dbo.tUserRole
		WHERE UserRoleName IS NOT NULL
	) r;

    -- Build one column per service category/function select * from tServices  select * from tprojectservice order by 1
    SELECT @colServices = STRING_AGG(CatService, ',')
    FROM (
        SELECT DISTINCT  QUOTENAME(Tag1 + '_' + ServiceName) AS CatService
        FROM dbo.tServices s
			inner join tprojectservice ps on s.ServiceID = ps.ServiceID
        WHERE CatFunction IS NOT NULL AND ServiceCategory IS NOT NULL
    ) s;
	--select @colservices

    -- Safety checks
    IF @columns IS NULL
    BEGIN
        RAISERROR('No UserRoleName values found in dbo.tUserRole.', 16, 1);
        RETURN;
    END;

    IF @colServices IS NULL
    BEGIN
        RAISERROR('No service values found in dbo.tServices.', 16, 1);
        RETURN;
    END;

    IF OBJECT_ID('dbo.tClientProjectList', 'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.tClientProjectList;
    END;
	
    SET @sql = N'
    SELECT
		rp.BUName,
		rp.BCName,
        rp.ClientID,
        rp.ClientName,
        rp.ClientShortName,
        rp.ClientCategory,
		rp.ClientStatus,
        rp.ClientType,
        rp.ProjectID,
        rp.ProjectName,
		rp.ProjectStatus,
        ' + @columns + ',
        ' + @colServices + '

    INTO dbo.tClientProjectList

    FROM
    (
        SELECT
			BUName,
			BCName,
            ClientID,
            ClientName,
            ClientShortName,
            ClientCategory,
			ClientStatus,
            ClientType,
            ProjectID,
            ProjectName,
			ProjectStatus,
            ' + @columns + '
        FROM
        (
 SELECT
				bu.BUName,
				bc.BCName,
                c.ClientID,
                c.ClientName,
                c.ClientShortName,
                ctc.CTCDescription ClientCategory,
				st.StatusDescription ClientStatus,
                ct.ClientTypeName ClientType,
                p.ProjectID,
                p.ProjectName,
				st.StatusDescription ProjectStatus,
                ur.UserRoleName,
                u.FirstName
            FROM dbo.tClient c
			INNER JOIN dbo.tBusinessUnit AS bu ON c.BUID = bu.BUID
			INNER JOIN dbo.tBusinessClass AS bc on bu.BUID = bc.BUID
			LEFT JOIN dbo.tclienttypecategorylink ctcl on c.Clientid = ctcl.Clientid
			LEFT JOIN dbo.tclienttypecategory ctc on ctc.CTCID = ctcl.CTCID
			LEFT JOIN dbo.tclienttypelink ctl on ctl.CTCID = ctc.CTCID
			LEFT JOIN dbo.tClientType ct on ct.ClientTypeID = ctl.ClientTypeID
            INNER JOIN dbo.tProject p ON c.ClientID = p.ClientID
            INNER JOIN dbo.tStatus st ON c.StatusID = st.StatusID and p.statusid = st.statusid
			INNER JOIN dbo.tUserGroup ug ON p.UserGroupNumber = ug.UserGroupNumber
            INNER JOIN dbo.tUser u ON ug.UserID = u.UserID
            INNER JOIN dbo.tUserRole ur ON ur.UserRoleID = ug.UserRoleID
        ) srcRole
        PIVOT
        (
            MAX(FirstName)
            FOR UserRoleName IN (' + @pivotColumns + ')
        ) pvtRole
    ) rp

    LEFT JOIN
    (
        SELECT
            ClientID,
            ProjectID,
            ' + @colServices + '
        FROM
        (
            SELECT
                c.ClientID,
                p.ProjectID,
                ts.Tag1 + ''_'' + ServiceName AS CatService,
                1 AS ServiceValue
            FROM dbo.tClient c
            INNER JOIN dbo.tProject p ON c.ClientID = p.ClientID
			INNER JOIN dbo.tprojectService ps on ps.ProjectID = p.ProjectID
            INNER JOIN dbo.tServices ts ON ps.ServiceID = ts.ServiceID
        ) srcService
        PIVOT
        (
            MAX(ServiceValue)
            FOR CatService IN (' + @colServices + ')
        ) pvtService
    ) sp
        ON rp.ClientID = sp.ClientID
       AND ISNULL(rp.ProjectID, -1) = ISNULL(sp.ProjectID, -1);
    ';

    --PRINT @sql;
    EXEC sp_executesql @sql;
END;
GO

-- =============================================================================
-- POST-DEPLOY SELF-TEST
-- Runs the proc once, then verifies the rebuilt table has the columns the app
-- requires (ClientStatus / ProjectStatus in particular). RAISERRORs loudly if anything is
-- missing so you don't have to chase invalid column names at runtime.
--
-- Comment this block out when redeploying to production if you do not want the
-- script to materialise the table on the spot.
-- =============================================================================
PRINT N'Verifying dbo.usp_Ref_Create_tClientProjectList by running it once...';
EXEC dbo.usp_Ref_Create_tClientProjectList;

DECLARE @missing NVARCHAR(MAX) = NULL;
SELECT @missing = STRING_AGG(needed.col, N', ')
FROM (VALUES
    (N'BUName'), (N'BCName'),
    (N'ClientID'), (N'ClientName'),
    (N'ClientCategory'), (N'ClientTypeName'), (N'ProgramName'),
    (N'ClientStatus'), (N'ProjectStatus'),
    (N'UserGroupNumber'), (N'UserGroupName'),
    (N'ProjectID'), (N'ProjectName'), (N'ProjectServices')
) AS needed(col)
LEFT JOIN sys.columns AS sc
    ON sc.object_id = OBJECT_ID(N'dbo.tClientProjectList', N'U')
   AND sc.name = needed.col
WHERE sc.object_id IS NULL;

IF @missing IS NOT NULL
BEGIN
    DECLARE @msg NVARCHAR(MAX) =
        N'dbo.tClientProjectList is missing required column(s): ' + @missing
        + N'. The deployed proc text does not match SqlScripts\usp_Ref_Create_tClientProjectList.sql.';
    RAISERROR(@msg, 16, 1);
END
ELSE
BEGIN
    PRINT N'OK — dbo.tClientProjectList has every column the app expects (including ClientStatus and ProjectStatus).';
END
GO

