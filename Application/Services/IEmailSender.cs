namespace ClientManagementSystem.Services;

public sealed class EmailMessage
{
    public required IReadOnlyList<string> To { get; init; }
    public IReadOnlyList<string> Cc { get; init; } = [];
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public string? PlainTextBody { get; init; }
}

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
