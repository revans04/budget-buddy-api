using Google.Cloud.Firestore;

namespace FamilyBudgetApi.Models
{
  [FirestoreData]
  public class PendingInvite
  {
    [FirestoreProperty("inviterUid")]
    public string InviterUid { get; set; }

    [FirestoreProperty("inviterEmail")]
    public string InviterEmail { get; set; }

    [FirestoreProperty("inviteeEmail")]
    public string InviteeEmail { get; set; }

    [FirestoreProperty("token")]
    public string Token { get; set; } // Unique token for the invite link

    [FirestoreProperty("createdAt")]
    public Timestamp CreatedAt { get; set; }

    [FirestoreProperty("expiresAt")]
    public Timestamp ExpiresAt { get; set; } // Expiration for security
  }


  public class InviteRequest
  {
    public string? InviterUid { get; set; }
    public string? InviterEmail { get; set; }
    public string? InviteeEmail { get; set; }
  }

  public class AcceptInviteRequest
  {
    public string Token { get; set; }
  }
}