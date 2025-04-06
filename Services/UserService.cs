using FamilyBudgetApi.Models;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;

namespace FamilyBudgetApi.Services
{

    public class UserService
    {
        private readonly FirestoreDb _db;

        public UserService(FirestoreDb db)
        {
            _db = db;
        }

        public async Task<UserData?> GetUser(string userId)
        {
            var userRef = _db.Collection("users").Document(userId);
            var snapshot = await userRef.GetSnapshotAsync();
            if (!snapshot.Exists) return null;

            return snapshot.ConvertTo<UserData>();
        }

        public async Task<UserData?> GetUserByEmail(string email)
        {
            var q = _db.Collection("users").WhereEqualTo("email", email);
            var snapshot = await q.GetSnapshotAsync();
            if (snapshot.Count == 0) return null;

            return snapshot.Documents[0].ConvertTo<UserData>();
        }

        public async Task SaveUser(string userId, UserData userData)
        {
            var userRef = _db.Collection("users").Document(userId);
            await userRef.SetAsync(userData, Google.Cloud.Firestore.SetOptions.MergeAll);
        }

        public async Task CreatePendingInvite(PendingInvite invite)
        {
            var docRef = _db.Collection("pendingInvites").Document();
            invite.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime());
            await docRef.SetAsync(invite, SetOptions.Overwrite);
        }

        public async Task CreatePendingInviteAsync(PendingInvite invite, string authenticatedUserId)
        {
            if (authenticatedUserId != invite.InviterUid)
                throw new Exception("Unauthorized: Inviter UID does not match authenticated user");

            var docRef = _db.Collection("pendingInvites").Document();
            invite.CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow.ToUniversalTime());
            await docRef.SetAsync(invite, SetOptions.Overwrite);
        }

    }
}
