USE BMS_CMS;
GO
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER PROCEDURE usp_User_CreateWithPrimaryAddress
    @FirstName                    VARCHAR(50),
    @LastName                     VARCHAR(50),
    @Title                        VARCHAR(100) = NULL,
    @Email                        VARCHAR(150),
    @Phone                        VARCHAR(30) = NULL,
    @Company                      VARCHAR(150) = NULL,
    @Department                   VARCHAR(100) = NULL,
    @UserType                     VARCHAR(20),
    @AddressType                  VARCHAR(20) = NULL,
    @Address1                     VARCHAR(200) = NULL,
    @Address2                     VARCHAR(200) = NULL,
    @City                         VARCHAR(100) = NULL,
    @State                        VARCHAR(50)  = NULL,
    @PostalCode                   VARCHAR(20)  = NULL,
    @Country                      VARCHAR(50)  = NULL,
    @ActingUserID                INT,
    @NewUserID                   INT OUTPUT,
    @NewAddressID                INT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @UserType NOT IN ('Basic', 'External', 'Administrator', 'Manager', 'Analyst')
        THROW 50003, 'UserType must be Basic, External, Administrator, Manager, or Analyst.', 1;

    IF EXISTS (SELECT 1 FROM dbo.tUser WHERE Email = @Email AND IsActive = 1)
        THROW 50004, 'Email already exists.', 1;

    SET @NewAddressID = NULL;

    BEGIN TRY
        BEGIN TRAN;

        INSERT INTO dbo.tUser (FirstName, LastName, Title, Email, Phone, Company, Department, UserType)
        VALUES (@FirstName, @LastName, @Title, @Email, @Phone, @Company, @Department, @UserType);

        SET @NewUserID = SCOPE_IDENTITY();

        EXEC dbo.usp_ActivityLog_Insert
            @EntityType = 'User',
            @EntityID   = @NewUserID,
            @UserID     = @ActingUserID,
            @Action     = 'Insert';

        IF NULLIF(LTRIM(RTRIM(@Address1)), '') IS NOT NULL
        BEGIN
            DECLARE @AddrType VARCHAR(20) = COALESCE(NULLIF(LTRIM(RTRIM(@AddressType)), ''), 'Work');
            DECLARE @AddrCountry VARCHAR(50) = COALESCE(NULLIF(LTRIM(RTRIM(@Country)), ''), 'US');

            INSERT INTO dbo.tAddress (Address1, Address2, City, State, PostalCode, Country)
            VALUES (
                LTRIM(RTRIM(@Address1)),
                @Address2,
                @City,
                @State,
                @PostalCode,
                @AddrCountry
            );

            SET @NewAddressID = SCOPE_IDENTITY();

            EXEC dbo.usp_ActivityLog_Insert
                @EntityType = 'Address',
                @EntityID   = @NewAddressID,
                @UserID     = @ActingUserID,
                @Action     = 'Insert';

            INSERT INTO dbo.tUserAddressRef (UserID, AddressID, AddressType, IsPrimary)
            VALUES (@NewUserID, @NewAddressID, @AddrType, 1);

            EXEC dbo.usp_ActivityLog_Insert
                @EntityType = 'UserAddressRef',
                @EntityID   = @NewUserID,
                @UserID     = @ActingUserID,
                @Action     = 'Insert';
        END;

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        THROW;
    END CATCH
END;
GO
