using Google.Cloud.Firestore;

namespace FamilyBudgetApi.Models
{

    [FirestoreData]
    public class ImportedTransaction
    {
        [FirestoreProperty("id")]
        public string? Id { get; set; }

        [FirestoreProperty]
        public string AccountId { get; set; }

        [FirestoreProperty("accountNumber")]
        public string? AccountNumber { get; set; }

        [FirestoreProperty("accountSource")]
        public string? AccountSource { get; set; }

        [FirestoreProperty("payee")]
        public string? Payee { get; set; }

        [FirestoreProperty("postedDate")]
        public string? PostedDate { get; set; }

        [FirestoreProperty("amount")]
        public double? Amount { get; set; }

        [FirestoreProperty("status")]
        public string? Status { get; set; }

        [FirestoreProperty("matched")]
        public bool Matched { get; set; }

        [FirestoreProperty("ignored")]
        public bool Ignored { get; set; }

        [FirestoreProperty("debitAmount")]
        public double? DebitAmount { get; set; }
        
        [FirestoreProperty("creditAmount")]
        public double? CreditAmount { get; set; }

        [FirestoreProperty("checkNumber")]
        public string? CheckNumber { get; set; }

        [FirestoreProperty("deleted")]
        public bool? Deleted { get; set; }
    }

    [FirestoreData]
    public class ImportedTransactionDoc
    {
        public string DocumentId { get; set; }

        [FirestoreProperty("id")]
        public string DocId { get; set; } // Maps to the Firestore "id" field (e.g., "c2cf43c9-08a5-4f40-bdbc-195797a91bc1")

        [FirestoreProperty("familyId")]
        public string FamilyId { get; set; }

        [FirestoreProperty("userId")] // Who imported it
        public string UserId { get; set; }

        [FirestoreProperty("importedTransactions")]
        public ImportedTransaction[] importedTransactions { get; set; }
    }
}