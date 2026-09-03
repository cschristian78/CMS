-- Adds the Manager user role. Safe to re-run.
MERGE dbo.tUserRole AS tgt
USING (VALUES
    ('Manager', 'Manages project resources and delivery')
) AS src (UserRoleName, RoleDescription)
  ON tgt.UserRoleName = src.UserRoleName
WHEN MATCHED THEN
    UPDATE SET
        RoleDescription = src.RoleDescription,
        IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (UserRoleName, RoleDescription, IsActive)
    VALUES (src.UserRoleName, src.RoleDescription, 1);
GO
