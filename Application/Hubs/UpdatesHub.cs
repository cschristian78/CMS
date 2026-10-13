using System.Security.Claims;
using ClientManagementSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ClientManagementSystem.Hubs;

/// <summary>
/// Authenticated realtime channel. Group membership is decided on the server.
/// Clients may ask to watch a project, but the hub only joins the group after it
/// confirms the caller is authenticated and the project exists.
/// </summary>
[Authorize]
public sealed class UpdatesHub : Hub
{
    private readonly IProjectRepository _projects;

    public UpdatesHub(IProjectRepository projects)
    {
        _projects = projects;
    }

    public static string UserGroup(int userId) => $"user-{userId}";

    public static string ProjectGroup(int projectId) => $"project-{projectId}";

    public override async Task OnConnectedAsync()
    {
        var userId = GetAuthenticatedUserId();
        if (userId > 0)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Watch one project. The project id is validated server-side before any group join.</summary>
    public async Task JoinProject(int projectId)
    {
        if (projectId <= 0)
        {
            throw new HubException("A valid project is required.");
        }

        if (GetAuthenticatedUserId() <= 0)
        {
            throw new HubException("Not authenticated.");
        }

        var project = await _projects.GetAsync(projectId, Context.ConnectionAborted);
        if (project is null || project.IsDeleted)
        {
            throw new HubException("Project was not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ProjectGroup(project.ProjectID));
    }

    public Task LeaveProject(int projectId)
    {
        if (projectId <= 0 || GetAuthenticatedUserId() <= 0)
        {
            return Task.CompletedTask;
        }

        return Groups.RemoveFromGroupAsync(Context.ConnectionId, ProjectGroup(projectId));
    }

    private int GetAuthenticatedUserId()
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) && id > 0 ? id : 0;
    }
}
