using System.Net;
using System.Net.Mail;
using ClientManagementSystem.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClientManagementSystem.Services;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_options.Enabled)
        {
            _logger.LogInformation("Email skipped (Email:Enabled=false). Subject: {Subject}", message.Subject);
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(_options.SmtpHost))
        {
            _logger.LogWarning("Email skipped (Email:SmtpHost is not configured). Subject: {Subject}", message.Subject);
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            _logger.LogWarning("Email skipped (Email:FromAddress is not configured). Subject: {Subject}", message.Subject);
            return Task.CompletedTask;
        }

        var recipients = message.To
            .Select(NormalizeAddress)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (recipients.Count == 0)
        {
            _logger.LogWarning("Email skipped (no To recipients). Subject: {Subject}", message.Subject);
            return Task.CompletedTask;
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress.Trim(), _options.FromDisplayName?.Trim() ?? "CMS"),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true,
        };

        if (!string.IsNullOrWhiteSpace(message.PlainTextBody))
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.PlainTextBody,
                null,
                "text/plain"));
        }

        foreach (var to in recipients)
        {
            mail.To.Add(to);
        }

        foreach (var cc in message.Cc.Select(NormalizeAddress).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            mail.CC.Add(cc);
        }

        using var client = new SmtpClient(_options.SmtpHost.Trim(), _options.SmtpPort)
        {
            EnableSsl = _options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username.Trim(), _options.Password ?? string.Empty);
        }

        _logger.LogInformation(
            "Sending email \"{Subject}\" to {Recipients} (CC: {CcCount}).",
            message.Subject,
            string.Join(", ", recipients),
            mail.CC.Count);

        return client.SendMailAsync(mail, cancellationToken);
    }

    private static string NormalizeAddress(string? value) => (value ?? string.Empty).Trim();
}
