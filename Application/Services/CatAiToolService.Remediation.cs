using System.Text.Json;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public sealed partial class CatAiToolService
{
    private const int ListGroupsLimit = 100;
    private const int AnalyticsLeadRoleId = 5;
    private const int AccountExecutiveRoleId = 8;

    private static object StartRenewalProjectWizard() => new
    {
        wizard = CatAiDefaults.RenewalProjectWizardKind,
        wizardActive = true,
        title = "Create renewal project",
        instructions =
            "Ask for missing fields one at a time. Resolve client names with search_clients. "
            + "Optional: list_user_groups / create_user_group for the project group. "
            + "When required fields are ready, call create_renewal_project.",
        required = new[]
        {
            new { field = "clientId", label = "Client", how = "Ask for client name; use search_clients / get_client." }
        },
        optional = new[]
        {
            new { field = "projectName", label = "Project name", how = "Defaults to '{Client} Renewal' if omitted." },
            new { field = "renewalDate", label = "Renewal date", how = "YYYY-MM-DD." },
            new { field = "userGroupId", label = "User group", how = "list_user_groups or create_user_group, then assign." }
        },
        message = "Wizard started. Ask the user for the client first.",
    };

    private static object StartUserGroupWizard() => new
    {
        wizard = CatAiDefaults.UserGroupWizardKind,
        wizardActive = true,
        title = "Create user group",
        instructions =
            "Ask for missing fields one at a time. Use search_users and list_user_roles to resolve members. "
            + "When ready, call create_user_group with userGroupName and members[].",
        required = new[]
        {
            new { field = "userGroupName", label = "User group name", how = "Display name for the new group." },
            new
            {
                field = "members",
                label = "At least one member",
                how = "Each needs userId or userName, and roleId or roleName (e.g. Analytics Lead)."
            }
        },
        optional = Array.Empty<object>(),
        message = "Wizard started. Ask the user for the group name first.",
    };

    private static object CancelWizard() => new
    {
        wizardActive = false,
        message = "Wizard cancelled. Resume normal independent Q&A.",
    };

    private async Task<object> ListUserGroupsAsync(string query, CancellationToken cancellationToken)
    {
        var term = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var rows = await _reference.GetTopUserGroupSummariesAsync(term, cancellationToken);
        var matches = rows.Take(ListGroupsLimit).Select(g => new
        {
            userGroupId = g.UGID,
            userGroupNumber = g.UserGroupNumber,
            userGroupName = g.UGName,
            memberCount = g.MemberCount,
        }).ToList();

        return new
        {
            count = matches.Count,
            truncated = rows.Count > ListGroupsLimit,
            matches,
        };
    }

    private async Task<object> ListUserRolesAsync(CancellationToken cancellationToken)
    {
        var roles = await _reference.GetUserRolesAsync(cancellationToken);
        var matches = roles
            .Where(r => r.IsActive)
            .Select(r => new
            {
                roleId = r.UserRoleID,
                roleName = r.UserRoleName,
                description = r.RoleDescription,
            })
            .ToList();

        return new { count = matches.Count, roles = matches };
    }

    private async Task<object> SearchUsersAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new { matches = Array.Empty<object>(), message = "query is required." };
        }

        var rows = await _reference.GetTopUsersAsync(query.Trim(), activeOnly: true, cancellationToken);
        var matches = rows.Take(SearchLimit).Select(u => new
        {
            userId = u.UserID,
            name = string.IsNullOrWhiteSpace(u.FullName)
                ? $"{u.FirstName} {u.LastName}".Trim()
                : u.FullName,
            email = u.Email,
            department = u.Department,
            title = u.Title,
        }).ToList();

        return new { count = matches.Count, matches };
    }

    private async Task<object> CreateUserGroupAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var name = GetString(root, "userGroupName").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return new { error = "userGroupName is required." };
        }

        if (!root.TryGetProperty("members", out var membersEl) || membersEl.ValueKind != JsonValueKind.Array)
        {
            return new { error = "members array is required with at least one member." };
        }

        var memberLines = new List<(int UserId, int RoleId, string UserLabel, string RoleLabel)>();
        foreach (var item in membersEl.EnumerateArray())
        {
            var resolved = await ResolveMemberAsync(item, cancellationToken);
            if (resolved.Error is not null)
            {
                return new { error = resolved.Error };
            }

            memberLines.Add((resolved.UserId, resolved.RoleId, resolved.UserLabel, resolved.RoleLabel));
        }

        if (memberLines.Count == 0)
        {
            return new { error = "Provide at least one member with a user and role." };
        }

        var number = await _admin.GetNextUserGroupNumberAsync(cancellationToken);
        var ugid = await _admin.SaveUserGroupAsync(
            new UserGroup
            {
                UGID = 0,
                UserGroupNumber = number,
                UGName = name,
                StatusID = 1,
            },
            cancellationToken);

        foreach (var m in memberLines)
        {
            await _admin.AddUserGroupMemberAsync(
                new UserGroupMember
                {
                    UGID = ugid,
                    UserID = m.UserId,
                    UserRoleID = m.RoleId,
                    StatusID = 1,
                },
                cancellationToken);
        }

        return new
        {
            success = true,
            userGroupId = ugid,
            userGroupNumber = number,
            userGroupName = name,
            members = memberLines.Select(m => new
            {
                userId = m.UserId,
                userName = m.UserLabel,
                roleId = m.RoleId,
                roleName = m.RoleLabel,
            }).ToList(),
            message = "User group created. Use assign_project_user_group to link it to a project if needed.",
        };
    }

    private async Task<object> AddUserGroupMemberAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var ugid = GetInt(root, "userGroupId");
        if (ugid <= 0)
        {
            return new { error = "userGroupId must be a positive integer." };
        }

        var groups = await _admin.ListUserGroupsAsync(null, includeDeleted: true, cancellationToken);
        var group = groups.FirstOrDefault(g => g.UGID == ugid);
        if (group is null)
        {
            return new { error = $"User group {ugid} was not found." };
        }

        var resolved = await ResolveMemberAsync(root, cancellationToken);
        if (resolved.Error is not null)
        {
            return new { error = resolved.Error };
        }

        var existing = await _admin.ListUserGroupMembersAsync(ugid, cancellationToken);
        var already = existing.FirstOrDefault(m => m.UserID == resolved.UserId);
        if (already is not null)
        {
            if (already.UserRoleID != resolved.RoleId)
            {
                await _admin.UpdateUserGroupMemberAsync(
                    new UserGroupMember
                    {
                        UGID = ugid,
                        UserID = resolved.UserId,
                        UserRoleID = resolved.RoleId,
                    },
                    cancellationToken);
            }
        }
        else
        {
            await _admin.AddUserGroupMemberAsync(
                new UserGroupMember
                {
                    UGID = ugid,
                    UserID = resolved.UserId,
                    UserRoleID = resolved.RoleId,
                    StatusID = 1,
                },
                cancellationToken);
        }

        return new
        {
            success = true,
            userGroupId = ugid,
            userGroupName = group.UGName,
            userId = resolved.UserId,
            userName = resolved.UserLabel,
            roleId = resolved.RoleId,
            roleName = resolved.RoleLabel,
            updatedExisting = already is not null,
            message = already is not null
                ? "Member role updated on the user group."
                : "Member added to the user group.",
        };
    }

    private async Task<object> AssignProjectUserGroupAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var projectId = GetInt(root, "projectId");
        var userGroupId = GetInt(root, "userGroupId");
        if (projectId <= 0 || userGroupId <= 0)
        {
            return new { error = "projectId and userGroupId are required positive integers." };
        }

        var project = await _projects.GetFullAsync(projectId, cancellationToken)
            ?? await _projects.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return new { error = $"Project {projectId} was not found." };
        }

        var groups = await _admin.ListUserGroupsAsync(null, includeDeleted: true, cancellationToken);
        var group = groups.FirstOrDefault(g => g.UGID == userGroupId);
        if (group is null)
        {
            return new { error = $"User group {userGroupId} was not found." };
        }

        if (group.IsDeleted)
        {
            return new
            {
                error = $"User group {userGroupId} is inactive. Call restore_user_group first, then assign.",
            };
        }

        await _projects.AssignUserGroupAsync(projectId, userGroupId, cancellationToken);

        return new
        {
            success = true,
            projectId,
            projectName = project.ProjectName,
            userGroupId,
            userGroupName = group.UGName,
            message = "Project user group was updated.",
        };
    }

    private async Task<object> RestoreUserGroupAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var ugid = GetInt(root, "userGroupId");
        if (ugid <= 0)
        {
            return new { error = "userGroupId must be a positive integer." };
        }

        await _admin.RestoreUserGroupAsync(ugid, cancellationToken);
        var group = await _admin.GetUserGroupAsync(ugid, cancellationToken);
        return new
        {
            success = true,
            userGroupId = ugid,
            userGroupName = group?.UGName,
            message = "User group restored (re-activated).",
        };
    }

    private async Task<object> UpdateProjectRenewalDateAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var projectId = GetInt(root, "projectId");
        var date = GetDate(root, "renewalDate");
        if (projectId <= 0)
        {
            return new { error = "projectId must be a positive integer." };
        }

        if (date is null)
        {
            return new { error = "renewalDate is required as YYYY-MM-DD." };
        }

        var project = await _projects.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return new { error = $"Project {projectId} was not found." };
        }

        await _projects.UpdateRenewalDateAsync(projectId, date.Value, cancellationToken);
        return new
        {
            success = true,
            projectId,
            projectName = project.ProjectName,
            renewalDate = date.Value.ToString("yyyy-MM-dd"),
            message = "Project renewal date was updated.",
        };
    }

    private async Task<object> CreateRenewalProjectAsync(JsonElement root, CancellationToken cancellationToken)
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

        var types = await _reference.GetProjectTypesAsync(cancellationToken);
        var rnwl = types.FirstOrDefault(t =>
            string.Equals((t.PTCode ?? string.Empty).Trim(), "RNWL", StringComparison.OrdinalIgnoreCase));
        if (rnwl is null || rnwl.ProjectTypeID <= 0)
        {
            return new { error = "Renewal project type (RNWL) was not found in the catalog." };
        }

        var projectName = GetString(root, "projectName").Trim();
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = $"{client.ClientName ?? "Client"} Renewal".Trim();
        }

        var renewalDate = GetDate(root, "renewalDate");
        var userGroupId = GetInt(root, "userGroupId");

        var projectNumber = await _projects.GetNextProjectNumberPreviewAsync(cancellationToken);
        var project = new Project
        {
            ProjectID = 0,
            ProjectNumber = projectNumber,
            ProjectName = projectName,
            ClientID = clientId,
            ProjectTypeID = rnwl.ProjectTypeID,
            RenewalDate = renewalDate,
            StatusID = 1,
            UGID = userGroupId > 0 ? userGroupId : 0,
            IsOnline = false,
        };

        var projectId = await _projects.SaveProjectAndReplaceServicesAsync(
            project,
            [],
            null,
            null,
            cancellationToken);

        return new
        {
            success = true,
            projectId,
            projectNumber,
            projectName,
            clientId,
            clientName = client.ClientName,
            projectTypeCode = "RNWL",
            renewalDate = renewalDate?.ToString("yyyy-MM-dd"),
            userGroupId = userGroupId > 0 ? userGroupId : (int?)null,
            message = "Renewal project created for the client.",
        };
    }

    private async Task<(int UserId, int RoleId, string UserLabel, string RoleLabel, string? Error)> ResolveMemberAsync(
        JsonElement item,
        CancellationToken cancellationToken)
    {
        var userId = item.ValueKind == JsonValueKind.Object ? GetInt(item, "userId") : 0;
        var userName = item.ValueKind == JsonValueKind.Object ? GetString(item, "userName") : string.Empty;
        var roleId = item.ValueKind == JsonValueKind.Object ? GetInt(item, "roleId") : 0;
        var roleName = item.ValueKind == JsonValueKind.Object ? GetString(item, "roleName") : string.Empty;

        if (userId <= 0)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                return (0, 0, "", "", "Each member needs userId or userName.");
            }

            var users = await _reference.GetTopUsersAsync(userName.Trim(), activeOnly: true, cancellationToken);
            var matches = users
                .Where(u =>
                {
                    var full = (u.FullName ?? $"{u.FirstName} {u.LastName}").Trim();
                    return full.Contains(userName.Trim(), StringComparison.OrdinalIgnoreCase)
                        || string.Equals(full, userName.Trim(), StringComparison.OrdinalIgnoreCase)
                        || (u.Email ?? string.Empty).Contains(userName.Trim(), StringComparison.OrdinalIgnoreCase);
                })
                .Take(5)
                .ToList();

            if (matches.Count == 0)
            {
                return (0, 0, "", "", $"No active user matched '{userName}'.");
            }

            if (matches.Count > 1)
            {
                var options = string.Join("; ", matches.Select(m =>
                    $"{m.UserID}:{(m.FullName ?? $"{m.FirstName} {m.LastName}").Trim()}"));
                return (0, 0, "", "", $"Multiple users matched '{userName}'. Choose one userId: {options}");
            }

            userId = matches[0].UserID;
            userName = (matches[0].FullName ?? $"{matches[0].FirstName} {matches[0].LastName}").Trim();
        }
        else
        {
            var info = await _admin.GetUserContactInfoByIdsAsync([userId], cancellationToken);
            var hit = info.FirstOrDefault();
            if (hit is null)
            {
                return (0, 0, "", "", $"User {userId} was not found or is inactive.");
            }

            userName = hit.FullName ?? userName;
        }

        if (roleId <= 0)
        {
            if (string.IsNullOrWhiteSpace(roleName))
            {
                return (0, 0, "", "", "Each member needs roleId or roleName (e.g. Analytics Lead, Account Executive).");
            }

            roleId = await ResolveRoleIdAsync(roleName, cancellationToken);
            if (roleId <= 0)
            {
                return (0, 0, "", "", $"Unknown roleName '{roleName}'. Call list_user_roles.");
            }
        }

        var roles = await _reference.GetUserRolesAsync(cancellationToken);
        var role = roles.FirstOrDefault(r => r.UserRoleID == roleId);
        if (role is null)
        {
            return (0, 0, "", "", $"Unknown roleId {roleId}.");
        }

        return (userId, roleId, userName, role.UserRoleName ?? roleName, null);
    }

    private async Task<int> ResolveRoleIdAsync(string roleName, CancellationToken cancellationToken)
    {
        var normalized = roleName.Trim().ToLowerInvariant();
        if (normalized is "al" or "analytics lead" or "analyticslead")
        {
            return AnalyticsLeadRoleId;
        }

        if (normalized is "ae" or "account executive" or "accountexecutive")
        {
            return AccountExecutiveRoleId;
        }

        var roles = await _reference.GetUserRolesAsync(cancellationToken);
        var match = roles.FirstOrDefault(r =>
            string.Equals((r.UserRoleName ?? string.Empty).Trim(), roleName.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.UserRoleID ?? 0;
    }
}
