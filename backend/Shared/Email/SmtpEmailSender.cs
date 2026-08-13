using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Backend.Shared.Email;

/// <summary>
/// Gửi email qua SMTP bằng MailKit. Dự án chưa từng gửi email nào trước đây — đây là hạ tầng
/// đầu tiên, dựng riêng cho bản tin trang tin nhưng không gắn cứng vào NewsSite, dùng lại được
/// cho việc khác sau này.
/// </summary>
public class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public bool IsConfigured()
    {
        var opt = options.Value;
        return opt.Enabled
            && !string.IsNullOrWhiteSpace(opt.Host)
            && !string.IsNullOrWhiteSpace(opt.Username)
            && !string.IsNullOrWhiteSpace(opt.Password)
            && !string.IsNullOrWhiteSpace(opt.FromAddress);
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        var opt = options.Value;
        if (!IsConfigured())
        {
            logger.LogWarning("Chưa cấu hình SMTP (EmailOptions) — không gửi được email tới {To}", toEmail);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(opt.FromName, opt.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        var socketOptions = opt.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(opt.Host, opt.Port, socketOptions, ct);
        await client.AuthenticateAsync(opt.Username, opt.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
