using Google.Cloud.Firestore;
using FamilyBudgetApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FamilyBudgetApi.Services
{
  public class FamilyService
  {
    private readonly FirestoreDb _db;

    public FamilyService(FirestoreDb db)
    {
      _db = db;
    }

    public async Task<Family> GetUserFamily(string uid)
    {
      var familyRef = _db.Collection("families").WhereArrayContains("memberUids", uid);
      var snapshot = await familyRef.GetSnapshotAsync();
      return snapshot.Documents.Select(doc => doc.ConvertTo<Family>()).FirstOrDefault();
    }

    public async Task<Family> GetFamilyById(string familyId)
    {
      var doc = await _db.Collection("families").Document(familyId).GetSnapshotAsync();
      return doc.Exists ? doc.ConvertTo<Family>() : null;
    }

    public async Task CreateFamily(string familyId, Family family)
    {
      await _db.Collection("families").Document(familyId).SetAsync(family);
    }

    public async Task AddFamilyMember(string familyId, UserRef member)
    {
      var familyRef = _db.Collection("families").Document(familyId);
      await familyRef.UpdateAsync(new Dictionary<string, object>
            {
                { "members", FieldValue.ArrayUnion(member) },
                { "memberUids", FieldValue.ArrayUnion(member.Uid) },
                { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
            });
    }

    public async Task RemoveFamilyMember(string familyId, string memberUid)
    {
      var familyRef = _db.Collection("families").Document(familyId);
      var family = await GetFamilyById(familyId);
      if (family == null) return;

      var memberToRemove = family.Members.FirstOrDefault(m => m.Uid == memberUid);
      if (memberToRemove != null)
      {
        await familyRef.UpdateAsync(new Dictionary<string, object>
                {
                    { "members", FieldValue.ArrayRemove(memberToRemove) },
                    { "memberUids", FieldValue.ArrayRemove(memberUid) },
                    { "updatedAt", Timestamp.FromDateTime(DateTime.UtcNow) }
                });
      }
    }

    // New methods for pending invites
    public async Task CreatePendingInvite(PendingInvite invite)
    {
      var inviteRef = _db.Collection("pendingInvites").Document(invite.Token);
      await inviteRef.SetAsync(invite);
    }

    public async Task<PendingInvite> GetPendingInviteByToken(string token)
    {
      var doc = await _db.Collection("pendingInvites").Document(token).GetSnapshotAsync();
      return doc.Exists ? doc.ConvertTo<PendingInvite>() : null;
    }

    public async Task DeletePendingInvite(string token)
    {
      await _db.Collection("pendingInvites").Document(token).DeleteAsync();
    }

    public async Task<List<PendingInvite>> GetPendingInvitesByInviter(string inviterUid)
    {
      var query = _db.Collection("pendingInvites").WhereEqualTo("inviterUid", inviterUid);
      var snapshot = await query.GetSnapshotAsync();
      return snapshot.Documents.Select(doc => doc.ConvertTo<PendingInvite>()).ToList();
    }

    public async Task UpdateLastAccessed(string uid)
    {
      await _db.Collection("users").Document(uid).SetAsync(
          new { LastAccessed = Timestamp.FromDateTime(DateTime.UtcNow) },
          SetOptions.MergeAll
      );
    }

    public async Task<Timestamp?> GetLastAccessed(string uid)
    {
      var userDoc = await _db.Collection("users").Document(uid).GetSnapshotAsync();
      return userDoc.Exists && userDoc.ContainsField("lastAccessed")
          ? userDoc.GetValue<Timestamp>("lastAccessed")
          : null;
    }
  }
}
