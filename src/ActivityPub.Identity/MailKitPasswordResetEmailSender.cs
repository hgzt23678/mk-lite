using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace ActivityPub.Identity;

public sealed class MailKitPasswordResetEmailSender(PasswordResetOptions options) :
    IPasswordResetEmailSender,
    IEmailConfirmationSender
{
    public async Task SendAsync(PasswordResetEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.SendTimeout);

        string link = email.ResetUri.AbsoluteUri;
        MimeMessage message = CreateMessage(
            email.RecipientAddress,
            "Password reset requested",
            $"To reset your password, open this link before {email.ExpiresAt:O}:\n{link}",
            $"<p>To reset your password, open this link before {WebUtility.HtmlEncode(email.ExpiresAt.ToString("O"))}:</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(link)}</a></p>");

        await SendMessageAsync(message, timeout.Token).ConfigureAwait(false);
    }

    async Task IEmailConfirmationSender.SendAsync(
        EmailConfirmationEmail email,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.SendTimeout);
        string link = email.ConfirmationUri.AbsoluteUri;
        MimeMessage message = CreateMessage(
            email.RecipientAddress,
            "Confirm your email address",
            $"To confirm your email address, open this link before {email.ExpiresAt:O}:\n{link}",
            $"<p>To confirm your email address, open this link before {WebUtility.HtmlEncode(email.ExpiresAt.ToString("O"))}:</p><p><a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(link)}</a></p>");

        await SendMessageAsync(message, timeout.Token).ConfigureAwait(false);
    }

    private async Task SendMessageAsync(MimeMessage message, CancellationToken cancellationToken)
    {

        using var client = new SmtpClient();
        await client.ConnectAsync(
            options.SmtpHost,
            options.SmtpPort,
            ToSecureSocketOptions(options.TlsMode),
            cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(options.SmtpUsername))
        {
            string password = ReadPasswordSecret(options.SmtpPasswordFile!);
            await client.AuthenticateAsync(options.SmtpUsername, password, cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
    }

    private MimeMessage CreateMessage(string recipient, string subject, string plainText, string html)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.SenderName, options.SenderAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new Multipart("alternative")
        {
            new TextPart(TextFormat.Plain) { Text = plainText },
            new TextPart(TextFormat.Html) { Text = html }
        };
        return message;
    }

    private static SecureSocketOptions ToSecureSocketOptions(PasswordResetTlsMode mode) => mode switch
    {
        PasswordResetTlsMode.None => SecureSocketOptions.None,
        PasswordResetTlsMode.StartTls => SecureSocketOptions.StartTls,
        PasswordResetTlsMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => throw new InvalidOperationException("Unsupported password reset SMTP TLS mode.")
    };

    private static string ReadPasswordSecret(string path)
    {
        string secret = File.ReadAllText(path).TrimEnd('\r', '\n');
        if (secret.Length is < 1 or > 4_096 || secret.Any(char.IsControl))
        {
            throw new InvalidOperationException("Password reset SMTP password secret has an invalid length or format.");
        }

        return secret;
    }
}
