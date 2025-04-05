using Google.Cloud.Firestore;
using FamilyBudgetApi.Models;

namespace FamilyBudgetApi.Services
{
  public class FamilyService
  {
    private readonly FirestoreDb _firestoreDb;

    public FamilyService(FirestoreDb firestoreDb)
    {
      _firestoreDb = firestoreDb;
    }

    public async Task<Family> GetUserFamily(string uid)
    {
      var familyQuery = await _firestoreDb.Collection("families")
          .WhereArrayContains("memberUids", uid)
          .Limit(1)
          .GetSnapshotAsync();
      if (familyQuery.Count == 0) return null;
      return familyQuery.Documents[0].ConvertTo<Family>();
    }

    public async Task<Family> GetFamilyById(string familyId)
    {
      var familyRef = _firestoreDb.Collection("families").Document(familyId);
      var snapshot = await familyRef.GetSnapshotAsync();
      if (!snapshot.Exists) return null;
      return snapshot.ConvertTo<Family>();
    }

    public async Task CreateFamily(string familyId, Family family)
    {
      var familyRef = _firestoreDb.Collection("families").Document(familyId);
      await familyRef.SetAsync(family);
    }

    public async Task AddFamilyMember(string familyId, UserRef member)
    {
      var familyRef = _firestoreDb.Collection("families").Document(familyId);
      var snapshot = await familyRef.GetSnapshotAsync();
      if (!snapshot.Exists) throw new Exception("Family not found");

      var family = snapshot.ConvertTo<Family>();
      if (!family.Members.Any(m => m.Uid == member.Uid))
      {
        family.Members.Add(member);
        family.MemberUids.Add(member.Uid);
        family.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        await familyRef.SetAsync(family, SetOptions.MergeAll);
      }
    }

    public async Task RemoveFamilyMember(string familyId, string memberUid)
    {
      var familyRef = _firestoreDb.Collection("families").Document(familyId);
      var snapshot = await familyRef.GetSnapshotAsync();
      if (!snapshot.Exists) throw new Exception("Family not found");

      var family = snapshot.ConvertTo<Family>();
      family.Members.RemoveAll(m => m.Uid == memberUid);
      family.MemberUids.RemoveAll(m => m == memberUid);
      family.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
      await familyRef.SetAsync(family, SetOptions.MergeAll);
    }
  }
}