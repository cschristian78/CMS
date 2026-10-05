using System.Text.Json;
using ClientManagementSystem.Data;
using ClientManagementSystem.Models;

namespace ClientManagementSystem.Services;

public interface ICatAiToolService
{
    IReadOnlyList<object> GetToolDefinitions(CatAiActorContext? actor = null);

    Task<string> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CatAiActorContext? actor = null,
        CancellationToken cancellationToken = default);
}

public sealed partial class CatAiToolService : ICatAiToolService
{
    private const int SearchLimit = 25;
    private const int AuditRowLimit = 40;
    private readonly IAdminRepository _admin;
    private readonly IProjectRepository _projects;
    private readonly IDataAuditRepository _dataAudit;
    private readonly IReferenceDataRepository _reference;
    private readonly IDbExecutor _db;
    private readonly IProjectReportService _projectReports;

    public CatAiToolService(
        IAdminRepository admin,
        IProjectRepository projects,
        IDataAuditRepository dataAudit,
        IReferenceDataRepository reference,
        IDbExecutor db,
        IProjectReportService projectReports)
    {
        _admin = admin;
        _projects = projects;
        _dataAudit = dataAudit;
        _reference = reference;
        _db = db;
        _projectReports = projectReports;
    }

    public IReadOnlyList<object> GetToolDefinitions(CatAiActorContext? actor = null)
    {
        var tools = new List<ToolDef>
        {
            Tool("search_clients", "Search active CMS clients by name or short name.", new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Client name or short name fragment." }
                },
                required = new[] { "query" }
            }),
            Tool("get_client", "Get one client by ClientID, including Analytics Lead and Account Executive.", new
            {
                type = "object",
                properties = new
                {
                    clientId = new { type = "integer", description = "CMS ClientID." }
                },
                required = new[] { "clientId" }
            }),
            Tool("search_projects", "Search active projects by name, number, or client name.", new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Project name, number, or client name fragment." }
                },
                required = new[] { "query" }
            }),
            Tool("get_project", "Get one project by ProjectID with type, renewal, user group, and revenue presence.", new
            {
                type = "object",
                properties = new
                {
                    projectId = new { type = "integer", description = "CMS ProjectID." }
                },
                required = new[] { "projectId" }
            }),
            Tool(
                "list_client_projects",
                "List all projects for a given ClientID, including ProjectID and status. Defaults to active projects only.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        clientId = new { type = "integer", description = "CMS ClientID." },
                        includeInactive = new
                        {
                            type = "boolean",
                            description = "When true, also include soft-deleted / inactive projects. Default false."
                        }
                    },
                    required = new[] { "clientId" }
                }),
            Tool(
                "list_all_projects",
                "List CMS projects. By default returns ALL projects regardless of status (active and inactive, online and offline). "
                    + "Optional filters: activeFilter (all|active|inactive) and onlineFilter (all|online|offline). "
                    + "Use when the user asks for a list of all projects, then filter if they ask for active/inactive or online/offline. "
                    + "When presenting results, always include ProjectID, ProjectNumber, isActive, and isOnline for each project.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        activeFilter = new
                        {
                            type = "string",
                            description = "all (default) = no filter; active = IsActive true only; inactive = IsActive false only."
                        },
                        onlineFilter = new
                        {
                            type = "string",
                            description = "all (default) = no filter; online = IsOnline true only; offline = IsOnline false only."
                        }
                    }
                }),
            Tool(
                "list_projects_by_renewal_date",
                "List projects whose RenewalDate matches a date. Use for questions like 'List all projects that renew on 1/1'. "
                    + "If only month/day is given (e.g. 1/1 or January 1), matches every year. If a full date with year is given, matches that exact date. "
                    + "When presenting results to the user, always include each project's ProjectID and ProjectNumber (do not omit them).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        date = new
                        {
                            type = "string",
                            description = "Renewal date. Examples: 1/1, 01/01, January 1, 2026-01-01, 1/1/2026."
                        },
                        includeInactive = new
                        {
                            type = "boolean",
                            description = "When true, also include inactive projects. Default false."
                        }
                    },
                    required = new[] { "date" }
                }),
            Tool("search_user_groups", "Search user groups by name or group number.", new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "User group name or number fragment." }
                },
                required = new[] { "query" }
            }),
            Tool("get_user_group", "Get a user group and its members/roles by UserGroupID.", new
            {
                type = "object",
                properties = new
                {
                    userGroupId = new { type = "integer", description = "CMS UserGroupID (UGID)." }
                },
                required = new[] { "userGroupId" }
            }),
            Tool("list_data_audit_checks", "List every Admin Data Audit check available to CatAI (ids, titles, tool names).", new
            {
                type = "object",
                properties = new { },
            }),
            Tool("get_data_audit_summary", "Get Data Audit section finding totals (counts only).", new
            {
                type = "object",
                properties = new { },
            }),
            Tool(
                "get_project_revenue_summary",
                "List all revenue history entries for a project and summarize whether revenue went up or down over time and by how much (net change, percent, and step-by-step deltas).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        projectId = new { type = "integer", description = "CMS ProjectID." }
                    },
                    required = new[] { "projectId" }
                }),
            Tool(
                "update_project_revenue",
                "Add a new revenue history entry for a project (append-only). This becomes the project's latest revenue. Use only when the user explicitly asks to set or update project revenue. Resolves Data Audit: projects-missing-revenue.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        projectId = new { type = "integer", description = "CMS ProjectID." },
                        revenue = new { type = "number", description = "Revenue amount (must be greater than 0)." },
                        effectiveDate = new
                        {
                            type = "string",
                            description = "Effective date as YYYY-MM-DD. Defaults to today (UTC) when omitted."
                        },
                        changeReason = new
                        {
                            type = "string",
                            description = "Optional short reason for the revenue change (max 255 chars)."
                        }
                    },
                    required = new[] { "projectId", "revenue" }
                }),
            Tool("list_user_groups", "List active user groups (id, number, name, member count). Use when resolving projects/users missing a group.", new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Optional name/number filter. Omit to list all active groups (capped)." }
                }
            }),
            Tool("list_user_roles", "List CMS user roles (id + name) for assigning members to user groups.", new
            {
                type = "object",
                properties = new { },
            }),
            Tool("search_users", "Search active users by name or email to resolve userId for group membership.", new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Name or email fragment." }
                },
                required = new[] { "query" }
            }),
            Tool(
                "create_user_group",
                "Create a user group with a name and at least one member (user + role). Resolves audits needing a new group, empty groups, missing AE/AL, or CAT users without a group.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userGroupName = new { type = "string", description = "Display name for the new user group." },
                        members = new
                        {
                            type = "array",
                            description = "At least one member. Provide userId or userName, and roleId or roleName (e.g. Analytics Lead, Account Executive).",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    userId = new { type = "integer" },
                                    userName = new { type = "string" },
                                    roleId = new { type = "integer" },
                                    roleName = new { type = "string" }
                                }
                            }
                        }
                    },
                    required = new[] { "userGroupName", "members" }
                }),
            Tool(
                "add_user_group_member",
                "Add or update a member on an existing user group with a role. Use for empty groups, missing AE/AL, or CAT users without membership.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userGroupId = new { type = "integer", description = "UGID." },
                        userId = new { type = "integer" },
                        userName = new { type = "string", description = "Used when userId is omitted." },
                        roleId = new { type = "integer" },
                        roleName = new { type = "string", description = "Used when roleId is omitted (e.g. Analytics Lead)." }
                    },
                    required = new[] { "userGroupId" }
                }),
            Tool(
                "assign_project_user_group",
                "Assign an existing user group to a project's active project ref. Resolves projects-missing-user-group and projects-inactive-or-missing-user-group; also links a group that has no project.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        projectId = new { type = "integer" },
                        userGroupId = new { type = "integer" }
                    },
                    required = new[] { "projectId", "userGroupId" }
                }),
            Tool(
                "restore_user_group",
                "Re-activate a soft-deleted / inactive user group. Use when a project points at an inactive group.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userGroupId = new { type = "integer" }
                    },
                    required = new[] { "userGroupId" }
                }),
            Tool(
                "get_user",
                "Get one user by userId or name/email, including department, user type, active flag, and all user-group memberships with roles.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userId = new { type = "integer" },
                        userName = new { type = "string", description = "Name or email when userId is omitted." }
                    }
                }),
            Tool(
                "list_groups_for_user",
                "List every user group a person belongs to (with role). Use for questions like 'How many user groups is Jason Chou a part of?'. Defaults to active groups only.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userId = new { type = "integer" },
                        userName = new { type = "string", description = "Name or email when userId is omitted." },
                        includeInactiveGroups = new
                        {
                            type = "boolean",
                            description = "When true, include inactive/soft-deleted groups. Default false."
                        }
                    }
                }),
            Tool(
                "list_projects_for_user_group",
                "List projects whose active project ref is assigned to a user group.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userGroupId = new { type = "integer" },
                        includeInactiveProjects = new
                        {
                            type = "boolean",
                            description = "When true, include inactive projects. Default false."
                        }
                    },
                    required = new[] { "userGroupId" }
                }),
            Tool(
                "list_cat_users",
                "List users whose Department is CAT. Optionally restrict to CAT users who are not in any active user group.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        withoutGroupOnly = new
                        {
                            type = "boolean",
                            description = "When true, only CAT users with no active group membership."
                        },
                        includeInactive = new
                        {
                            type = "boolean",
                            description = "When true, include inactive users. Default false."
                        }
                    }
                }),
            Tool(
                "get_project_cat_cc",
                "List CAT-department members of a project's user group and their emails (Outlook CC preview for project reports).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        projectId = new { type = "integer" }
                    },
                    required = new[] { "projectId" }
                }),
            Tool(
                "remove_user_group_member",
                "Remove a user from a user group.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userGroupId = new { type = "integer" },
                        userId = new { type = "integer" },
                        userName = new { type = "string", description = "Used when userId is omitted." }
                    },
                    required = new[] { "userGroupId" }
                }),
            Tool(
                "update_user_group",
                "Rename a user group and/or change its user group number.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userGroupId = new { type = "integer" },
                        userGroupName = new { type = "string" },
                        userGroupNumber = new { type = "string" }
                    },
                    required = new[] { "userGroupId" }
                }),
            Tool(
                "soft_delete_user_group",
                "Deactivate (soft-delete) a user group. Refuses when active projects still use it unless force=true.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userGroupId = new { type = "integer" },
                        force = new
                        {
                            type = "boolean",
                            description = "When true, soft-delete even if projects still reference the group."
                        }
                    },
                    required = new[] { "userGroupId" }
                }),
            Tool(
                "update_client_leads",
                "Set or clear a client's Account Executive and/or Analytics Lead (client-level FKs, not group roles).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        clientId = new { type = "integer" },
                        accountExecutiveUserId = new { type = "integer" },
                        accountExecutiveUserName = new { type = "string" },
                        clearAccountExecutive = new { type = "boolean" },
                        analyticsLeadUserId = new { type = "integer" },
                        analyticsLeadUserName = new { type = "string" },
                        clearAnalyticsLead = new { type = "boolean" }
                    },
                    required = new[] { "clientId" }
                }),
            Tool(
                "ensure_group_analytics_lead",
                "Ensure a user group has the given member with the Analytics Lead role (UserRoleID 5). Adds or updates membership.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userGroupId = new { type = "integer" },
                        userId = new { type = "integer" },
                        userName = new { type = "string" }
                    },
                    required = new[] { "userGroupId" }
                }),
            Tool(
                "ensure_group_account_executive",
                "Ensure a user group has the given member with the Account Executive role (UserRoleID 8). Adds or updates membership.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userGroupId = new { type = "integer" },
                        userId = new { type = "integer" },
                        userName = new { type = "string" }
                    },
                    required = new[] { "userGroupId" }
                }),
            Tool(
                "create_user",
                "Create a CMS user (firstName, lastName, email required). Optional: title, phone, company, department, userType (Basic|External|Administrator|Manager|Analyst).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        firstName = new { type = "string" },
                        lastName = new { type = "string" },
                        email = new { type = "string" },
                        title = new { type = "string" },
                        phone = new { type = "string" },
                        company = new { type = "string" },
                        department = new { type = "string", description = "e.g. CAT" },
                        userType = new { type = "string", description = "Basic (default), External, Administrator, Manager, or Analyst." }
                    },
                    required = new[] { "firstName", "lastName", "email" }
                }),
            Tool(
                "update_user",
                "Update an existing user's profile fields (name, email, title, phone, company, department, userType).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userId = new { type = "integer" },
                        userName = new { type = "string", description = "Used to find the user when userId is omitted." },
                        firstName = new { type = "string" },
                        lastName = new { type = "string" },
                        email = new { type = "string" },
                        title = new { type = "string" },
                        phone = new { type = "string" },
                        company = new { type = "string" },
                        department = new { type = "string" },
                        userType = new { type = "string" }
                    }
                }),
            Tool(
                "deactivate_user",
                "Soft-delete / deactivate a user.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userId = new { type = "integer" },
                        userName = new { type = "string" }
                    }
                }),
            Tool(
                "restore_user",
                "Re-activate a soft-deleted / inactive user.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        userId = new { type = "integer" },
                        userName = new { type = "string" }
                    }
                }),
            Tool(
                "unassign_project_user_group",
                "Clear the user group on a project's active project ref.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        projectId = new { type = "integer" }
                    },
                    required = new[] { "projectId" }
                }),
            Tool(
                "list_users_by_role",
                "List users who hold a given role on any user group (e.g. all Analytics Leads or Account Executives across groups).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        roleId = new { type = "integer" },
                        roleName = new { type = "string", description = "e.g. Analytics Lead, Account Executive." },
                        includeInactiveGroups = new
                        {
                            type = "boolean",
                            description = "When true, include assignments on inactive groups. Default false."
                        }
                    }
                }),
            Tool(
                "update_project_renewal_date",
                "Set a project's RenewalDate. Resolves Data Audit: projects-missing-renewal-date.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        projectId = new { type = "integer" },
                        renewalDate = new { type = "string", description = "YYYY-MM-DD" }
                    },
                    required = new[] { "projectId", "renewalDate" }
                }),
            Tool(
                "generate_project_report",
                "Generate the PDF project report for a project, save it to project data (Azure Blob / CorrespondenceData), "
                    + "and return downloadUrl plus linkLabel. Use when the user asks to create, generate, or export a project report. "
                    + "In your reply, include a markdown link using linkLabel as the visible text and downloadUrl as the target, "
                    + "exactly like: [ClientName_ProjectName_ProjectReport.pdf](https://...).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        projectId = new { type = "integer", description = "CMS ProjectID." },
                        fileName = new
                        {
                            type = "string",
                            description = "Optional PDF file name. Defaults to the standard ClientShort_Renewal_Type_Name_ProjectReport.pdf pattern."
                        }
                    },
                    required = new[] { "projectId" }
                }),
            Tool(
                "create_renewal_project",
                "Create an active Renewal (RNWL) project for a client. Also covers Data Audit: clients-missing-project. Optionally assign a user group and renewal date. Ends the renewal-project wizard on success.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        clientId = new { type = "integer" },
                        projectName = new { type = "string", description = "Optional; defaults to '{Client} Renewal'." },
                        renewalDate = new { type = "string", description = "Optional YYYY-MM-DD." },
                        userGroupId = new { type = "integer", description = "Optional UserGroupID to assign." }
                    },
                    required = new[] { "clientId" }
                }),
            Tool(
                "start_renewal_project_wizard",
                "Begin a multi-step wizard to create a renewal project. Returns the required/optional fields to collect. Call this when the user wants to create a renewal project before asking for details.",
                new { type = "object", properties = new { } }),
            Tool(
                "start_user_group_wizard",
                "Begin a multi-step wizard to create a user group. Returns the required fields (name + at least one member with role). Call this when the user wants to create a user group before asking for details.",
                new { type = "object", properties = new { } }),
            Tool(
                "cancel_wizard",
                "Cancel the active create wizard (renewal project or user group) without saving.",
                new { type = "object", properties = new { } }),
        };

        foreach (var check in _dataAudit.ListCheckCatalog())
        {
            tools.Add(Tool(
                check.ToolName,
                $"Run Data Audit check '{check.CheckId}': {check.Title}. {check.Description} Returns finding count and up to {AuditRowLimit} sample rows.",
                new
                {
                    type = "object",
                    properties = new { },
                }));
        }

        IEnumerable<ToolDef> visible = tools;
        if (actor is not null && !actor.IsAdministrator)
        {
            visible = tools.Where(t => CatAiToolPermissions.CanSeeTool(actor, t.Name));
        }

        return visible.Select(t => t.Definition).ToList();
    }

    public async Task<string> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CatAiActorContext? actor = null,
        CancellationToken cancellationToken = default)
    {
        using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        var root = args.RootElement;

        try
        {
            actor ??= new CatAiActorContext { UserId = 0, UserType = "Basic" };
            var denied = CatAiToolPermissions.AuthorizeExecution(actor, toolName, root);
            if (denied is not null)
            {
                return JsonSerializer.Serialize(new { error = denied });
            }

            if (CatAiToolPermissions.RequiresAssignedProjectCheck(actor, toolName))
            {
                var projectId = CatAiToolPermissions.ReadProjectId(root);
                if (!await IsUserAssignedToProjectAsync(actor.UserId, projectId, cancellationToken))
                {
                    return JsonSerializer.Serialize(new
                    {
                        error =
                            $"You are not assigned to project {projectId}. "
                            + "Analysts may only update projects whose user group includes them.",
                    });
                }
            }

            var auditCheckId = ResolveAuditCheckId(toolName);
            object payload = toolName switch
            {
                "search_clients" => await SearchClientsAsync(GetString(root, "query"), cancellationToken),
                "get_client" => await GetClientAsync(GetInt(root, "clientId"), cancellationToken),
                "search_projects" => await SearchProjectsAsync(GetString(root, "query"), cancellationToken),
                "get_project" => await GetProjectAsync(GetInt(root, "projectId"), cancellationToken),
                "list_client_projects" => await ListClientProjectsAsync(
                    GetInt(root, "clientId"),
                    GetBool(root, "includeInactive"),
                    cancellationToken),
                "list_all_projects" => await ListAllProjectsAsync(root, cancellationToken),
                "list_projects_by_renewal_date" => await ListProjectsByRenewalDateAsync(root, cancellationToken),
                "search_user_groups" => await SearchUserGroupsAsync(GetString(root, "query"), cancellationToken),
                "get_user_group" => await GetUserGroupAsync(GetInt(root, "userGroupId"), cancellationToken),
                "list_data_audit_checks" => ListDataAuditChecks(),
                "get_data_audit_summary" => await GetDataAuditSummaryAsync(cancellationToken),
                "get_project_revenue_summary" => await GetProjectRevenueSummaryAsync(
                    GetInt(root, "projectId"),
                    cancellationToken),
                "update_project_revenue" => await UpdateProjectRevenueAsync(root, cancellationToken),
                "list_user_groups" => await ListUserGroupsAsync(GetString(root, "query"), cancellationToken),
                "list_user_roles" => await ListUserRolesAsync(cancellationToken),
                "search_users" => await SearchUsersAsync(GetString(root, "query"), cancellationToken),
                "create_user_group" => await CreateUserGroupAsync(root, cancellationToken),
                "add_user_group_member" => await AddUserGroupMemberAsync(root, cancellationToken),
                "assign_project_user_group" => await AssignProjectUserGroupAsync(root, cancellationToken),
                "restore_user_group" => await RestoreUserGroupAsync(root, cancellationToken),
                "get_user" => await GetUserAsync(root, cancellationToken),
                "list_groups_for_user" => await ListGroupsForUserAsync(root, cancellationToken),
                "list_projects_for_user_group" => await ListProjectsForUserGroupAsync(root, cancellationToken),
                "list_cat_users" => await ListCatUsersAsync(root, cancellationToken),
                "get_project_cat_cc" => await GetProjectCatCcAsync(root, cancellationToken),
                "remove_user_group_member" => await RemoveUserGroupMemberAsync(root, cancellationToken),
                "update_user_group" => await UpdateUserGroupAsync(root, cancellationToken),
                "soft_delete_user_group" => await SoftDeleteUserGroupToolAsync(root, cancellationToken),
                "update_client_leads" => await UpdateClientLeadsAsync(root, cancellationToken),
                "ensure_group_analytics_lead" => await EnsureGroupAnalyticsLeadAsync(root, cancellationToken),
                "ensure_group_account_executive" => await EnsureGroupAccountExecutiveAsync(root, cancellationToken),
                "create_user" => await CreateUserAsync(root, cancellationToken),
                "update_user" => await UpdateUserAsync(root, cancellationToken),
                "deactivate_user" => await DeactivateUserAsync(root, cancellationToken),
                "restore_user" => await RestoreUserAsync(root, cancellationToken),
                "unassign_project_user_group" => await UnassignProjectUserGroupAsync(root, cancellationToken),
                "list_users_by_role" => await ListUsersByRoleAsync(root, cancellationToken),
                "update_project_renewal_date" => await UpdateProjectRenewalDateAsync(root, cancellationToken),
                "generate_project_report" => await GenerateProjectReportAsync(root, cancellationToken),
                "create_renewal_project" => await CreateRenewalProjectAsync(root, cancellationToken),
                "start_renewal_project_wizard" => StartRenewalProjectWizard(),
                "start_user_group_wizard" => StartUserGroupWizard(),
                "cancel_wizard" => CancelWizard(),
                _ when auditCheckId is not null => await RunDataAuditCheckAsync(auditCheckId, cancellationToken),
                _ => new { error = $"Unknown tool '{toolName}'." }
            };

            return JsonSerializer.Serialize(payload);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private async Task<object> SearchClientsAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new { matches = Array.Empty<object>(), message = "query is required." };
        }

        var rows = await _admin.ListClientsAsync(query.Trim(), includeInactive: false, cancellationToken);
        var matches = rows.Take(SearchLimit).Select(c => new
        {
            clientId = c.ClientID,
            clientName = c.ClientName,
            clientShortName = c.ClientShortName,
            clientStatus = c.ClientStatus,
            analyticsLeadName = c.AnalyticsLeadName,
            accountExecutiveName = c.AccountExecutiveName,
        }).ToList();

        return new { count = matches.Count, matches };
    }

    private async Task<object> GetClientAsync(int clientId, CancellationToken cancellationToken)
    {
        if (clientId <= 0)
        {
            return new { error = "clientId must be positive." };
        }

        var rows = await _admin.ListClientsAsync(null, includeInactive: true, cancellationToken);
        var c = rows.FirstOrDefault(x => x.ClientID == clientId);
        if (c is null)
        {
            return new { error = $"Client {clientId} was not found." };
        }

        return new
        {
            clientId = c.ClientID,
            clientName = c.ClientName,
            clientShortName = c.ClientShortName,
            clientDescription = c.ClientDescription,
            clientStatus = c.ClientStatus,
            isActive = c.IsActive,
            accountExecutiveUserId = c.AccountExecutiveUserID,
            accountExecutiveName = string.IsNullOrWhiteSpace(c.AccountExecutiveName) ? null : c.AccountExecutiveName,
            analyticsLeadUserId = c.AnalyticsLeadUserID,
            analyticsLeadName = string.IsNullOrWhiteSpace(c.AnalyticsLeadName) ? null : c.AnalyticsLeadName,
            businessUnitName = c.BusinessUnitName,
            businessClassName = c.BusinessClassName,
            clientTypeName = c.ClientTypeName,
        };
    }

    private async Task<object> SearchProjectsAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new { matches = Array.Empty<object>(), message = "query is required." };
        }

        const string sql = """
            SELECT TOP (25)
                p.ProjectID,
                p.ProjectNumber,
                p.ProjectName,
                c.ClientName,
                pt.ProjectTypeCode,
                p.RenewalDate,
                pr.UserGroupID
              FROM dbo.tProject AS p
             INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectID = p.ProjectID AND pr.IsActive = 1
              LEFT JOIN dbo.tProjectType AS pt ON pt.ProjectTypeID = pr.ProjectTypeID
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
             WHERE p.IsActive = 1
               AND (
                    p.ProjectName LIKE @Pattern
                 OR p.ProjectNumber LIKE @Pattern
                 OR c.ClientName LIKE @Pattern
                 OR c.ClientShortName LIKE @Pattern
                   )
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
                projectTypeCode = r.IsDBNull(4) ? null : r.GetString(4),
                renewalDate = r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5),
                userGroupId = r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
            },
            [SqlParameterFactory.NVarChar("@Pattern", 152, $"%{query.Trim()}%")],
            cancellationToken);

        return new { count = rows.Count, matches = rows };
    }

    private async Task<object> GetProjectAsync(int projectId, CancellationToken cancellationToken)
    {
        if (projectId <= 0)
        {
            return new { error = "projectId must be positive." };
        }

        var project = await _projects.GetFullAsync(projectId, cancellationToken);
        if (project is null)
        {
            return new { error = $"Project {projectId} was not found." };
        }

        var revenue = await _projects.GetRevenueHistoryAsync(projectId, cancellationToken);
        var hasRevenue = revenue.Count > 0;

        return new
        {
            projectId = project.ProjectID,
            projectNumber = project.ProjectNumber,
            projectName = project.ProjectName,
            clientName = project.ClientName,
            projectType = project.PTDescription ?? project.PTCode,
            projectTypeCode = project.PTCode,
            renewalDate = project.RenewalDate,
            dueDate = project.DueDate,
            isDeleted = project.IsDeleted,
            isOnline = project.IsOnline,
            userGroupId = project.UGID > 0 ? project.UGID : (int?)null,
            userGroupName = project.UGName,
            latestRevenue = project.LatestRevenue,
            hasRevenueHistory = hasRevenue,
        };
    }

    private async Task<object> ListClientProjectsAsync(int clientId, bool includeInactive, CancellationToken cancellationToken)
    {
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

        const string sql = """
            SELECT
                p.ProjectID,
                p.ProjectNumber,
                p.ProjectName,
                st.StatusCode,
                st.StatusDescription,
                IsActive = CAST(CASE WHEN p.IsActive = 1 THEN 1 ELSE 0 END AS BIT),
                IsOnline = CAST(ISNULL(p.IsOnline, 0) AS BIT),
                pt.ProjectTypeCode
              FROM dbo.tProject AS p
             INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectID = p.ProjectID AND pr.IsActive = 1
             INNER JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tStatus AS st ON st.StatusID = pr.StatusID
              LEFT JOIN dbo.tProjectType AS pt ON pt.ProjectTypeID = pr.ProjectTypeID
             WHERE cr.ClientID = @ClientID
               AND (@IncludeInactive = 1 OR p.IsActive = 1)
             ORDER BY p.ProjectName, p.ProjectNumber, p.ProjectID;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            r => new
            {
                projectId = r.GetInt32(0),
                projectNumber = r.IsDBNull(1) ? null : r.GetString(1),
                projectName = r.IsDBNull(2) ? null : r.GetString(2),
                statusCode = r.IsDBNull(3) ? null : r.GetString(3),
                status = r.IsDBNull(4)
                    ? (r.IsDBNull(3) ? null : r.GetString(3))
                    : r.GetString(4),
                isActive = !r.IsDBNull(5) && r.GetBoolean(5),
                isOnline = !r.IsDBNull(6) && r.GetBoolean(6),
                projectTypeCode = r.IsDBNull(7) ? null : r.GetString(7),
            },
            [
                SqlParameterFactory.Int("@ClientID", clientId),
                SqlParameterFactory.Bit("@IncludeInactive", includeInactive),
            ],
            cancellationToken);

        return new
        {
            clientId = client.ClientID,
            clientName = client.ClientName,
            includeInactive,
            count = rows.Count,
            projects = rows,
        };
    }

    private async Task<object> ListAllProjectsAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (!TryParseActiveFilter(GetString(root, "activeFilter"), out var filterActive, out var activeLabel, out var activeError))
        {
            return new { error = activeError };
        }

        if (!TryParseOnlineFilter(GetString(root, "onlineFilter"), out var filterOnline, out var onlineLabel, out var onlineError))
        {
            return new { error = onlineError };
        }

        const int rowLimit = 5000;

        const string sql = """
            SELECT TOP (5000)
                p.ProjectID,
                p.ProjectNumber,
                p.ProjectName,
                c.ClientName,
                pt.ProjectTypeCode,
                st.StatusCode,
                st.StatusDescription,
                IsActive = CAST(CASE WHEN p.IsActive = 1 THEN 1 ELSE 0 END AS BIT),
                IsOnline = CAST(ISNULL(p.IsOnline, 0) AS BIT),
                p.RenewalDate
              FROM dbo.tProject AS p
             INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectID = p.ProjectID AND pr.IsActive = 1
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
              LEFT JOIN dbo.tProjectType AS pt ON pt.ProjectTypeID = pr.ProjectTypeID
              LEFT JOIN dbo.tStatus AS st ON st.StatusID = pr.StatusID
             WHERE (@FilterActive = -1 OR p.IsActive = @FilterActive)
               AND (@FilterOnline = -1 OR CAST(ISNULL(p.IsOnline, 0) AS INT) = @FilterOnline)
             ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            r =>
            {
                var projectId = r.GetInt32(0);
                var projectNumber = r.IsDBNull(1) ? null : r.GetString(1);
                var projectName = r.IsDBNull(2) ? null : r.GetString(2);
                var clientName = r.IsDBNull(3) ? null : r.GetString(3);
                var isActive = !r.IsDBNull(7) && r.GetBoolean(7);
                var isOnline = !r.IsDBNull(8) && r.GetBoolean(8);
                return new
                {
                    projectId,
                    projectNumber,
                    projectName,
                    clientName,
                    projectTypeCode = r.IsDBNull(4) ? null : r.GetString(4),
                    statusCode = r.IsDBNull(5) ? null : r.GetString(5),
                    status = r.IsDBNull(6)
                        ? (r.IsDBNull(5) ? null : r.GetString(5))
                        : r.GetString(6),
                    isActive,
                    isOnline,
                    activeLabel = isActive ? "active" : "inactive",
                    onlineLabel = isOnline ? "online" : "offline",
                    renewalDate = r.IsDBNull(9) ? null : r.GetDateTime(9).ToString("yyyy-MM-dd"),
                    listLine =
                        $"{clientName ?? "(no client)"} | ProjectID {projectId} | ProjectNumber {projectNumber ?? "(none)"} | {projectName ?? "(unnamed)"} | {(isActive ? "active" : "inactive")} | {(isOnline ? "online" : "offline")}",
                };
            },
            [
                SqlParameterFactory.Int("@FilterActive", filterActive),
                SqlParameterFactory.Int("@FilterOnline", filterOnline),
            ],
            cancellationToken);

        return new
        {
            activeFilter = activeLabel,
            onlineFilter = onlineLabel,
            count = rows.Count,
            truncated = rows.Count >= rowLimit,
            projects = rows,
            presentationHint =
                "Include ProjectID, ProjectNumber, active/inactive, and online/offline for every project when listing to the user.",
            message = $"Listed {rows.Count} project(s) (activeFilter={activeLabel}, onlineFilter={onlineLabel}).",
        };
    }

    /// <summary>Parses all|active|inactive (default all). Returns -1 for no filter, else 0/1 for SQL.</summary>
    private static bool TryParseActiveFilter(string raw, out int filterValue, out string label, out string? error)
    {
        filterValue = -1;
        label = "all";
        error = null;
        var t = (raw ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(t) || t.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (t.Equals("active", StringComparison.OrdinalIgnoreCase)
            || t.Equals("true", StringComparison.OrdinalIgnoreCase)
            || t.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            filterValue = 1;
            label = "active";
            return true;
        }

        if (t.Equals("inactive", StringComparison.OrdinalIgnoreCase)
            || t.Equals("false", StringComparison.OrdinalIgnoreCase)
            || t.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            filterValue = 0;
            label = "inactive";
            return true;
        }

        error = "activeFilter must be all, active, or inactive.";
        return false;
    }

    /// <summary>Parses all|online|offline (default all). Returns -1 for no filter, else 0/1 for SQL.</summary>
    private static bool TryParseOnlineFilter(string raw, out int filterValue, out string label, out string? error)
    {
        filterValue = -1;
        label = "all";
        error = null;
        var t = (raw ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(t) || t.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (t.Equals("online", StringComparison.OrdinalIgnoreCase)
            || t.Equals("true", StringComparison.OrdinalIgnoreCase)
            || t.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            filterValue = 1;
            label = "online";
            return true;
        }

        if (t.Equals("offline", StringComparison.OrdinalIgnoreCase)
            || t.Equals("false", StringComparison.OrdinalIgnoreCase)
            || t.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            filterValue = 0;
            label = "offline";
            return true;
        }

        error = "onlineFilter must be all, online, or offline.";
        return false;
    }

    private async Task<object> ListProjectsByRenewalDateAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var rawDate = GetString(root, "date").Trim();
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            return new { error = "date is required (e.g. 1/1, January 1, or 2026-01-01)." };
        }

        if (!TryParseRenewalDateFilter(rawDate, out var month, out var day, out var exactDate, out var matchMode, out var parseError))
        {
            return new { error = parseError ?? $"Could not parse date '{rawDate}'." };
        }

        var includeInactive = GetBool(root, "includeInactive");
        const int rowLimit = 200;

        const string sql = """
            SELECT TOP (200)
                p.ProjectID,
                p.ProjectNumber,
                p.ProjectName,
                c.ClientName,
                p.RenewalDate,
                pt.ProjectTypeCode,
                st.StatusCode,
                IsActive = CAST(CASE WHEN p.IsActive = 1 THEN 1 ELSE 0 END AS BIT),
                IsOnline = CAST(ISNULL(p.IsOnline, 0) AS BIT)
              FROM dbo.tProject AS p
             INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectID = p.ProjectID AND pr.IsActive = 1
              LEFT JOIN dbo.tClientRef AS cr ON cr.ClientRefID = pr.ClientRefID
              LEFT JOIN dbo.tClient AS c ON c.ClientID = cr.ClientID
              LEFT JOIN dbo.tProjectType AS pt ON pt.ProjectTypeID = pr.ProjectTypeID
              LEFT JOIN dbo.tStatus AS st ON st.StatusID = pr.StatusID
             WHERE p.RenewalDate IS NOT NULL
               AND (@IncludeInactive = 1 OR p.IsActive = 1)
               AND (
                    (@MatchMode = N'month_day' AND MONTH(p.RenewalDate) = @Month AND DAY(p.RenewalDate) = @Day)
                 OR (@MatchMode = N'exact' AND CAST(p.RenewalDate AS date) = @ExactDate)
                   )
             ORDER BY c.ClientName, p.ProjectName, p.ProjectID;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            r =>
            {
                var projectId = r.GetInt32(0);
                var projectNumber = r.IsDBNull(1) ? null : r.GetString(1);
                var projectName = r.IsDBNull(2) ? null : r.GetString(2);
                var clientName = r.IsDBNull(3) ? null : r.GetString(3);
                var renewalDate = r.IsDBNull(4) ? null : r.GetDateTime(4).ToString("yyyy-MM-dd");
                return new
                {
                    projectId,
                    projectNumber,
                    projectName,
                    clientName,
                    renewalDate,
                    projectTypeCode = r.IsDBNull(5) ? null : r.GetString(5),
                    statusCode = r.IsDBNull(6) ? null : r.GetString(6),
                    isActive = !r.IsDBNull(7) && r.GetBoolean(7),
                    isOnline = !r.IsDBNull(8) && r.GetBoolean(8),
                    // Preformatted line so ProjectID / ProjectNumber are hard to omit when listing.
                    listLine = $"{clientName ?? "(no client)"} | ProjectID {projectId} | ProjectNumber {projectNumber ?? "(none)"} | {projectName ?? "(unnamed)"} | Renews {renewalDate ?? "(none)"}",
                };
            },
            [
                SqlParameterFactory.Bit("@IncludeInactive", includeInactive),
                SqlParameterFactory.NVarChar("@MatchMode", 20, matchMode),
                SqlParameterFactory.Int("@Month", month),
                SqlParameterFactory.Int("@Day", day),
                SqlParameterFactory.Date("@ExactDate", exactDate),
            ],
            cancellationToken);

        return new
        {
            dateInput = rawDate,
            matchMode,
            matchedMonth = matchMode == "month_day" ? month : (int?)null,
            matchedDay = matchMode == "month_day" ? day : (int?)null,
            matchedExactDate = matchMode == "exact" && exactDate is not null
                ? exactDate.Value.ToString("yyyy-MM-dd")
                : null,
            includeInactive,
            count = rows.Count,
            truncated = rows.Count >= rowLimit,
            projects = rows,
            presentationHint =
                "When listing these projects to the user, include ProjectID and ProjectNumber for every row (use listLine, or projectId + projectNumber fields).",
            message = matchMode == "month_day"
                ? $"Projects with RenewalDate on month {month}/day {day} (any year)."
                : $"Projects with RenewalDate exactly {exactDate:yyyy-MM-dd}.",
        };
    }

    /// <summary>
    /// Parses renewal filters. Month/day-only inputs (1/1, Jan 1) match every year;
    /// inputs with an explicit year match that exact calendar date.
    /// </summary>
    private static bool TryParseRenewalDateFilter(
        string raw,
        out int month,
        out int day,
        out DateTime? exactDate,
        out string matchMode,
        out string? error)
    {
        month = 0;
        day = 0;
        exactDate = null;
        matchMode = "month_day";
        error = null;

        var t = raw.Trim();

        // Explicit M/D or MM/DD without year (also M-D).
        var md = System.Text.RegularExpressions.Regex.Match(
            t,
            @"^(?<m>\d{1,2})[/\-.](?<d>\d{1,2})$");
        if (md.Success
            && int.TryParse(md.Groups["m"].Value, out var m1)
            && int.TryParse(md.Groups["d"].Value, out var d1)
            && m1 is >= 1 and <= 12
            && d1 is >= 1 and <= 31)
        {
            month = m1;
            day = d1;
            matchMode = "month_day";
            return true;
        }

        // Month name + day, optional year: "January 1", "Jan 1, 2026"
        if (DateTime.TryParse(
                t,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out var parsed)
            || DateTime.TryParse(
                t,
                System.Globalization.CultureInfo.GetCultureInfo("en-US"),
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out parsed))
        {
            // If the user typed a 4-digit year, treat as exact date; otherwise month/day.
            var hasYear = System.Text.RegularExpressions.Regex.IsMatch(t, @"\b(19|20)\d{2}\b");
            if (hasYear)
            {
                exactDate = parsed.Date;
                month = parsed.Month;
                day = parsed.Day;
                matchMode = "exact";
                return true;
            }

            month = parsed.Month;
            day = parsed.Day;
            matchMode = "month_day";
            return true;
        }

        error = $"Unrecognized date '{raw}'. Try 1/1, January 1, or 2026-01-01.";
        return false;
    }

    private async Task<object> SearchUserGroupsAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new { matches = Array.Empty<object>(), message = "query is required." };
        }

        var rows = await _reference.GetTopUserGroupSummariesAsync(query.Trim(), cancellationToken);
        var matches = rows.Take(SearchLimit).Select(g => new
        {
            userGroupId = g.UGID,
            userGroupNumber = g.UserGroupNumber,
            userGroupName = g.UGName,
            memberCount = g.MemberCount,
        }).ToList();

        return new { count = matches.Count, matches };
    }

    private async Task<object> GetUserGroupAsync(int userGroupId, CancellationToken cancellationToken)
    {
        if (userGroupId <= 0)
        {
            return new { error = "userGroupId must be positive." };
        }

        var groups = await _admin.ListUserGroupsAsync(null, includeDeleted: true, cancellationToken);
        var group = groups.FirstOrDefault(g => g.UGID == userGroupId);
        if (group is null)
        {
            return new { error = $"User group {userGroupId} was not found." };
        }

        var members = await _admin.ListUserGroupMembersAsync(userGroupId, cancellationToken);
        return new
        {
            userGroupId = group.UGID,
            userGroupNumber = group.UserGroupNumber,
            userGroupName = group.UGName,
            isDeleted = group.IsDeleted,
            members = members.Select(m => new
            {
                userId = m.UserID,
                name = m.UserFullName ?? m.Username,
                email = m.Email,
                role = m.UserRoleName,
                userRoleId = m.UserRoleID,
            }).ToList(),
        };
    }

    private async Task<object> GetDataAuditSummaryAsync(CancellationToken cancellationToken)
    {
        var export = await _dataAudit.GetExportAsync(cancellationToken);
        return new
        {
            generatedAtUtc = export.GeneratedAtUtc,
            sections = export.Sections.Select(s => new
            {
                key = s.Key,
                label = s.Label,
                findingTotal = s.FindingTotal,
                checks = s.Checks.Select(c => new { c.CheckId, c.Title, c.Count }).ToList(),
            }).ToList(),
        };
    }

    private object ListDataAuditChecks()
    {
        var checks = _dataAudit.ListCheckCatalog()
            .Select(c => new
            {
                checkId = c.CheckId,
                toolName = c.ToolName,
                title = c.Title,
                description = c.Description,
            })
            .ToList();

        return new { count = checks.Count, checks };
    }

    private string? ResolveAuditCheckId(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        var match = _dataAudit.ListCheckCatalog()
            .FirstOrDefault(c => string.Equals(c.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
        return match?.CheckId;
    }

    private async Task<object> RunDataAuditCheckAsync(string checkId, CancellationToken cancellationToken)
    {
        var check = await _dataAudit.GetCheckAsync(checkId, cancellationToken);
        if (check is null)
        {
            return new { error = $"Unknown data audit check '{checkId}'." };
        }

        var totalRows = check.Rows.Count;
        var sampleRows = check.Rows
            .Take(AuditRowLimit)
            .Select(row =>
            {
                var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < check.Columns.Count && i < row.Count; i++)
                {
                    dict[check.Columns[i]] = row[i];
                }

                return dict;
            })
            .ToList();

        return new
        {
            checkId = check.CheckId,
            title = check.Title,
            description = check.Description,
            count = check.Count,
            rowCount = totalRows,
            columns = check.Columns,
            rowsReturned = sampleRows.Count,
            truncated = totalRows > AuditRowLimit,
            rows = sampleRows,
            message = totalRows > AuditRowLimit
                ? $"Showing first {AuditRowLimit} of {totalRows} rows. Open Admin → Data Audit for the full list."
                : null,
        };
    }

    private async Task<object> GetProjectRevenueSummaryAsync(int projectId, CancellationToken cancellationToken)
    {
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

        var history = await _projects.GetRevenueHistoryAsync(projectId, cancellationToken);
        var chronological = history
            .OrderBy(r => r.EffectiveDate)
            .ThenBy(r => r.RevenueID)
            .ToList();

        var entries = chronological.Select(r => new
        {
            revenueId = r.RevenueID,
            effectiveDate = r.EffectiveDate.ToString("yyyy-MM-dd"),
            revenue = r.Revenue,
            changeReason = r.ChangeReason,
            recordedBy = r.RecordedByName,
            createdUtc = r.CreatedDate == default
                ? null
                : DateTime.SpecifyKind(r.CreatedDate, DateTimeKind.Utc).ToString("o"),
        }).ToList();

        if (chronological.Count == 0)
        {
            return new
            {
                projectId,
                projectNumber = project.ProjectNumber,
                projectName = project.ProjectName,
                entryCount = 0,
                entries,
                summary = new
                {
                    direction = "none",
                    message = "No revenue history entries exist for this project.",
                },
            };
        }

        var first = chronological[0];
        var last = chronological[^1];
        var netChange = last.Revenue - first.Revenue;
        var direction = netChange > 0m ? "up" : netChange < 0m ? "down" : "unchanged";
        decimal? percentChange = first.Revenue == 0m
            ? null
            : Math.Round(netChange / first.Revenue * 100m, 2);

        var stepChanges = new List<object>();
        for (var i = 1; i < chronological.Count; i++)
        {
            var prev = chronological[i - 1];
            var curr = chronological[i];
            var delta = curr.Revenue - prev.Revenue;
            stepChanges.Add(new
            {
                fromEffectiveDate = prev.EffectiveDate.ToString("yyyy-MM-dd"),
                toEffectiveDate = curr.EffectiveDate.ToString("yyyy-MM-dd"),
                fromRevenue = prev.Revenue,
                toRevenue = curr.Revenue,
                changeAmount = delta,
                direction = delta > 0m ? "up" : delta < 0m ? "down" : "unchanged",
                changeReason = curr.ChangeReason,
            });
        }

        var absNet = Math.Abs(netChange);
        var trendSentence = direction switch
        {
            "up" => percentChange is null
                ? $"Revenue increased by {absNet:0.##} from {first.Revenue:0.##} to {last.Revenue:0.##}."
                : $"Revenue increased by {absNet:0.##} ({percentChange:0.##}%) from {first.Revenue:0.##} to {last.Revenue:0.##}.",
            "down" => percentChange is null
                ? $"Revenue decreased by {absNet:0.##} from {first.Revenue:0.##} to {last.Revenue:0.##}."
                : $"Revenue decreased by {absNet:0.##} ({Math.Abs(percentChange.Value):0.##}%) from {first.Revenue:0.##} to {last.Revenue:0.##}.",
            _ => chronological.Count == 1
                ? $"Only one revenue entry exists: {last.Revenue:0.##} effective {last.EffectiveDate:yyyy-MM-dd}."
                : $"Revenue is unchanged at {last.Revenue:0.##} across {chronological.Count} entries.",
        };

        return new
        {
            projectId,
            projectNumber = project.ProjectNumber,
            projectName = project.ProjectName,
            entryCount = chronological.Count,
            entries,
            summary = new
            {
                earliestEffectiveDate = first.EffectiveDate.ToString("yyyy-MM-dd"),
                earliestRevenue = first.Revenue,
                latestEffectiveDate = last.EffectiveDate.ToString("yyyy-MM-dd"),
                latestRevenue = last.Revenue,
                netChange,
                percentChange,
                direction,
                stepChanges,
                message = trendSentence,
            },
        };
    }

    private async Task<object> GenerateProjectReportAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var projectId = GetInt(root, "projectId");
        if (projectId <= 0)
        {
            return new { error = "projectId must be a positive integer." };
        }

        var fileName = GetString(root, "fileName");
        var result = await _projectReports.GenerateAndSaveAsync(
            projectId,
            string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim(),
            overwriteSameName: true,
            cancellationToken);

        if (!result.Success)
        {
            return new
            {
                error = result.Error ?? "Could not generate the project report.",
                projectId = result.ProjectId,
                projectNumber = result.ProjectNumber,
                projectName = result.ProjectName,
            };
        }

        return new
        {
            success = true,
            projectId = result.ProjectId,
            projectNumber = result.ProjectNumber,
            projectName = result.ProjectName,
            projectFileId = result.ProjectFileId,
            fileName = result.FileName,
            linkLabel = result.LinkLabel,
            category = result.Category,
            downloadUrl = result.DownloadUrl,
            downloadMarkdown = $"[{result.LinkLabel}]({result.DownloadUrl})",
            message = result.Message,
            presentationHint =
                "Put downloadMarkdown in your reply unchanged so the chat can show a clickable alias "
                + "(e.g. ClientName_ProjectName_ProjectReport.pdf) that opens the PDF in a new browser tab.",
        };
    }

    private async Task<object> UpdateProjectRevenueAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var projectId = GetInt(root, "projectId");
        if (projectId <= 0)
        {
            return new { error = "projectId must be a positive integer." };
        }

        var amount = GetDecimal(root, "revenue");
        if (amount is null or <= 0m)
        {
            return new { error = "revenue must be a number greater than 0." };
        }

        var project = await _projects.GetFullAsync(projectId, cancellationToken)
            ?? await _projects.GetAsync(projectId, cancellationToken);
        if (project is null)
        {
            return new { error = $"Project {projectId} was not found." };
        }

        if (project.IsDeleted)
        {
            return new { error = $"Project {projectId} is deleted and cannot receive revenue updates." };
        }

        var effectiveDate = GetDate(root, "effectiveDate") ?? DateTime.UtcNow.Date;
        var changeReason = GetString(root, "changeReason");
        if (changeReason.Length > 255)
        {
            changeReason = changeReason[..255];
        }

        var revenueId = await _projects.AddRevenueAsync(
            new ProjectRevenue
            {
                ProjectID = projectId,
                EffectiveDate = effectiveDate,
                Revenue = amount.Value,
                ChangeReason = string.IsNullOrWhiteSpace(changeReason) ? null : changeReason.Trim(),
                RecordedBy = CmsDefaults.ActingUserId,
            },
            cancellationToken);

        return new
        {
            success = true,
            revenueId,
            projectId,
            projectNumber = project.ProjectNumber,
            projectName = project.ProjectName,
            revenue = amount.Value,
            effectiveDate = effectiveDate.ToString("yyyy-MM-dd"),
            changeReason = string.IsNullOrWhiteSpace(changeReason) ? null : changeReason.Trim(),
            message = "Revenue history entry was created and is now the latest revenue for this project.",
        };
    }

    private async Task<bool> IsUserAssignedToProjectAsync(
        int userId,
        int projectId,
        CancellationToken cancellationToken)
    {
        if (userId <= 0 || projectId <= 0)
        {
            return false;
        }

        const string sql = """
            SELECT TOP (1) 1
              FROM dbo.tProject AS p
             INNER JOIN dbo.tProjectRef AS pr ON pr.ProjectID = p.ProjectID AND pr.IsActive = 1
             INNER JOIN dbo.tUserGroupRef AS ugr
                ON ugr.UserGroupID = pr.UserGroupID
               AND ugr.UserID = @UserID
             WHERE p.ProjectID = @ProjectID;
            """;

        var rows = await _db.QueryTextAsync(
            sql,
            static _ => 1,
            [
                SqlParameterFactory.Int("@UserID", userId),
                SqlParameterFactory.Int("@ProjectID", projectId),
            ],
            cancellationToken);

        return rows.Count > 0;
    }

    private sealed record ToolDef(string Name, object Definition);

    private static ToolDef Tool(string name, string description, object parameters) => new(
        name,
        new
        {
            type = "function",
            function = new
            {
                name,
                description,
                parameters,
            }
        });

    private static string GetString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static int GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return 0;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
        {
            return n;
        }

        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static bool GetBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return false;
        }

        if (el.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return el.GetBoolean();
        }

        if (el.ValueKind == JsonValueKind.String
            && bool.TryParse(el.GetString(), out var parsed))
        {
            return parsed;
        }

        return false;
    }

    private static decimal? GetDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
        {
            return d;
        }

        if (el.ValueKind == JsonValueKind.String
            && decimal.TryParse(el.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? GetDate(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = el.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParse(
                raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out var dt))
        {
            return dt.Date;
        }

        return null;
    }
}
