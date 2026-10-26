using System.Text.Json;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

/// <summary>CatAI tools for users, user-group membership, client leads, and reverse lookups.</summary>
public sealed partial class CatAiToolService
{
    private const int UserGroupsForUserLimit = 500;
    private const int ProjectsForGroupLimit = 500;
    private const int CatUsersLimit = 500;
    private const int UsersByRoleLimit = 500;

    private async Task<object> GetUserAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var resolved = await ResolveUserFromArgsAsync(root, includeInactive: true, cancellationToken);
        if (resolved.Error is not null)
        {
            return new { error = resolved.Error };
        }

        var u = resolved.User!;
        const string membershipSql = """
            SELECT
                ug.UserGroupID,
                ug.UserGroupNumber,
                ug.UserGroupName,
                ug.IsActive,
                ugr.UserRoleID,
                ur.UserRoleName
              FROM dbo.tUserGroupRef AS ugr
              INNER JOIN dbo.tUserGroup AS ug ON ug.UserGroupID = ugr.UserGroupID
              LEFT JOIN dbo.tUserRole AS ur ON ur.UserRoleID = ugr.UserRoleID
             WHERE ugr.UserID = @UserID
             ORDER BY ug.UserGroupName, ug.UserGroupID;
            """;

        var groups = await _db.QueryTextAsync(
            membershipSql,
            r => new
            {
                userGroupId = r.GetInt32(0),
                userGroupNumber = r.IsDBNull(1) ? null : r.GetString(1),
                userGroupName = r.IsDBNull(2) ? null : r.GetString(2),
                isActive = !r.IsDBNull(3) && r.GetBoolean(3),
                userRoleId = r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
                userRoleName = r.IsDBNull(5) ? null : r.GetString(5),
            },
            [SqlParameterFactory.Int("@UserID", u.UserID)],
            cancellationToken);

        return new
        {
            userId = u.UserID,
            firstName = u.FirstName,
            lastName = u.LastName,
            fullName = string.IsNullOrWhiteSpace(u.FullName)
                ? $"{u.FirstName} {u.LastName}".Trim()
                : u.FullName,
            email = u.Email,
            phone = u.Phone,
            title = u.Title,
            company = u.Company,
            department = u.Department,
            userType = u.UserType,
            isActive = !u.IsDeleted,
            groupCount = groups.Count,
            groups,
        };
    }

    private async Task<object> ListGroupsForUserAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var resolved = await ResolveUserFromArgsAsync(root, includeInactive: true, cancellationToken);
        if (resolved.Error is not null)
        {
            return new { error = resolved.Error };
        }

        var includeInactiveGroups = GetBool(root, "includeInactiveGroups");
        var u = resolved.User!;

        const string sql = """
            SELECT TOP (500)
                ug.UserGroupID,
                ug.UserGroupNumber,
                ug.UserGroupName,
                ug.IsActive,
                ugr.UserRoleID,
                ur.UserRoleName
              FROM dbo.tUserGroupRef AS ugr
              INNER JOIN dbo.tUserGroup AS ug ON ug.UserGroupID = ugr.UserGroupID
              LEFT JOIN dbo.tUserRole AS ur ON ur.UserRoleID = ugr.UserRoleID
             WHERE ugr.UserID = @UserID
               AND (@IncludeInactiveGroups = 1 OR ug.IsActive = 1)
             ORDER BY ug.UserGroupName, ug.UserGroupID;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            r => new
            {
                userGroupId = r.GetInt32(0),
                userGroupNumber = r.IsDBNull(1) ? null : r.GetString(1),
                userGroupName = r.IsDBNull(2) ? null : r.GetString(2),
                isActive = !r.IsDBNull(3) && r.GetBoolean(3),
                userRoleId = r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
                userRoleName = r.IsDBNull(5) ? null : r.GetString(5),
            },
            [
                SqlParameterFactory.Int("@UserID", u.UserID),
                SqlParameterFactory.Bit("@IncludeInactiveGroups", includeInactiveGroups),
            ],
            cancellationToken);

        var name = string.IsNullOrWhiteSpace(u.FullName)
            ? $"{u.FirstName} {u.LastName}".Trim()
            : u.FullName;

        return new
        {
            userId = u.UserID,
            userName = name,
            includeInactiveGroups,
            count = rows.Count,
            truncated = rows.Count >= UserGroupsForUserLimit,
            groups = rows,
            message = $"{name} is a member of {rows.Count} user group(s).",
        };
    }

    private async Task<object> ListProjectsForUserGroupAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var ugid = GetInt(root, "userGroupId");
        if (ugid <= 0)
        {
            return new { error = "userGroupId must be a positive integer." };
        }

        var group = await _admin.GetUserGroupAsync(ugid, cancellationToken)
            ?? (await _admin.ListUserGroupsAsync(null, includeDeleted: true, cancellationToken))
                .FirstOrDefault(g => g.UGID == ugid);
        if (group is null)
        {
            return new { error = $"User group {ugid} was not found." };
        }

        var includeInactiveProjects = GetBool(root, "includeInactiveProjects");

        const string sql = """
            SELECT TOP (500)
                p.ProjectID,
                p.ProjectNumber,
                p.ProjectName,
                c.ClientName,
                IsActive = CAST(CASE WHEN p.IsActive = 1 THEN 1 ELSE 0 END AS BIT),
                IsOnline = CAST(CASE WHEN ISNULL(NULLIF(LTRIM(RTRIM(p.WorkStatus)), N''), N'Planned') = N'In Progress' THEN 1 ELSE 0 END AS BIT),
                st.StatusCode
              FROM dbo.tProject AS p
             INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectID = p.ProjectID AND pr.IsActive = 1
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
              LEFT JOIN dbo.tStatus AS st ON st.StatusID = pr.StatusID
             WHERE pr.UserGroupID = @UserGroupID
               AND (@IncludeInactive = 1 OR p.IsActive = 1)
             ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            r => new
            {
                projectId = r.GetInt32(0),
                projectNumber = r.IsDBNull(1) ? null : r.GetString(1),
                projectName = r.IsDBNull(2) ? null : r.GetString(2),
                clientName = r.IsDBNull(3) ? null : r.GetString(3),
                isActive = !r.IsDBNull(4) && r.GetBoolean(4),
                isOnline = !r.IsDBNull(5) && r.GetBoolean(5),
                statusCode = r.IsDBNull(6) ? null : r.GetString(6),
            },
            [
                SqlParameterFactory.Int("@UserGroupID", ugid),
                SqlParameterFactory.Bit("@IncludeInactive", includeInactiveProjects),
            ],
            cancellationToken);

        return new
        {
            userGroupId = ugid,
            userGroupName = group.UGName,
            userGroupNumber = group.UserGroupNumber,
            includeInactiveProjects,
            count = rows.Count,
            truncated = rows.Count >= ProjectsForGroupLimit,
            projects = rows,
        };
    }

    private async Task<object> ListCatUsersAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var withoutGroupOnly = GetBool(root, "withoutGroupOnly");
        var includeInactive = GetBool(root, "includeInactive");

        const string sql = """
            SELECT TOP (500)
                u.UserID,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS FullName,
                u.Email,
                u.Title,
                u.Department,
                u.IsActive,
                GroupCount = (
                    SELECT COUNT(1)
                      FROM dbo.tUserGroupRef AS ugr
                      INNER JOIN dbo.tUserGroup AS ug ON ug.UserGroupID = ugr.UserGroupID AND ug.IsActive = 1
                     WHERE ugr.UserID = u.UserID
                )
              FROM dbo.tUser AS u
             WHERE UPPER(LTRIM(RTRIM(ISNULL(u.Department, N'')))) = N'CAT'
               AND (@IncludeInactive = 1 OR u.IsActive = 1)
               AND (
                    @WithoutGroupOnly = 0
                 OR NOT EXISTS (
                        SELECT 1
                          FROM dbo.tUserGroupRef AS ugr2
                          INNER JOIN dbo.tUserGroup AS ug2 ON ug2.UserGroupID = ugr2.UserGroupID AND ug2.IsActive = 1
                         WHERE ugr2.UserID = u.UserID
                    )
                   )
             ORDER BY u.LastName, u.FirstName, u.UserID;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            r => new
            {
                userId = r.GetInt32(0),
                name = r.IsDBNull(1) ? null : r.GetString(1),
                email = r.IsDBNull(2) ? null : r.GetString(2),
                title = r.IsDBNull(3) ? null : r.GetString(3),
                department = r.IsDBNull(4) ? null : r.GetString(4),
                isActive = !r.IsDBNull(5) && r.GetBoolean(5),
                activeGroupCount = r.IsDBNull(6) ? 0 : r.GetInt32(6),
            },
            [
                SqlParameterFactory.Bit("@WithoutGroupOnly", withoutGroupOnly),
                SqlParameterFactory.Bit("@IncludeInactive", includeInactive),
            ],
            cancellationToken);

        return new
        {
            withoutGroupOnly,
            includeInactive,
            count = rows.Count,
            truncated = rows.Count >= CatUsersLimit,
            users = rows,
        };
    }

    private async Task<object> GetProjectCatCcAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var projectId = GetInt(root, "projectId");
        if (projectId <= 0)
        {
            return new { error = "projectId must be a positive integer." };
        }

        var project = await _projects.GetFullAsync(projectId, cancellationToken)
            ?? await _projects.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return new { error = $"Project {projectId} was not found." };
        }

        var catUsers = await _projects.ListCatDepartmentUsersForProjectAsync(projectId, cancellationToken);
        var emails = catUsers
            .Select(u => (u.Email ?? string.Empty).Trim())
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new
        {
            projectId,
            projectNumber = project.ProjectNumber,
            projectName = project.ProjectName,
            userGroupId = project.UGID > 0 ? project.UGID : (int?)null,
            userGroupName = project.UGName,
            count = catUsers.Count,
            users = catUsers.Select(u => new
            {
                userId = u.UserID,
                name = u.UserFullName,
                email = u.Email,
            }),
            ccEmails = emails,
            message = emails.Count == 0
                ? "No CAT-department members with email were found on this project's user group."
                : $"Found {emails.Count} CAT CC email(s) from the project's user group.",
        };
    }

    private async Task<object> RemoveUserGroupMemberAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var ugid = GetInt(root, "userGroupId");
        if (ugid <= 0)
        {
            return new { error = "userGroupId must be a positive integer." };
        }

        var resolved = await ResolveUserFromArgsAsync(root, includeInactive: true, cancellationToken);
        if (resolved.Error is not null)
        {
            return new { error = resolved.Error };
        }

        var user = resolved.User!;
        var userLabel = string.IsNullOrWhiteSpace(user.FullName)
            ? $"{user.FirstName} {user.LastName}".Trim()
            : user.FullName!;

        var groups = await _admin.ListUserGroupsAsync(null, includeDeleted: true, cancellationToken);
        var group = groups.FirstOrDefault(g => g.UGID == ugid);
        if (group is null)
        {
            return new { error = $"User group {ugid} was not found." };
        }

        var existing = await _admin.ListUserGroupMembersAsync(ugid, cancellationToken);
        if (existing.All(m => m.UserID != user.UserID))
        {
            return new
            {
                error = $"{userLabel} is not a member of user group {ugid} ({group.UGName}).",
            };
        }

        await _admin.RemoveUserGroupMemberAsync(ugid, user.UserID, cancellationToken);
        return new
        {
            success = true,
            userGroupId = ugid,
            userGroupName = group.UGName,
            userId = user.UserID,
            userName = userLabel,
            message = "Member removed from the user group.",
        };
    }

    private async Task<object> UpdateUserGroupAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var ugid = GetInt(root, "userGroupId");
        if (ugid <= 0)
        {
            return new { error = "userGroupId must be a positive integer." };
        }

        var group = await _admin.GetUserGroupAsync(ugid, cancellationToken)
            ?? (await _admin.ListUserGroupsAsync(null, includeDeleted: true, cancellationToken))
                .FirstOrDefault(g => g.UGID == ugid);
        if (group is null)
        {
            return new { error = $"User group {ugid} was not found." };
        }

        var newName = GetString(root, "userGroupName").Trim();
        var newNumber = GetString(root, "userGroupNumber").Trim();
        if (string.IsNullOrWhiteSpace(newName) && string.IsNullOrWhiteSpace(newNumber))
        {
            return new { error = "Provide userGroupName and/or userGroupNumber to update." };
        }

        if (!string.IsNullOrWhiteSpace(newName))
        {
            group.UGName = newName;
        }

        if (!string.IsNullOrWhiteSpace(newNumber))
        {
            group.UserGroupNumber = newNumber;
        }

        await _admin.SaveUserGroupAsync(group, cancellationToken);
        var saved = await _admin.GetUserGroupAsync(ugid, cancellationToken) ?? group;

        return new
        {
            success = true,
            userGroupId = ugid,
            userGroupName = saved.UGName,
            userGroupNumber = saved.UserGroupNumber,
            message = "User group was updated.",
        };
    }

    private async Task<object> SoftDeleteUserGroupToolAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var ugid = GetInt(root, "userGroupId");
        if (ugid <= 0)
        {
            return new { error = "userGroupId must be a positive integer." };
        }

        var group = await _admin.GetUserGroupAsync(ugid, cancellationToken)
            ?? (await _admin.ListUserGroupsAsync(null, includeDeleted: true, cancellationToken))
                .FirstOrDefault(g => g.UGID == ugid);
        if (group is null)
        {
            return new { error = $"User group {ugid} was not found." };
        }

        if (group.IsDeleted)
        {
            return new { error = $"User group {ugid} is already inactive.", userGroupId = ugid };
        }

        const string linkedSql = """
            SELECT COUNT(1)
              FROM dbo.tProjectRef AS pr
             INNER JOIN dbo.tProject AS p ON p.ProjectID = pr.ProjectID AND p.IsActive = 1
             WHERE pr.IsActive = 1
               AND pr.UserGroupID = @UserGroupID;
            """;

        var linked = await _db.QueryTextAsync(
            linkedSql,
            r => r.GetInt32(0),
            [SqlParameterFactory.Int("@UserGroupID", ugid)],
            cancellationToken);
        var linkedCount = linked.FirstOrDefault();
        var force = GetBool(root, "force");
        if (linkedCount > 0 && !force)
        {
            return new
            {
                error =
                    $"User group {ugid} is assigned to {linkedCount} active project(s). "
                    + "Unassign those projects first, or pass force=true to soft-delete anyway.",
                linkedActiveProjectCount = linkedCount,
            };
        }

        await _admin.SoftDeleteUserGroupAsync(ugid, cancellationToken);
        return new
        {
            success = true,
            userGroupId = ugid,
            userGroupName = group.UGName,
            linkedActiveProjectCount = linkedCount,
            message = "User group soft-deleted (deactivated).",
        };
    }

    private async Task<object> UpdateClientLeadsAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var clientId = GetInt(root, "clientId");
        if (clientId <= 0)
        {
            return new { error = "clientId must be a positive integer." };
        }

        var clients = await _admin.ListClientsAsync(null, includeInactive: true, cancellationToken);
        var client = clients.FirstOrDefault(c => c.ClientID == clientId);
        if (client is null)
        {
            return new { error = $"Client {clientId} was not found." };
        }

        var hasAe = root.TryGetProperty("accountExecutiveUserId", out _)
            || root.TryGetProperty("accountExecutiveUserName", out _)
            || root.TryGetProperty("clearAccountExecutive", out _);
        var hasAl = root.TryGetProperty("analyticsLeadUserId", out _)
            || root.TryGetProperty("analyticsLeadUserName", out _)
            || root.TryGetProperty("clearAnalyticsLead", out _);

        if (!hasAe && !hasAl)
        {
            return new
            {
                error =
                    "Provide accountExecutiveUserId/accountExecutiveUserName (or clearAccountExecutive), "
                    + "and/or analyticsLeadUserId/analyticsLeadUserName (or clearAnalyticsLead).",
            };
        }

        int? aeId = client.AccountExecutiveUserID;
        int? alId = client.AnalyticsLeadUserID;
        string? aeLabel = client.AccountExecutiveName;
        string? alLabel = client.AnalyticsLeadName;

        if (GetBool(root, "clearAccountExecutive"))
        {
            aeId = null;
            aeLabel = null;
        }
        else if (hasAe)
        {
            var ae = await ResolveUserFromArgsAsync(
                WrapUserArgs(root, "accountExecutiveUserId", "accountExecutiveUserName"),
                includeInactive: false,
                cancellationToken);
            if (ae.Error is not null)
            {
                return new { error = "Account Executive: " + ae.Error };
            }

            aeId = ae.User!.UserID;
            aeLabel = string.IsNullOrWhiteSpace(ae.User.FullName)
                ? $"{ae.User.FirstName} {ae.User.LastName}".Trim()
                : ae.User.FullName;
        }

        if (GetBool(root, "clearAnalyticsLead"))
        {
            alId = null;
            alLabel = null;
        }
        else if (hasAl)
        {
            var al = await ResolveUserFromArgsAsync(
                WrapUserArgs(root, "analyticsLeadUserId", "analyticsLeadUserName"),
                includeInactive: false,
                cancellationToken);
            if (al.Error is not null)
            {
                return new { error = "Analytics Lead: " + al.Error };
            }

            alId = al.User!.UserID;
            alLabel = string.IsNullOrWhiteSpace(al.User.FullName)
                ? $"{al.User.FirstName} {al.User.LastName}".Trim()
                : al.User.FullName;
        }

        await _admin.SaveClientAsync(new ClientAdminSaveRequest
        {
            ClientID = client.ClientID,
            ClientName = client.ClientName ?? string.Empty,
            ClientShortName = client.ClientShortName,
            ClientDescription = client.ClientDescription,
            IsActive = client.IsActive,
            ClientStatus = string.IsNullOrWhiteSpace(client.ClientStatus) ? "Active" : client.ClientStatus,
            AccountExecutiveUserID = aeId,
            AnalyticsLeadUserID = alId,
            BusinessUnitID = client.BusinessUnitID,
            BusinessClassID = client.BusinessClassID,
            ClientTypeID = client.ClientTypeID,
        }, cancellationToken);

        return new
        {
            success = true,
            clientId,
            clientName = client.ClientName,
            accountExecutiveUserId = aeId,
            accountExecutiveName = aeLabel,
            analyticsLeadUserId = alId,
            analyticsLeadName = alLabel,
            message = "Client Account Executive / Analytics Lead updated.",
        };
    }

    private async Task<object> EnsureGroupAnalyticsLeadAsync(JsonElement root, CancellationToken cancellationToken)
        => await EnsureGroupRoleAsync(root, AnalyticsLeadRoleId, "Analytics Lead", cancellationToken);

    private async Task<object> EnsureGroupAccountExecutiveAsync(JsonElement root, CancellationToken cancellationToken)
        => await EnsureGroupRoleAsync(root, AccountExecutiveRoleId, "Account Executive", cancellationToken);

    private async Task<object> EnsureGroupRoleAsync(
        JsonElement root,
        int roleId,
        string roleName,
        CancellationToken cancellationToken)
    {
        var ugid = GetInt(root, "userGroupId");
        if (ugid <= 0)
        {
            return new { error = "userGroupId must be a positive integer." };
        }

        var userId = GetInt(root, "userId");
        var userName = GetString(root, "userName");
        if (userId <= 0 && string.IsNullOrWhiteSpace(userName))
        {
            return new { error = "Provide userId or userName for the member who should hold this role." };
        }

        using var addArgs = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            userGroupId = ugid,
            userId = userId > 0 ? userId : (int?)null,
            userName = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim(),
            roleId,
            roleName,
        }));

        var result = await AddUserGroupMemberAsync(addArgs.RootElement, cancellationToken);
        return result;
    }

    private async Task<object> CreateUserAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var firstName = GetString(root, "firstName").Trim();
        var lastName = GetString(root, "lastName").Trim();
        var email = GetString(root, "email").Trim();
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(email))
        {
            return new { error = "firstName, lastName, and email are required." };
        }

        var userType = GetString(root, "userType").Trim();
        if (string.IsNullOrWhiteSpace(userType))
        {
            userType = "Basic";
        }

        if (userType is not ("Basic" or "External" or "Administrator" or "Manager" or "Analyst"))
        {
            return new { error = "userType must be Basic, External, Administrator, Manager, or Analyst." };
        }

        var existing = await _reference.GetTopUsersAsync(email, activeOnly: false, cancellationToken);
        var emailClash = existing.FirstOrDefault(u =>
            string.Equals((u.Email ?? string.Empty).Trim(), email, StringComparison.OrdinalIgnoreCase));
        if (emailClash is not null)
        {
            return new
            {
                error = $"A user with email '{email}' already exists (userId {emailClash.UserID}).",
                userId = emailClash.UserID,
            };
        }

        var user = new User
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Title = NullIfBlank(GetString(root, "title")),
            Phone = NullIfBlank(GetString(root, "phone")),
            Company = NullIfBlank(GetString(root, "company")),
            Department = NullIfBlank(GetString(root, "department")),
            UserType = userType,
        };

        var userId = await _admin.SaveUserAsync(user, cancellationToken);
        return new
        {
            success = true,
            userId,
            firstName,
            lastName,
            email,
            department = user.Department,
            userType,
            message = "User created.",
        };
    }

    private async Task<object> UpdateUserAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var resolved = await ResolveUserFromArgsAsync(root, includeInactive: true, cancellationToken);
        if (resolved.Error is not null)
        {
            return new { error = resolved.Error };
        }

        var user = resolved.User!;
        if (root.TryGetProperty("firstName", out _) && !string.IsNullOrWhiteSpace(GetString(root, "firstName")))
        {
            user.FirstName = GetString(root, "firstName").Trim();
        }

        if (root.TryGetProperty("lastName", out _) && !string.IsNullOrWhiteSpace(GetString(root, "lastName")))
        {
            user.LastName = GetString(root, "lastName").Trim();
        }

        if (root.TryGetProperty("email", out _) && !string.IsNullOrWhiteSpace(GetString(root, "email")))
        {
            user.Email = GetString(root, "email").Trim();
        }

        if (root.TryGetProperty("title", out _))
        {
            user.Title = NullIfBlank(GetString(root, "title"));
        }

        if (root.TryGetProperty("phone", out _))
        {
            user.Phone = NullIfBlank(GetString(root, "phone"));
        }

        if (root.TryGetProperty("company", out _))
        {
            user.Company = NullIfBlank(GetString(root, "company"));
        }

        if (root.TryGetProperty("department", out _))
        {
            user.Department = NullIfBlank(GetString(root, "department"));
        }

        if (root.TryGetProperty("userType", out _) && !string.IsNullOrWhiteSpace(GetString(root, "userType")))
        {
            var userType = GetString(root, "userType").Trim();
            if (userType is not ("Basic" or "External" or "Administrator" or "Manager" or "Analyst"))
            {
                return new { error = "userType must be Basic, External, Administrator, Manager, or Analyst." };
            }

            user.UserType = userType;
        }

        await _admin.SaveUserAsync(user, cancellationToken);
        return new
        {
            success = true,
            userId = user.UserID,
            firstName = user.FirstName,
            lastName = user.LastName,
            email = user.Email,
            department = user.Department,
            userType = user.UserType,
            message = "User updated.",
        };
    }

    private async Task<object> DeactivateUserAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var resolved = await ResolveUserFromArgsAsync(root, includeInactive: true, cancellationToken);
        if (resolved.Error is not null)
        {
            return new { error = resolved.Error };
        }

        var user = resolved.User!;
        if (user.IsDeleted)
        {
            return new { error = $"User {user.UserID} is already inactive.", userId = user.UserID };
        }

        await _admin.SoftDeleteUserAsync(user.UserID, cancellationToken);
        return new
        {
            success = true,
            userId = user.UserID,
            name = string.IsNullOrWhiteSpace(user.FullName)
                ? $"{user.FirstName} {user.LastName}".Trim()
                : user.FullName,
            message = "User deactivated (soft-deleted).",
        };
    }

    private async Task<object> RestoreUserAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var resolved = await ResolveUserFromArgsAsync(root, includeInactive: true, cancellationToken);
        if (resolved.Error is not null)
        {
            return new { error = resolved.Error };
        }

        var user = resolved.User!;
        if (!user.IsDeleted)
        {
            return new
            {
                success = true,
                userId = user.UserID,
                message = "User is already active.",
            };
        }

        await _admin.RestoreUserAsync(user.UserID, cancellationToken);
        return new
        {
            success = true,
            userId = user.UserID,
            name = string.IsNullOrWhiteSpace(user.FullName)
                ? $"{user.FirstName} {user.LastName}".Trim()
                : user.FullName,
            message = "User restored (re-activated).",
        };
    }

    private async Task<object> UnassignProjectUserGroupAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var projectId = GetInt(root, "projectId");
        if (projectId <= 0)
        {
            return new { error = "projectId must be a positive integer." };
        }

        var project = await _projects.GetFullAsync(projectId, cancellationToken)
            ?? await _projects.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return new { error = $"Project {projectId} was not found." };
        }

        var previousGroupId = project.UGID > 0 ? project.UGID : (int?)null;
        var previousGroupName = project.UGName;

        await _projects.UnassignUserGroupAsync(projectId, cancellationToken);

        return new
        {
            success = true,
            projectId,
            projectNumber = project.ProjectNumber,
            projectName = project.ProjectName,
            previousUserGroupId = previousGroupId,
            previousUserGroupName = previousGroupName,
            message = "Project user group was cleared.",
        };
    }

    private async Task<object> ListUsersByRoleAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var roleId = GetInt(root, "roleId");
        var roleName = GetString(root, "roleName").Trim();
        if (roleId <= 0 && string.IsNullOrWhiteSpace(roleName))
        {
            return new { error = "Provide roleId or roleName (e.g. Analytics Lead, Account Executive)." };
        }

        if (roleId <= 0)
        {
            roleId = await ResolveRoleIdAsync(roleName, cancellationToken);
            if (roleId <= 0)
            {
                return new { error = $"Role '{roleName}' was not found." };
            }
        }

        var roles = await _reference.GetUserRolesAsync(cancellationToken);
        var role = roles.FirstOrDefault(r => r.UserRoleID == roleId);
        var resolvedRoleName = role?.UserRoleName ?? roleName;
        var activeGroupsOnly = !GetBool(root, "includeInactiveGroups");

        const string sql = """
            SELECT TOP (500)
                u.UserID,
                LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS FullName,
                u.Email,
                u.Department,
                ug.UserGroupID,
                ug.UserGroupNumber,
                ug.UserGroupName,
                ug.IsActive
              FROM dbo.tUserGroupRef AS ugr
              INNER JOIN dbo.tUser AS u ON u.UserID = ugr.UserID AND u.IsActive = 1
              INNER JOIN dbo.tUserGroup AS ug ON ug.UserGroupID = ugr.UserGroupID
             WHERE ugr.UserRoleID = @UserRoleID
               AND (@ActiveGroupsOnly = 0 OR ug.IsActive = 1)
             ORDER BY u.LastName, u.FirstName, ug.UserGroupName, ug.UserGroupID;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            r => new
            {
                userId = r.GetInt32(0),
                name = r.IsDBNull(1) ? null : r.GetString(1),
                email = r.IsDBNull(2) ? null : r.GetString(2),
                department = r.IsDBNull(3) ? null : r.GetString(3),
                userGroupId = r.GetInt32(4),
                userGroupNumber = r.IsDBNull(5) ? null : r.GetString(5),
                userGroupName = r.IsDBNull(6) ? null : r.GetString(6),
                groupIsActive = !r.IsDBNull(7) && r.GetBoolean(7),
            },
            [
                SqlParameterFactory.Int("@UserRoleID", roleId),
                SqlParameterFactory.Bit("@ActiveGroupsOnly", activeGroupsOnly),
            ],
            cancellationToken);

        var distinctUsers = rows.Select(r => r.userId).Distinct().Count();

        return new
        {
            roleId,
            roleName = resolvedRoleName,
            activeGroupsOnly,
            assignmentCount = rows.Count,
            distinctUserCount = distinctUsers,
            truncated = rows.Count >= UsersByRoleLimit,
            assignments = rows,
        };
    }

    private async Task<(User? User, string? Error)> ResolveUserFromArgsAsync(
        JsonElement root,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var userId = GetInt(root, "userId");
        var userName = GetString(root, "userName");
        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = GetString(root, "query");
        }

        if (userId > 0)
        {
            const string sql = """
                SELECT TOP (1)
                    u.UserID,
                    u.FirstName,
                    u.LastName,
                    LTRIM(RTRIM(CONCAT(u.FirstName, N' ', u.LastName))) AS FullName,
                    u.Title,
                    u.Department,
                    u.Email,
                    u.Phone,
                    u.Company,
                    u.UserType,
                    u.IsActive,
                    u.CreatedDate,
                    u.ModifiedDate
                  FROM dbo.tUser AS u
                 WHERE u.UserID = @UserID;
                """;

            var rows = await _db.QueryTextAsync(
                sql,
                r => new User
                {
                    UserID = r.GetInt32(0),
                    FirstName = r.IsDBNull(1) ? null : r.GetString(1),
                    LastName = r.IsDBNull(2) ? null : r.GetString(2),
                    FullName = r.IsDBNull(3) ? null : r.GetString(3),
                    Title = r.IsDBNull(4) ? null : r.GetString(4),
                    Department = r.IsDBNull(5) ? null : r.GetString(5),
                    Email = r.IsDBNull(6) ? null : r.GetString(6),
                    Phone = r.IsDBNull(7) ? null : r.GetString(7),
                    Company = r.IsDBNull(8) ? null : r.GetString(8),
                    UserType = r.IsDBNull(9) ? "Basic" : r.GetString(9),
                    IsDeleted = r.IsDBNull(10) || !r.GetBoolean(10),
                    CreatedDate = r.IsDBNull(11) ? null : r.GetDateTime(11),
                    ModifiedDate = r.IsDBNull(12) ? null : r.GetDateTime(12),
                },
                [SqlParameterFactory.Int("@UserID", userId)],
                cancellationToken);

            var user = rows.FirstOrDefault();
            if (user is null)
            {
                return (null, $"User {userId} was not found.");
            }

            if (!includeInactive && user.IsDeleted)
            {
                return (null, $"User {userId} is inactive.");
            }

            return (user, null);
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            return (null, "Provide userId or userName (name/email).");
        }

        var matches = await _reference.GetTopUsersAsync(userName.Trim(), activeOnly: !includeInactive, cancellationToken);
        if (matches.Count == 0)
        {
            return (null, $"No users matched '{userName}'.");
        }

        if (matches.Count > 1)
        {
            var options = string.Join(
                "; ",
                matches.Take(8).Select(m =>
                    $"{m.UserID}: {(string.IsNullOrWhiteSpace(m.FullName) ? $"{m.FirstName} {m.LastName}".Trim() : m.FullName)} <{m.Email}>"));
            return (null, $"Multiple users matched '{userName}'. Choose one userId: {options}");
        }

        return (matches[0], null);
    }

    private static JsonElement WrapUserArgs(JsonElement root, string idProp, string nameProp)
    {
        var id = 0;
        if (root.TryGetProperty(idProp, out var idEl))
        {
            if (idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out var n))
            {
                id = n;
            }
            else if (idEl.ValueKind == JsonValueKind.String && int.TryParse(idEl.GetString(), out var parsed))
            {
                id = parsed;
            }
        }

        var name = root.TryGetProperty(nameProp, out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString()
            : null;

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { userId = id, userName = name }));
        return doc.RootElement.Clone();
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
