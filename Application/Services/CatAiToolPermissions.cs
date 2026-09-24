using System.Text.Json;

namespace ClientManagementSystem.Services;

/// <summary>Authenticated CatAI caller identity for tool authorization.</summary>
public sealed class CatAiActorContext
{
    public int UserId { get; init; }
    public string UserType { get; init; } = "Basic";

    public bool IsAdministrator =>
        string.Equals(UserType, "Administrator", StringComparison.OrdinalIgnoreCase);

    public bool IsManager =>
        string.Equals(UserType, "Manager", StringComparison.OrdinalIgnoreCase);

    public bool IsAnalyst =>
        string.Equals(UserType, "Analyst", StringComparison.OrdinalIgnoreCase);

    /// <summary>Manager or Administrator — may create/update clients, projects, users, and groups.</summary>
    public bool CanManage => IsAdministrator || IsManager;
}

/// <summary>CatAI tool permission rules by <c>dbo.tUser.UserType</c>.</summary>
public static class CatAiToolPermissions
{
    /// <summary>Writes scoped to a project the Analyst is assigned to (via the project's user group).</summary>
    private static readonly HashSet<string> AssignedProjectWriteTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "update_project_revenue",
        "update_project_renewal_date",
        "generate_project_report",
    };

    /// <summary>Create/update tools for Managers (and Administrators). Includes assigned-project writes.</summary>
    private static readonly HashSet<string> ManagerWriteTools = new(StringComparer.OrdinalIgnoreCase)
    {
        // Users
        "create_user",
        "update_user",
        "deactivate_user",
        "restore_user",
        // User groups
        "create_user_group",
        "update_user_group",
        "add_user_group_member",
        "remove_user_group_member",
        "ensure_group_analytics_lead",
        "ensure_group_account_executive",
        "soft_delete_user_group",
        "restore_user_group",
        "start_user_group_wizard",
        // Projects / clients
        "create_renewal_project",
        "start_renewal_project_wizard",
        "assign_project_user_group",
        "unassign_project_user_group",
        "update_client_leads",
        "update_project_revenue",
        "update_project_renewal_date",
        "generate_project_report",
    };

    public static bool IsWriteTool(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        if (ManagerWriteTools.Contains(toolName) || AssignedProjectWriteTools.Contains(toolName))
        {
            return true;
        }

        // Unknown write-like names stay conservative (deny for non-admin until classified).
        return toolName.StartsWith("create_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("update_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("add_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("remove_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("assign_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("unassign_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("soft_delete_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("deactivate_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("restore_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("ensure_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("generate_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("start_", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReadTool(string toolName)
        => !IsWriteTool(toolName);

    public static bool CanSeeTool(CatAiActorContext actor, string toolName)
    {
        if (actor.IsAdministrator)
        {
            return true;
        }

        if (IsReadTool(toolName))
        {
            return true;
        }

        if (actor.CanManage)
        {
            return ManagerWriteTools.Contains(toolName) || IsWriteTool(toolName);
        }

        if (actor.IsAnalyst)
        {
            return AssignedProjectWriteTools.Contains(toolName);
        }

        return false;
    }

    public static string? AuthorizeExecution(
        CatAiActorContext actor,
        string toolName,
        JsonElement args)
    {
        if (actor.IsAdministrator)
        {
            return null;
        }

        if (IsReadTool(toolName))
        {
            return null;
        }

        if (actor.CanManage)
        {
            if (ManagerWriteTools.Contains(toolName) || IsWriteTool(toolName))
            {
                return null;
            }

            return $"Managers cannot run '{toolName}'.";
        }

        if (actor.IsAnalyst)
        {
            if (!AssignedProjectWriteTools.Contains(toolName))
            {
                return $"Analysts may only update projects they are assigned to. '{toolName}' is not allowed.";
            }

            var projectId = ReadProjectId(args);
            if (projectId <= 0)
            {
                return "projectId is required for this Analyst write operation.";
            }

            // Assignment is verified by the tool service after this gate (needs DB).
            return null;
        }

        return "Your user type is read-only in CatAI. Ask a Manager or Administrator to make changes.";
    }

    public static bool RequiresAssignedProjectCheck(CatAiActorContext actor, string toolName)
        => actor.IsAnalyst && AssignedProjectWriteTools.Contains(toolName);

    public static int ReadProjectId(JsonElement root)
    {
        if (!root.TryGetProperty("projectId", out var el))
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

    public static string PermissionSystemAddendum(CatAiActorContext actor)
    {
        if (actor.IsAdministrator)
        {
            return "PERMISSIONS: You are an Administrator — full CatAI read/write tool access.";
        }

        if (actor.IsManager)
        {
            return "PERMISSIONS: You are a Manager — you may create and update clients, projects, users, and user groups. "
                + "Do not claim Admin-only capabilities. Prefer write tools when the user asks to change data.";
        }

        if (actor.IsAnalyst)
        {
            return "PERMISSIONS: You are an Analyst — read tools are available, and you may update projects you are assigned to "
                + "(revenue, renewal date, generate report) only. Refuse other write requests and suggest a Manager/Administrator.";
        }

        return "PERMISSIONS: Your user type is read-only in CatAI. Answer with lookups only; if the user asks to change data, "
            + "explain that a Manager or Administrator must perform the change.";
    }

    public static bool CanStartCreateWizard(CatAiActorContext actor)
        => actor.CanManage;
}
