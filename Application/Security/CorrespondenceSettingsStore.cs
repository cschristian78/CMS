using System.Globalization;
using System.Text.Json;
using ClientManagementSystem.Models;

namespace ClientManagementSystem.Security;

public sealed class CorrespondenceSettingsStore : ICorrespondenceSettingsStore
{
    private static readonly HashSet<string> AllowedSequences = new(StringComparer.OrdinalIgnoreCase)
    {
        CorrespondenceSettings.SequenceWeekly,
    };

    private static readonly HashSet<string> AllowedRecipientTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        CorrespondenceSettings.RecipientAnalyticsLead,
        CorrespondenceSettings.RecipientEachCatMember,
    };

    private static readonly HashSet<string> AllowedCommunicationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        CorrespondenceSettings.CommunicationCatMemberProjectStatus,
    };

    private readonly string _settingsFilePath;

    public CorrespondenceSettingsStore(IWebHostEnvironment environment)
    {
        var appDataPath = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(appDataPath);
        _settingsFilePath = Path.Combine(appDataPath, "correspondence.settings.json");
    }

    public async Task<CorrespondenceSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return CorrespondenceSettings.CreateDefaults();
        }

        var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return CorrespondenceSettings.CreateDefaults();
        }

        var file = JsonSerializer.Deserialize<CorrespondenceSettings>(json) ?? CorrespondenceSettings.CreateDefaults();
        return Normalize(file);
    }

    public async Task SaveAsync(CorrespondenceSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        var json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_settingsFilePath, json, cancellationToken);
    }

    private static CorrespondenceSettings Normalize(CorrespondenceSettings settings)
    {
        var defaults = CorrespondenceSettings.CreateDefaults();
        var day = (settings.DayOfWeek ?? string.Empty).Trim();
        if (!Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out _))
        {
            day = defaults.DayOfWeek;
        }

        var time = (settings.SendTimeLocal ?? string.Empty).Trim();
        if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            && !TimeOnly.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            time = defaults.SendTimeLocal;
        }
        else if (TimeOnly.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            time = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        var sequence = (settings.Sequence ?? string.Empty).Trim();
        if (!AllowedSequences.Contains(sequence))
        {
            sequence = defaults.Sequence;
        }

        var recipient = (settings.RecipientType ?? string.Empty).Trim();
        if (!AllowedRecipientTypes.Contains(recipient))
        {
            recipient = defaults.RecipientType;
        }

        var communication = (settings.CommunicationType ?? string.Empty).Trim();
        if (!AllowedCommunicationTypes.Contains(communication))
        {
            communication = defaults.CommunicationType;
        }

        var tz = (settings.TimeZoneId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(tz))
        {
            tz = defaults.TimeZoneId;
        }
        else
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(tz);
            }
            catch (TimeZoneNotFoundException)
            {
                tz = defaults.TimeZoneId;
            }
            catch (InvalidTimeZoneException)
            {
                tz = defaults.TimeZoneId;
            }
        }

        var cc = (settings.CcAddress ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cc))
        {
            cc = defaults.CcAddress;
        }

        return new CorrespondenceSettings
        {
            Enabled = settings.Enabled,
            Sequence = sequence,
            DayOfWeek = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(day.ToLowerInvariant()),
            SendTimeLocal = time,
            TimeZoneId = tz,
            RecipientType = AllowedRecipientTypes.Contains(recipient)
                ? (string.Equals(recipient, CorrespondenceSettings.RecipientAnalyticsLead, StringComparison.OrdinalIgnoreCase)
                    ? CorrespondenceSettings.RecipientAnalyticsLead
                    : CorrespondenceSettings.RecipientEachCatMember)
                : defaults.RecipientType,
            CommunicationType = CorrespondenceSettings.CommunicationCatMemberProjectStatus,
            CcAddress = cc,
        };
    }
}
