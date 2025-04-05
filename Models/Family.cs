using Google.Cloud.Firestore;

namespace FamilyBudgetApi.Models
{

    [FirestoreData]
    public class Family
    {
        [FirestoreProperty("id")]
        public required string Id { get; set; }
        [FirestoreProperty("name")]
        public required string Name { get; set; }
        [FirestoreProperty("ownerUid")]
        public required string OwnerUid { get; set; }
        [FirestoreProperty("members")]
        public List<UserRef> Members { get; set; } = new();
        [FirestoreProperty("memberUids")]
        public List<string> MemberUids { get; set; } = new();
        [FirestoreProperty("createdAt")]
        public Timestamp CreatedAt { get; set; }
        [FirestoreProperty("updatedAt")]
        public Timestamp UpdatedAt { get; set; }
    }

    public class CreateFamilyRequest
    {
        public required string Name { get; set; }
        public required string Email { get; set; }
    }
}