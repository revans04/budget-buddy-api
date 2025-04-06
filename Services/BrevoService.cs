using System.Threading.Tasks;
using FamilyBudgetApi.Models;
using Microsoft.Extensions.Options;
using sib_api_v3_sdk.Api; // Brevo (Sendinblue) SDK
using sib_api_v3_sdk.Model;
using sib_api_v3_sdk.Client;

namespace FamilyBudgetApi.Services
{
  public class BrevoService
  {
    private readonly BrevoSettings _brevoSettings;

    public BrevoService(IOptions<BrevoSettings> brevoSettings)
    {
       _brevoSettings = brevoSettings.Value ?? throw new ArgumentNullException(nameof(brevoSettings));
        if (string.IsNullOrEmpty(_brevoSettings.ApiKey))
            throw new InvalidOperationException("Brevo API key is not configured.");
    }

    public async System.Threading.Tasks.Task SendInviteEmail(string toEmail, string familyName, string inviteLink)
    {
      var subject = $"You’re Invited to Join {familyName} on Steady Rise";
      var htmlContent = $@"
                    <h1>Join {familyName} on Steady Rise</h1>
                    <p>You’ve been invited to collaborate on budget management. Click below to accept:</p>
                    <a href='{inviteLink}'>Accept Invite</a>
                    <p>This link expires in 7 days.</p>";

      await SendEmail(toEmail, subject, htmlContent);
    }

    public async System.Threading.Tasks.Task SendEmail(string email, string subject, string htmlContent)
    {
      Configuration.Default.ApiKey["api-key"] = _brevoSettings.ApiKey;
      var apiInstance = new TransactionalEmailsApi();

      var sendSmtpEmail = new SendSmtpEmail(
        sender: new SendSmtpEmailSender(_brevoSettings.SenderName, _brevoSettings.SenderEmail),
        to: new List<SendSmtpEmailTo> { new SendSmtpEmailTo(email) },
        subject: subject,
        htmlContent: htmlContent
      );

      try
      {
        await apiInstance.SendTransacEmailAsync(sendSmtpEmail);
        Console.WriteLine($"Verification email sent to {email}");
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Failed to send verification email: {ex.Message}");
        throw;
      }
    }
  }
}