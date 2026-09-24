namespace ClientManagementSystem.Models;

/// <summary>Resolved CatAI / OpenAI settings used at chat time.</summary>
public sealed class CatAiSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Deployment { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2024-06-01";
    public string SystemPrompt { get; set; } = CatAiDefaults.SystemPrompt;
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 1500;
    public int MaxToolRounds { get; set; } = 5;
}

public static class CatAiDefaults
{
    public const string SystemPrompt =
        "You are CatAI, an assistant for the Client Management System (CMS). "
        + "Use the provided tools to look up live CMS data for factual questions about clients, projects, user groups, and data-audit summaries. "
        + "Never invent client names, analytics leads, account executives, project numbers, or other CMS facts. "
        + "If a search returns multiple matches, ask the user to choose. "
        + "When listing projects, always include ProjectID and ProjectNumber for each project. "
        + "When a tool returns downloadMarkdown or downloadUrl+linkLabel, put that markdown link in your reply "
        + "(visible text = linkLabel like ClientName_ProjectName_ProjectReport.pdf). "
        + "If a field is missing or null, say it is not assigned. "
        + "Only use write tools when the user explicitly asks to change data; confirm identities first if ambiguous. "
        + "When resolving Data Audit findings, prefer the matching remediation tools (create/assign user groups, renewal projects, renewal dates, revenue). "
        + "This chat keeps prior turns in context — use them for follow-ups, but prefer live tool lookups over inventing facts. "
        + "When the user wants to CREATE a renewal project or CREATE a user group, start the matching wizard tool first "
        + "(start_renewal_project_wizard or start_user_group_wizard), then ask for missing fields one at a time, "
        + "use lookup tools as needed, and only call the create_* tool after required fields are confirmed. "
        + "If the user cancels a create flow, call cancel_wizard. "
        + "Keep answers concise and accurate.";

    public const string RenewalProjectWizardKind = "renewal_project";
    public const string UserGroupWizardKind = "user_group";

    public static string WizardSystemAddendum(string wizardKind) => wizardKind switch
    {
        RenewalProjectWizardKind =>
            "WIZARD MODE (renewal project): Prior turns in this conversation are in context. "
            + "Collect: clientId (required — resolve via search_clients), projectName (optional), "
            + "renewalDate YYYY-MM-DD (optional), userGroupId (optional — list_user_groups / create_user_group). "
            + "Ask for one missing required item at a time. When ready, call create_renewal_project. "
            + "On success or user cancel, the wizard ends.",
        UserGroupWizardKind =>
            "WIZARD MODE (user group): Prior turns in this conversation are in context. "
            + "Collect: userGroupName (required) and at least one member with user (userId/userName) and role (roleId/roleName). "
            + "Use search_users and list_user_roles to resolve names. Ask for one missing item at a time. "
            + "When ready, call create_user_group. On success or user cancel, the wizard ends.",
        _ => "WIZARD MODE: Prior turns in this conversation are in context. Complete the create flow, then finish."
    };
}

/// <summary>Admin GET/PUT DTO. ApiKey is masked on GET.</summary>
public sealed class CatAiSettingsDto
{
    public string? ApiKey { get; set; }
    public bool ApiKeyConfigured { get; set; }
    public string Model { get; set; } = "gpt-4o";
    public string BaseUrl { get; set; } = string.Empty;
    public string Deployment { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2024-06-01";
    public string SystemPrompt { get; set; } = CatAiDefaults.SystemPrompt;
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 1500;
    public int MaxToolRounds { get; set; } = 5;
}

public sealed class CatAiChatRequest
{
    public int? ConversationId { get; set; }
    public List<CatAiChatMessageDto> Messages { get; set; } = [];
}

public sealed class CatAiChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public sealed class CatAiChatResult
{
    public string Reply { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int? ConversationId { get; set; }
    public IReadOnlyList<string> ToolsUsed { get; set; } = [];
    public bool WizardActive { get; set; }
    public string? WizardKind { get; set; }
    public string? WizardLabel { get; set; }
}

public sealed class CatAiConversationSummary
{
    public int ConversationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
    public bool WizardActive { get; set; }
    public string? WizardKind { get; set; }
}

public sealed class CatAiConversationDetail
{
    public int ConversationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool WizardActive { get; set; }
    public string? WizardKind { get; set; }
    public IReadOnlyList<CatAiStoredMessage> Messages { get; set; } = [];
}

public sealed class CatAiStoredMessage
{
    public int MessageId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
