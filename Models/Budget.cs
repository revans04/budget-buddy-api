using Google.Cloud.Firestore;

namespace FamilyBudgetApi.Models
{
    [FirestoreData]
    public class BudgetInfo : Budget
    {
        [FirestoreProperty("isOwner")]
        public bool IsOwner { get; set; }
    }

    [FirestoreData]
    public class Budget
    {
        [FirestoreProperty("budgetId")]
        public string BudgetId { get; set; } = string.Empty;

        [FirestoreProperty("familyId")]
        public required string FamilyId { get; set; }

        [FirestoreProperty("entityId")]
        public string? EntityId { get; set; } // New field

        [FirestoreProperty("month")]
        public required string Month { get; set; }

        [FirestoreProperty("label")]
        public string? Label { get; set; }

        [FirestoreProperty("incomeTarget")]
        public double IncomeTarget { get; set; }

        [FirestoreProperty("categories")]
        public List<BudgetCategory> Categories { get; set; } = new();

        [FirestoreProperty("transactions")]
        public List<Transaction> Transactions { get; set; } = new();

        [FirestoreProperty("sharedWith")]
        public string? OriginalBudgetId { get; set; }

        [FirestoreProperty("merchants")]
        public List<Merchant> Merchants { get; set; } = new();
    }

    [FirestoreData]
    public class BudgetCategory
    {
        [FirestoreProperty("name")]
        public string? Name { get; set; }
        [FirestoreProperty("target")]
        public double Target { get; set; }
        [FirestoreProperty("isFund")]
        public bool IsFund { get; set; }
        [FirestoreProperty("group")]
        public string? Group { get; set; }
        [FirestoreProperty("carryover")]
        public double? Carryover { get; set; }
    }

    [FirestoreData]
    public class Transaction
    {
        [FirestoreProperty("id")]
        public string? Id { get; set; }

        [FirestoreProperty("date")]
        public string? Date { get; set; }

        [FirestoreProperty("budgetMonth")]
        public string? BudgetMonth { get; set; }

        [FirestoreProperty("merchant")]
        public string? Merchant { get; set; }

        [FirestoreProperty("categories")]
        public List<TransactionCategory>? Categories { get; set; }

        [FirestoreProperty("amount")]
        public double Amount { get; set; }

        [FirestoreProperty("notes")]
        public string? Notes { get; set; }

        [FirestoreProperty("recurring")]
        public bool Recurring { get; set; }

        [FirestoreProperty("recurringInterval")]
        public string? RecurringInterval { get; set; }

        [FirestoreProperty("userId")]
        public string? UserId { get; set; }

        [FirestoreProperty("isIncome")]
        public bool IsIncome { get; set; }

        [FirestoreProperty("accountNumber")]
        public string? AccountNumber { get; set; }

        [FirestoreProperty("accountSource")]
        public string? AccountSource { get; set; }

        [FirestoreProperty("postedDate")]
        public string? PostedDate { get; set; }

        [FirestoreProperty("importedMerchant")]
        public string? ImportedMerchant { get; set; }

        [FirestoreProperty("status")]
        public string? Status { get; set; }

        [FirestoreProperty("checkNumber")]
        public string? CheckNumber { get; set; }

        [FirestoreProperty("deleted")]
        public bool? Deleted { get; set; }

        [FirestoreProperty("entityId")]
        public string? EntityId { get; set; } // New field
    }

    [FirestoreData]
    public class TransactionCategory
    {
        [FirestoreProperty("category")]
        public string? Category { get; set; }
        [FirestoreProperty("amount")]
        public double Amount { get; set; }
    }


    public class TransactionWithBudgetId
    {
        public string BudgetId { get; set; }
        public Transaction Transaction { get; set; }
    }

    [FirestoreData]
    public class SharedBudget
    {
        [FirestoreProperty("ownerUid")]
        public string? OwnerUid { get; set; }
        [FirestoreProperty("userId")]
        public string? UserId { get; set; }
        [FirestoreProperty("budgetIds")]
        public List<string> BudgetIds { get; set; } = new();
    }

    [FirestoreData]
    public class Merchant
    {
        [FirestoreProperty("name")]
        public string? Name { get; set; }
        [FirestoreProperty("usageCount")]
        public int UsageCount { get; set; }
    }

    public class ReconcileRequest
    {
        public string BudgetTransactionId { get; set; }
        public string ImportedTransactionId { get; set; }
        public bool Match { get; set; }
        public bool Ignore { get; set; }
    }

    public class BatchReconcileRequest
    {
        public string BudgetId { get; set; }
        public List<ReconcileRequest> Reconciliations { get; set; }
    }

    [FirestoreData]
    public class TemplateBudget
    {
        [FirestoreProperty("categories")]
        public List<BudgetCategory> Categories { get; set; } = new();
    }
}
