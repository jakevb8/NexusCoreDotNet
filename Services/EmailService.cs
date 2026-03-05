using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NexusCoreDotNet.Services;

public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly HttpClient _http;

    public EmailService(IConfiguration config, ILogger<EmailService> logger, IHttpClientFactory factory)
    {
        _config = config;
        _logger = logger;
        _http = factory.CreateClient("Resend");
    }

    public async Task SendInviteEmailAsync(
        string toEmail,
        string inviteToken,
        string organizationName,
        string inviterName,
        string baseUrl)
    {
        var apiKey = _config["Resend:ApiKey"];
        var inviteUrl = $"{baseUrl.TrimEnd('/')}/AcceptInvite?token={inviteToken}";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogInformation("[DEV] Invite link for {Email}: {Url}", toEmail, inviteUrl);
            return;
        }

        var html = BuildInviteHtml(inviterName, organizationName, inviteUrl);

        var payload = new
        {
            from = "NexusCoreDotNet <onboarding@resend.dev>",
            to = new[] { toEmail },
            subject = $"{inviterName} invited you to join {organizationName} on NexusCoreDotNet",
            html
        };

        var json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Resend returned {Status}: {Body}", (int)response.StatusCode, body);
            }
            else
            {
                _logger.LogInformation("Invite email sent to {Email}", toEmail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send invite email to {Email}", toEmail);
        }
    }

    private static string BuildInviteHtml(string inviterName, string orgName, string inviteUrl) => $"""
<!DOCTYPE html>
<html lang="en">
<head><meta charset="UTF-8"/><meta name="viewport" content="width=device-width,initial-scale=1.0"/></head>
<body style="margin:0;padding:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f9fafb;">
  <table width="100%" cellpadding="0" cellspacing="0" style="padding:40px 16px;">
    <tr><td align="center">
      <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:12px;border:1px solid #e5e7eb;overflow:hidden;">
        <tr><td style="background:#2563eb;padding:32px 40px;">
          <p style="margin:0;font-size:20px;font-weight:700;color:#ffffff;letter-spacing:-0.3px;">NexusCoreDotNet</p>
        </td></tr>
        <tr><td style="padding:40px;">
          <h1 style="margin:0 0 8px;font-size:24px;font-weight:700;color:#111827;">You've been invited!</h1>
          <p style="margin:0 0 24px;font-size:15px;color:#6b7280;line-height:1.6;">
            <strong style="color:#374151;">{inviterName}</strong> has invited you to join
            <strong style="color:#374151;">{orgName}</strong> on NexusCoreDotNet.
          </p>
          <p style="margin:0 0 32px;font-size:15px;color:#6b7280;line-height:1.6;">
            Click the button below to accept your invitation. This link expires in <strong>7 days</strong>.
          </p>
          <table cellpadding="0" cellspacing="0">
            <tr><td style="border-radius:8px;background:#2563eb;">
              <a href="{inviteUrl}" style="display:inline-block;padding:14px 28px;font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:8px;">
                Accept invitation
              </a>
            </td></tr>
          </table>
          <p style="margin:32px 0 0;font-size:13px;color:#9ca3af;line-height:1.6;">
            If the button doesn't work, copy and paste this link:<br/>
            <a href="{inviteUrl}" style="color:#2563eb;word-break:break-all;">{inviteUrl}</a>
          </p>
        </td></tr>
        <tr><td style="padding:24px 40px;border-top:1px solid #f3f4f6;">
          <p style="margin:0;font-size:12px;color:#9ca3af;">If you weren't expecting this invitation, you can safely ignore this email.</p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>
""";
}
