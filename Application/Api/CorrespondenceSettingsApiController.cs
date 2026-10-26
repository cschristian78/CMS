using ClientManagementSystem.Models;
using ClientManagementSystem.Security;
using Microsoft.AspNetCore.Mvc;

namespace ClientManagementSystem.Api;

[Route("api/admin/correspondence-settings")]
[ApiController]
public sealed class CorrespondenceSettingsApiController : ControllerBase
{
    private readonly ICorrespondenceSettingsStore _store;
    private readonly ILogger<CorrespondenceSettingsApiController> _logger;

    public CorrespondenceSettingsApiController(
        ICorrespondenceSettingsStore store,
        ILogger<CorrespondenceSettingsApiController> logger)
    {
        _store = store;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<object>> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await _store.GetAsync(cancellationToken);
        return Ok(ToDto(settings));
    }

    [HttpPut]
    public async Task<ActionResult<object>> PutAsync(
        [FromBody] CorrespondenceSettingsDto? body,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "Body is required." });
        }

        try
        {
            var settings = new CorrespondenceSettings
            {
                Enabled = body.Enabled,
                Sequence = body.Sequence ?? CorrespondenceSettings.SequenceWeekly,
                DayOfWeek = body.DayOfWeek ?? nameof(DayOfWeek.Monday),
                SendTimeLocal = body.SendTimeLocal ?? "07:30",
                TimeZoneId = body.TimeZoneId ?? "Eastern Standard Time",
                RecipientType = body.RecipientType ?? CorrespondenceSettings.RecipientEachCatMember,
                CommunicationType = body.CommunicationType
                    ?? CorrespondenceSettings.CommunicationCatMemberProjectStatus,
                CcAddress = body.CcAddress ?? "Scott.Christian@bmsgroup.com",
            };

            await _store.SaveAsync(settings, cancellationToken);
            var saved = await _store.GetAsync(cancellationToken);
            return Ok(ToDto(saved));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save correspondence settings.");
            return BadRequest(new ProblemDetails
            {
                Title = "Save failed",
                Detail = ex.Message,
            });
        }
    }

    private static object ToDto(CorrespondenceSettings settings) => new
    {
        enabled = settings.Enabled,
        sequence = settings.Sequence,
        dayOfWeek = settings.DayOfWeek,
        sendTimeLocal = settings.SendTimeLocal,
        timeZoneId = settings.TimeZoneId,
        recipientType = settings.RecipientType,
        communicationType = settings.CommunicationType,
        ccAddress = settings.CcAddress,
        recipientOptions = new[]
        {
            new { value = CorrespondenceSettings.RecipientAnalyticsLead, text = "Analytics Lead" },
            new { value = CorrespondenceSettings.RecipientEachCatMember, text = "Each Cat Member" },
        },
        sequenceOptions = new[]
        {
            new { value = CorrespondenceSettings.SequenceWeekly, text = "Weekly" },
        },
        communicationOptions = new[]
        {
            new
            {
                value = CorrespondenceSettings.CommunicationCatMemberProjectStatus,
                text = "CAT member project status (In Progress assigned projects, tasks, % complete, time tracking)",
            },
        },
        dayOfWeekOptions = Enum.GetNames<DayOfWeek>(),
    };
}

public sealed class CorrespondenceSettingsDto
{
    public bool Enabled { get; set; } = true;
    public string? Sequence { get; set; }
    public string? DayOfWeek { get; set; }
    public string? SendTimeLocal { get; set; }
    public string? TimeZoneId { get; set; }
    public string? RecipientType { get; set; }
    public string? CommunicationType { get; set; }
    public string? CcAddress { get; set; }
}
