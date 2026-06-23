
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER VIEW dbo.vClient
AS
SELECT *
FROM dbo.tClient
WHERE IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vProject
AS
SELECT *
FROM dbo.tProject
WHERE IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vUser
AS
SELECT *
FROM dbo.tUser
WHERE IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vUserGroup
AS
SELECT *
FROM dbo.tUserGroup
WHERE IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vService
AS
SELECT *
FROM dbo.tService
WHERE IsDeleted = 0;
GO

CREATE OR ALTER VIEW dbo.vClient_WithBU
AS
SELECT
    c.ClientID,
    c.ClientName,
    c.ClientShortName,
    bu.BusinessUnitID,
    bu.BUName,
    bc.BusinessClassID,
    bc.BCName,
    cc.ClientCategoryID,
    cc.CCName,
    ct.ClientTypeID,
    ct.CTName,
    s.StatusID,
    s.StatusCode,
    s.StatusDescription,
    c.CreatedDate,
    c.ModifiedDate
FROM dbo.vClient c
JOIN dbo.tBusinessClass bc ON c.BusinessClassID = bc.BusinessClassID
JOIN dbo.tBusinessUnit bu ON bc.BusinessUnitID = bu.BusinessUnitID
JOIN dbo.tClientCategory cc ON c.ClientCategoryID = cc.ClientCategoryID
JOIN dbo.tClientType ct ON c.ClientTypeID = ct.ClientTypeID
JOIN dbo.tStatus s ON c.StatusID = s.StatusID;
GO

CREATE OR ALTER VIEW dbo.vProject_Current
AS
SELECT
    p.ProjectID,
    p.ProjectNumber,
    p.ProjectName,
    p.ClientID,
    c.ClientName,
    bc.BusinessClassID,
    bc.BCName,
    bu.BusinessUnitID,
    bu.BUName,
    p.UGID,
    ug.UGName,
    p.ProjectTypeID,
    pt.PTCode,
    pt.PTDescription,
    p.RenewalDate,
    p.DueDate,
    p.StatusID,
    s.StatusCode,
    s.StatusDescription,
    p.OwnerUserID,
    CONCAT(u.FirstName, ' ', u.LastName) AS OwnerName,
    lr.RevenueID AS LatestRevenueID,
    lr.EffectiveDate AS LatestRevenueEffectiveDate,
    lr.Revenue AS LatestRevenue,
    p.CreatedDate,
    p.ModifiedDate
FROM dbo.vProject p
JOIN dbo.vClient c ON p.ClientID = c.ClientID
JOIN dbo.tBusinessClass bc ON c.BusinessClassID = bc.BusinessClassID
JOIN dbo.tBusinessUnit bu ON bc.BusinessUnitID = bu.BusinessUnitID
JOIN dbo.vUserGroup ug ON p.UGID = ug.UGID
JOIN dbo.tProjectType pt ON p.ProjectTypeID = pt.ProjectTypeID
JOIN dbo.tStatus s ON p.StatusID = s.StatusID
JOIN dbo.vUser u ON p.OwnerUserID = u.UserID
OUTER APPLY
(
    SELECT TOP (1) pr.*
    FROM dbo.tProjectRevenue pr
    WHERE pr.ProjectID = p.ProjectID
    ORDER BY pr.EffectiveDate DESC, pr.RevenueID DESC
) lr;
GO
