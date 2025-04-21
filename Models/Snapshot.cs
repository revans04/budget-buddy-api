// FamilyBudgetApi/Models/Snapshot.cs
using Google.Cloud.Firestore;
using System.Collections.Generic;

namespace FamilyBudgetApi.Models
{
   [FirestoreData]
    public class Snapshot
    {
        [FirestoreProperty]
        public string Id { get; set; }

        [FirestoreProperty]
        public Timestamp Date { get; set; }

        [FirestoreProperty]
        public List<SnapshotAccount> Accounts { get; set; }

        [FirestoreProperty]
        public double NetWorth { get; set; }

        [FirestoreProperty]
        public Timestamp CreatedAt { get; set; }
    }

    [FirestoreData]
    public class SnapshotAccount
    {
        [FirestoreProperty]
        public string AccountId { get; set; }

        [FirestoreProperty]
        public string AccountName { get; set; }

        [FirestoreProperty]
        public double Value { get; set; }

        [FirestoreProperty]
        public string Type { get; set; } 
    }
}