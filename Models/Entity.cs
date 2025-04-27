
using Google.Cloud.Firestore;
using System.Collections.Generic;

namespace FamilyBudgetApi.Models
{
  [FirestoreData]
  public class Entity
  {
    [FirestoreProperty("id")]
    public string Id { get; set; }

    [FirestoreProperty("name")]
    public string Name { get; set; }

    [FirestoreProperty("type")]
    public string Type { get; set; } // e.g., "Family", "Business"

    [FirestoreProperty("members")]
    public List<UserRef> Members { get; set; } = new();
  }
}