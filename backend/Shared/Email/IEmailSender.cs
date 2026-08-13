namespace Backend.Shared.Email;

public interface IEmailSender
{
    bool IsConfigured();

    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
