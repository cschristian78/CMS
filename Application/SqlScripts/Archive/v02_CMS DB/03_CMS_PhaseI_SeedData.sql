
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/* Seed data from the Seed Data workbook tab. */

SET IDENTITY_INSERT dbo.tStatusType ON;
MERGE dbo.tStatusType AS tgt
USING (VALUES
    (1, 'Universal', 1),
    (2, 'Client', 1),
    (3, 'Project', 1),
    (4, 'UserGroup', 1),
    (5, 'User', 1),
    (6, 'Service', 1)
) AS src(StatusTypeID, TypeName, IsActive)
ON tgt.StatusTypeID = src.StatusTypeID
WHEN MATCHED THEN UPDATE SET TypeName = src.TypeName, IsActive = src.IsActive
WHEN NOT MATCHED THEN INSERT (StatusTypeID, TypeName, IsActive) VALUES (src.StatusTypeID, src.TypeName, src.IsActive);
SET IDENTITY_INSERT dbo.tStatusType OFF;
GO

SET IDENTITY_INSERT dbo.tStatus ON;
MERGE dbo.tStatus AS tgt
USING (VALUES
    (1, 1, 'A', 'Active', 1),
    (2, 1, 'I', 'Inactive', 1),
    (3, 2, 'P', 'Prospect', 1),
    (4, 3, 'S', 'Started', 1),
    (5, 3, 'C', 'Complete', 1),
    (6, 1, 'U', 'Unknown', 1)
) AS src(StatusID, StatusTypeID, StatusCode, StatusDescription, IsActive)
ON tgt.StatusID = src.StatusID
WHEN MATCHED THEN UPDATE SET
    StatusTypeID = src.StatusTypeID,
    StatusCode = src.StatusCode,
    StatusDescription = src.StatusDescription,
    IsActive = src.IsActive
WHEN NOT MATCHED THEN
    INSERT (StatusID, StatusTypeID, StatusCode, StatusDescription, IsActive)
    VALUES (src.StatusID, src.StatusTypeID, src.StatusCode, src.StatusDescription, src.IsActive);
SET IDENTITY_INSERT dbo.tStatus OFF;
GO

MERGE dbo.tStatusAppliesTo AS tgt
USING (VALUES
    (1, 'Universal', 'All entities'),
    (2, 'Universal', 'All entities'),
    (6, 'Universal', 'All entities fallback'),
    (3, 'Client', 'Client-only'),
    (4, 'Project', 'Project-only'),
    (5, 'Project', 'Project-only')
) AS src(StatusID, EntityType, Notes)
ON tgt.StatusID = src.StatusID AND tgt.EntityType = src.EntityType
WHEN MATCHED THEN UPDATE SET Notes = src.Notes
WHEN NOT MATCHED THEN INSERT (StatusID, EntityType, Notes) VALUES (src.StatusID, src.EntityType, src.Notes);
GO

/* Minimum working lookup set for Phase I tests. */
MERGE dbo.tBusinessUnit AS tgt
USING (VALUES
    ('Segment 1', 'Primary internal segment'),
    ('Segment 2', 'Secondary internal segment'),
    ('Segment 3', 'Tertiary internal segment')
) AS src(BUName, BUDescription)
ON tgt.BUName = src.BUName
WHEN MATCHED THEN UPDATE SET BUDescription = src.BUDescription, IsActive = 1
WHEN NOT MATCHED THEN INSERT (BUName, BUDescription, IsActive) VALUES (src.BUName, src.BUDescription, 1);
GO

MERGE dbo.tClientCategory AS tgt
USING (VALUES
    ('Prospect', 'Prospective client'),
    ('Active', 'Active client'),
    ('Strategic', 'Strategic account')
) AS src(CCName, CCDescription)
ON tgt.CCName = src.CCName
WHEN MATCHED THEN UPDATE SET CCDescription = src.CCDescription, IsActive = 1
WHEN NOT MATCHED THEN INSERT (CCName, CCDescription, IsActive) VALUES (src.CCName, src.CCDescription, 1);
GO

MERGE dbo.tClientType AS tgt
USING (VALUES
    ('Carrier', 'Insurance carrier'),
    ('Broker', 'Broker or intermediary'),
    ('Reinsurer', 'Reinsurance entity'),
    ('Corporate', 'Corporate client')
) AS src(CTName, CTDescription)
ON tgt.CTName = src.CTName
WHEN MATCHED THEN UPDATE SET CTDescription = src.CTDescription, IsActive = 1
WHEN NOT MATCHED THEN INSERT (CTName, CTDescription, IsActive) VALUES (src.CTName, src.CTDescription, 1);
GO

MERGE dbo.tProjectType AS tgt
USING (VALUES
    ('RNWL', 'Renewal'),
    ('NB', 'New business'),
    ('ADHOC', 'Ad hoc analysis'),
    ('ROLLUP', 'Portfolio rollup')
) AS src(PTCode, PTDescription)
ON tgt.PTCode = src.PTCode
WHEN MATCHED THEN UPDATE SET PTDescription = src.PTDescription, IsActive = 1
WHEN NOT MATCHED THEN INSERT (PTCode, PTDescription, IsActive) VALUES (src.PTCode, src.PTDescription, 1);
GO

MERGE dbo.tServiceCategory AS tgt
USING (VALUES
    ('Cat Modeling', 'Modeling'),
    ('Analytics', 'Analytics'),
    ('Reporting', 'Reporting')
) AS src(CategoryName, CatFunction)
ON tgt.CategoryName = src.CategoryName
WHEN MATCHED THEN UPDATE SET CatFunction = src.CatFunction, IsActive = 1
WHEN NOT MATCHED THEN INSERT (CategoryName, CatFunction, IsActive) VALUES (src.CategoryName, src.CatFunction, 1);
GO

MERGE dbo.tUserRole AS tgt
USING (VALUES
    ('Admin', 'System administrator'),
    ('Account Executive', 'Commercial owner'),
    ('Analytics Lead', 'Analytics owner'),
    ('Modeler', 'Catastrophe modeler'),
    ('Viewer', 'Read-only user')
) AS src(UserRoleName, RoleDescription)
ON tgt.UserRoleName = src.UserRoleName
WHEN MATCHED THEN UPDATE SET RoleDescription = src.RoleDescription, IsActive = 1
WHEN NOT MATCHED THEN INSERT (UserRoleName, RoleDescription, IsActive) VALUES (src.UserRoleName, src.RoleDescription, 1);
GO
