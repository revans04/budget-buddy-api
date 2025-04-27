using Google.Cloud.Firestore;

namespace FamilyBudgetApi.Models
{

    [FirestoreData]
    public class UserRef
    {
        [FirestoreProperty("uid")]
        public string? Uid { get; set; }

        [FirestoreProperty("email")]
        public string? Email { get; set; }
        
        [FirestoreProperty("role")]
        public string? Role { get; set; }
    }

    [FirestoreData]
    public class UserData
    {
        [FirestoreProperty("uid")]
        public string? Uid { get; set; }
        [FirestoreProperty("email")]
        public string? Email { get; set; }
    }

    [FirestoreData]
    public class SharedAccess
    {
        [FirestoreProperty("canEdit")]
        public bool CanEdit { get; set; }
    }
}