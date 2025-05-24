using Google.Cloud.Firestore;
using System.Collections.Generic;

namespace FamilyBudgetApi.Models
{
    // Define the EntityType enum in C#
    public enum EntityType
    {
        Family,
        Business,
        RentalProperty,
        Other
    }

    [FirestoreData]
    public class Entity
    {
        [FirestoreProperty("id")]
        public string Id { get; set; }

        [FirestoreProperty("name")]
        public string Name { get; set; }

        [FirestoreProperty("type")]
        public string Type { get; set; } // Store as string in Firestore

        // Helper property to work with EntityType enum in code
        public EntityType EntityType
        {
            get => Enum.TryParse<EntityType>(Type, true, out var result) ? result : throw new InvalidOperationException($"Invalid EntityType: {Type}");
            set => Type = value.ToString();
        }

        [FirestoreProperty("createdAt")]
        public Timestamp CreatedAt { get; set; }

        [FirestoreProperty("updatedAt")]
        public Timestamp UpdatedAt { get; set; }

        [FirestoreProperty("members")]
        public List<UserRef> Members { get; set; } = new();

        [FirestoreProperty("templateBudget")]
        public TemplateBudget? TemplateBudget { get; set; }

        [FirestoreProperty("taxFormIds")]
        public List<string>? TaxFormIds { get; set; }
    }
}
