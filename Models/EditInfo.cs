using Google.Cloud.Firestore;

namespace FamilyBudgetApi.Models
{


    [FirestoreData]
    public class EditEvent
    {
        [FirestoreProperty("userId")]
        public string? UserId { get; set; }
        [FirestoreProperty("userEmail")]
        public string? UserEmail { get; set; }
        [FirestoreProperty("timestamp")]
        public DateTime Timestamp { get; set; }
        [FirestoreProperty("action")]
        public string? Action { get; set; }
    }
}