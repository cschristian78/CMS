using ClientManagementSystem.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace ClientManagementSystem.Services;

/// <summary>Payload pushed to browsers on the "update" hub method.</summary>
public sealed class RealtimeUpdate
{
    public string Kind { get; init; } = "projectNote";
    public int ProjectId { get; init; }
    public string ChangeType { get; init; } = string.Empty;
    public int? NoteId { get; init; }
    public int? ProjectFileId { get; init; }
}

public interface IRealtimeNotifier
{
    Task NotifyProjectAsync(RealtimeUpdate update, CancellationToken cancellationToken = default);
}

/// <summary>Pushes updates through <see cref="UpdatesHub"/> without callers referencing the hub.</summary>
public sealed class RealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<UpdatesHub> _hub;
    private readonly ILogger<RealtimeNotifier> _logger;

    public RealtimeNotifier(IHubContext<UpdatesHub> hub, ILogger<RealtimeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyProjectAsync(RealtimeUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (update.ProjectId <= 0)
        {
            return;
        }

        try
        {
            await _hub.Clients
                .Group(UpdatesHub.ProjectGroup(update.ProjectId))
                .SendAsync("update", update, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push realtime update for project {ProjectId}.", update.ProjectId);
        }
    }
}
