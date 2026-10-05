namespace ClientManagementSystem.Models;

/// <summary>Admin-configured schedule for outbound CMS correspondence (email digests).</summary>
public sealed class CorrespondenceSettings
{
    public const string RecipientAnalyticsLead = "AnalyticsLead";
    public const string RecipientEachCatMember = "EachCatMember";

    public const string SequenceWeekly = "Weekly";

    public const string CommunicationCatMemberProjectStatus = "CatMemberProjectStatus";

    /// <summary>When false, the scheduled correspondence runner should skip sends.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often to send. Currently only <see cref="SequenceWeekly"/> is supported.</summary>
    public string Sequence { get; set; } = SequenceWeekly;

    /// <summary>Day of week name (e.g. Monday).</summary>
    public string DayOfWeek { get; set; } = nameof(System.DayOfWeek.Monday);

    /// <summary>Local send time as HH:mm (24-hour), interpreted in <see cref="TimeZoneId"/>.</summary>
    public string SendTimeLocal { get; set; } = "07:30";

    /// <summary>Windows time zone id. Eastern Standard Time observes EDT/EST.</summary>
    public string TimeZoneId { get; set; } = "Eastern Standard Time";

    /// <summary><see cref="RecipientAnalyticsLead"/> or <see cref="RecipientEachCatMember"/>.</summary>
    public string RecipientType { get; set; } = RecipientEachCatMember;

    /// <summary>Which communication / report to send when the schedule fires.</summary>
    public string CommunicationType { get; set; } = CommunicationCatMemberProjectStatus;

    /// <summary>Address copied on each send (defaults to the CMS calendar CC).</summary>
    public string CcAddress { get; set; } = "Scott.Christian@bmsgroup.com";

    public static CorrespondenceSettings CreateDefaults() => new();
}
