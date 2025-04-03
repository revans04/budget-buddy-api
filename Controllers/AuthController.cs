using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using sib_api_v3_sdk.Api;
using sib_api_v3_sdk.Client;
using System.Text;
using FamilyBudgetApi.Models;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
  private readonly FirestoreDb _firestoreDb;
  private readonly BrevoSettings _brevoSettings;

  public AuthController(FirestoreDb firestoreDb, IOptions<BrevoSettings> brevoSettings)
  {
    _firestoreDb = firestoreDb;
    _brevoSettings = brevoSettings.Value;
  }

  [HttpPost("google-login")]
  public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
  {
    try
    {
      var payload = await GoogleJsonWebSignature.ValidateAsync(request.GoogleIdToken);
      if (payload == null)
      {
        return BadRequest(new { Error = "Invalid Google ID token" });
      }

      var additionalClaims = new Dictionary<string, object>
      {
        { "email", payload.Email },
        { "name", payload.Name ?? "" },
        { "given_name", payload.GivenName ?? "" },
        { "family_name", payload.FamilyName ?? "" },
        { "picture", payload.Picture ?? "" }
      };
      string customToken = await FirebaseAuth.DefaultInstance.CreateCustomTokenAsync(payload.Subject, additionalClaims);

      UserRecord userRecord;
      try
      {
        userRecord = await FirebaseAuth.DefaultInstance.GetUserAsync(payload.Subject);
        // For existing users, ensure VerificationToken exists if not verified
        if (!userRecord.EmailVerified)
        {
          var userDoc = await _firestoreDb.Collection("users").Document(payload.Subject).GetSnapshotAsync();
          if (userDoc.Exists && !userDoc.ContainsField("VerificationToken"))
          {
            string verificationToken = Guid.NewGuid().ToString();
            await userDoc.Reference.SetAsync(new { VerificationToken = verificationToken }, SetOptions.MergeAll);
            await SendVerificationEmail(payload.Email, verificationToken);
          }
        }
      }
      catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
      {
        var userRecordArgs = new UserRecordArgs
        {
          Uid = payload.Subject,
          Email = payload.Email,
          DisplayName = payload.Name ?? "",
          PhotoUrl = payload.Picture ?? "",
          EmailVerified = false
        };
        userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(userRecordArgs);

        string verificationToken = Guid.NewGuid().ToString();
        await _firestoreDb.Collection("users").Document(payload.Subject)
          .SetAsync(new { VerificationToken = verificationToken }, SetOptions.MergeAll);

        await SendVerificationEmail(payload.Email, verificationToken);
      }

      await _firestoreDb.Collection("users").Document(payload.Subject)
        .SetAsync(new { uid = payload.Subject, email = payload.Email, name = payload.Name ?? "" }, SetOptions.MergeAll);

      return Ok(new { Token = customToken });
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Authentication error: {ex.Message}");
      return BadRequest(new { Error = $"Failed to authenticate: {ex.Message}" });
    }
  }

  [AuthorizeFirebase]
  [HttpPost("ensure-profile")]
  public async Task<IActionResult> EnsureUserProfile()
  {
    try
    {
      string uid = HttpContext.Items["UserId"] as string;
      if (string.IsNullOrEmpty(uid))
      {
        return Unauthorized(new { Error = "User ID not found" });
      }

      var userRef = _firestoreDb.Collection("users").Document(uid);
      await userRef.SetAsync(new
      {
        uid = uid,
        email = userRef.GetSnapshotAsync().Result.GetValue<string>("email") ?? ""
      }, SetOptions.MergeAll);

      return Ok();
    }
    catch (Exception ex)
    {
      return Unauthorized($"Error: {ex.Message}");
    }
  }

  [HttpGet("verify-email")]
  public async Task<IActionResult> VerifyEmail(string token)
  {
    try
    {
      var userQuery = await _firestoreDb.Collection("users")
        .WhereEqualTo("VerificationToken", token)
        .Limit(1)
        .GetSnapshotAsync();

      if (userQuery.Count == 0)
      {
        return BadRequest(new { Error = "Invalid or expired verification token" });
      }

      var userDoc = userQuery.Documents[0];
      string uid = userDoc.GetValue<string>("uid");

      await FirebaseAuth.DefaultInstance.UpdateUserAsync(new UserRecordArgs
      {
        Uid = uid,
        EmailVerified = true
      });

      await userDoc.Reference.UpdateAsync("VerificationToken", null);

      return Ok(new { Message = "Email verified successfully" });
    }
    catch (Exception ex)
    {
      return BadRequest(new { Error = $"Failed to verify email: {ex.Message}" });
    }
  }

  [AuthorizeFirebase]
  [HttpPost("resend-verification-email")]
  public async Task<IActionResult> ResendVerificationEmail()
  {
    try
    {
      string uid = HttpContext.Items["UserId"] as string;
      if (string.IsNullOrEmpty(uid))
      {
        return Unauthorized(new { Error = "User ID not found" });
      }

      var userRecord = await FirebaseAuth.DefaultInstance.GetUserAsync(uid);
      if (userRecord.EmailVerified)
      {
        return BadRequest(new { Error = "Email is already verified" });
      }

      var userDoc = await _firestoreDb.Collection("users").Document(uid).GetSnapshotAsync();
      if (!userDoc.Exists)
      {
        return BadRequest(new { Error = "User data not found" });
      }

      // Safely handle missing VerificationToken
      string verificationToken = userDoc.TryGetValue<string>("VerificationToken", out var token) ? token : null;
      if (string.IsNullOrEmpty(verificationToken))
      {
        verificationToken = Guid.NewGuid().ToString();
        await userDoc.Reference.SetAsync(new { VerificationToken = verificationToken }, SetOptions.MergeAll);
      }

      await SendVerificationEmail(userRecord.Email, verificationToken);
      return Ok(new { Message = "Verification email resent successfully" });
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error resending verification email: {ex.Message}");
      return BadRequest(new { Error = $"Failed to resend verification email: {ex.Message}" });
    }
  }

  private async Task SendVerificationEmail(string email, string token)
  {
    Configuration.Default.ApiKey["api-key"] = _brevoSettings.ApiKey;
    var apiInstance = new TransactionalEmailsApi();

    var sendSmtpEmail = new sib_api_v3_sdk.Model.SendSmtpEmail(
      sender: new sib_api_v3_sdk.Model.SendSmtpEmailSender(_brevoSettings.SenderName, _brevoSettings.SenderEmail),
      to: new List<sib_api_v3_sdk.Model.SendSmtpEmailTo> { new sib_api_v3_sdk.Model.SendSmtpEmailTo(email) },
      subject: "Verify Your Family Budget Email",
      htmlContent: $"<p>Please verify your email by clicking the link below:</p>" +
                   $"<a href='http://family-budget.local:8080/verify-email?token={token}'>Verify Email</a>"
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

public class GoogleLoginRequest
{
  public string? GoogleIdToken { get; set; }
}