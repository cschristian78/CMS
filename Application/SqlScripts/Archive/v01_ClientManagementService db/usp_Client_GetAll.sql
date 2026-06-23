/*
    Replace dbo.usp_Client_GetAll if it still references SegmentID / legacy columns.
    Aligns with dbo.tClient using BUID and BCID.

    Deploy on the application database, then any external callers of usp_Client_GetAll match app behavior.
*/

CREATE OR ALTER PROCEDURE dbo.usp_Client_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.ClientID,
        c.ClientName,
        c.ClientShortName,
        c.StatusID,
        st.StatusCode,
        st.StatusDescription,
        c.BUID,
        bu.BUName,
        c.BCID,
        bc.BCName
    FROM dbo.tClient AS c
    LEFT JOIN dbo.tStatus AS st ON c.StatusID = st.StatusID
    LEFT JOIN dbo.tBusinessUnit AS bu ON c.BUID = bu.BUID
    LEFT JOIN dbo.tBusinessClass AS bc ON c.BCID = bc.BCID
    ORDER BY c.ClientName, c.ClientID;
END;
GO
