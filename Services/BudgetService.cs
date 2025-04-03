using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FamilyBudgetApi.Services;

public class BudgetService
{
    private readonly FirestoreDb _db;

    public BudgetService(FirestoreDb db)
    {
        _db = db;
    }

    public async Task<List<BudgetInfo>> LoadAccessibleBudgets(string userId)
    {
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("User ID is required");
        var budgets = new List<BudgetInfo>();
        var sharedBudgets = await GetSharedBudgets(userId);
        foreach (var shared in sharedBudgets)
        {
            foreach (var budgetId in shared.BudgetIds)
            {
                var month = budgetId.Split("_")[1];
                var budget = await GetBudget(budgetId);
                if (budget != null)
                {
                    budgets.Add(new BudgetInfo
                    {
                        BudgetId = budgetId,
                        IsOwner = false,
                        UserId = shared.OwnerUid,
                        Label = $"Shared Budget ({month})",
                        Month = month,
                        IncomeTarget = budget.IncomeTarget,
                        Categories = budget.Categories,
                        Transactions = budget.Transactions,
                        SharedWith = budget.SharedWith,
                        SharedWithUids = budget.SharedWithUids,
                        OriginalBudgetId = budget.OriginalBudgetId,
                        Merchants = budget.Merchants
                    });
                }
            }
        }

        var budgetsQuery = _db.Collection("budgets").WhereEqualTo("userId", userId);
        var querySnapshot = await budgetsQuery.GetSnapshotAsync();
        foreach (var doc in querySnapshot.Documents)
        {
            var budget = await GetBudget(doc.Id);
            if (budget != null)
            {
                var month = doc.Id.Split("_")[1];
                budgets.Add(new BudgetInfo
                {
                    BudgetId = doc.Id,
                    IsOwner = true,
                    UserId = userId,
                    Label = $"My Budget ({month})",
                    Month = month,
                    IncomeTarget = budget.IncomeTarget,
                    Categories = budget.Categories,
                    Transactions = budget.Transactions,
                    SharedWith = budget.SharedWith,
                    SharedWithUids = budget.SharedWithUids,
                    OriginalBudgetId = budget.OriginalBudgetId,
                    Merchants = budget.Merchants
                });
            }
        }

        return budgets;
    }

    public async Task<Budget?> GetBudget(string budgetId)
    {
        var budgetRef = _db.Collection("budgets").Document(budgetId);
        try
        {
            var budgetSnap = await budgetRef.GetSnapshotAsync();
            if (!budgetSnap.Exists) return null;

            return budgetSnap.ConvertTo<Budget>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetBudget: {ex.Message}");
            return null;
        }
    }

    public async Task<List<SharedBudget>> GetSharedBudgets(string userId)
    {
        var q = _db.Collection("sharedBudgets").WhereEqualTo("userId", userId);
        var snapshot = await q.GetSnapshotAsync();
        return snapshot.Documents.Select(doc => doc.ConvertTo<SharedBudget>()).ToList();
    }

    public async Task SaveBudget(string budgetId, Budget budget, string userId, string userEmail)
    {
        var budgetRef = _db.Collection("budgets").Document(budgetId);
        await budgetRef.SetAsync(budget, SetOptions.MergeAll);
        await LogEditEvent(budgetId, userId, userEmail, "update_budget");
    }

    public async Task<List<EditEvent>> GetEditHistory(string budgetId, DateTime since)
    {
        var editHistoryRef = _db.Collection("budgets").Document(budgetId).Collection("editHistory");
        var query = editHistoryRef.WhereGreaterThanOrEqualTo("timestamp", Timestamp.FromDateTime(since.ToUniversalTime()));
        var snapshot = await query.GetSnapshotAsync();
        return snapshot.Documents.Select(doc => doc.ConvertTo<EditEvent>()).ToList();
    }

    public async Task AddTransaction(string budgetId, Transaction transaction, string userId, string userEmail)
    {
        var budget = await GetBudget(budgetId) ?? throw new Exception($"Budget {budgetId} not found");
        transaction.Id ??= _db.Collection("budgets").Document().Id;
        budget.Transactions.Add(transaction);
        if (!string.IsNullOrEmpty(transaction.Merchant)) await UpdateMerchants(budgetId, transaction.Merchant, 1);
        await SaveBudget(budgetId, budget, userId, userEmail.Replace("update_budget", "add_transaction"));
    }

    public async Task SaveTransaction(string budgetId, Transaction transaction, string userId, string userEmail)
    {
        var budget = await GetBudget(budgetId) ?? throw new Exception($"Budget {budgetId} not found");
        var index = budget.Transactions.FindIndex(t => t.Id == transaction.Id);
        string? oldMerchant = null;
        if (index >= 0)
        {
            oldMerchant = budget.Transactions[index].Merchant;
            budget.Transactions[index] = transaction;
        }
        else
        {
            transaction.Id ??= _db.Collection("budgets").Document().Id;
            budget.Transactions.Add(transaction);
        }

        if (!string.IsNullOrEmpty(oldMerchant) && oldMerchant != transaction.Merchant)
            await UpdateMerchants(budgetId, oldMerchant, -1);
        if (!string.IsNullOrEmpty(transaction.Merchant) && (oldMerchant == null || oldMerchant != transaction.Merchant))
            await UpdateMerchants(budgetId, transaction.Merchant, 1);

        await SaveBudget(budgetId, budget, userId, userEmail.Replace("update_budget", transaction.Id != null ? "update_transaction" : "add_transaction"));
    }

    public async Task DeleteTransaction(string budgetId, string transactionId, string userId, string userEmail)
    {
        var budget = await GetBudget(budgetId) ?? throw new Exception($"Budget {budgetId} not found");
        var transaction = budget.Transactions.FirstOrDefault(t => t.Id == transactionId) ?? throw new Exception($"Transaction {transactionId} not found");
        budget.Transactions.Remove(transaction);
        if (!string.IsNullOrEmpty(transaction.Merchant)) await UpdateMerchants(budgetId, transaction.Merchant, -1);
        await SaveBudget(budgetId, budget, userId, userEmail.Replace("update_budget", "delete_transaction"));
    }

    private async Task UpdateMerchants(string budgetId, string merchantName, int increment)
    {
        var budgetRef = _db.Collection("budgets").Document(budgetId);
        var budgetSnap = await budgetRef.GetSnapshotAsync();
        if (!budgetSnap.Exists) throw new Exception($"Budget {budgetId} not found");

        var budget = budgetSnap.ConvertTo<Budget>();
        var merchant = budget.Merchants.FirstOrDefault(m => m.Name == merchantName);
        if (merchant != null)
        {
            merchant.UsageCount += increment;
            if (merchant.UsageCount <= 0) budget.Merchants.Remove(merchant);
        }
        else if (increment > 0)
        {
            budget.Merchants.Add(new Merchant { Name = merchantName, UsageCount = increment });
        }
        budget.Merchants.Sort((a, b) => b.UsageCount.CompareTo(a.UsageCount));
        await budgetRef.SetAsync(budget, SetOptions.MergeAll);
    }

    private async Task LogEditEvent(string budgetId, string userId, string userEmail, string action)
    {
        var editEventRef = _db.Collection("budgets").Document(budgetId).Collection("editHistory").Document();
        await editEventRef.SetAsync(new EditEvent
        {
            UserId = userId ?? "",
            UserEmail = userEmail ?? "",
            Timestamp = DateTime.UtcNow,
            Action = action ?? ""
        });
    }

    public async Task<string> SaveImportedTransactions(string userId, ImportedTransactionDoc doc)
    {
        var docRef = _db.Collection("importedTransactions").Document();
        doc.UserId = userId; // Ensure userId is set
        await docRef.SetAsync(doc, SetOptions.Overwrite);
        return docRef.Id;
    }

    public async Task UpdateImportedTransaction(string docId, string transactionId, ImportedTransaction updates)
    {
        var docRef = _db.Collection("importedTransactions").Document(docId);
        var docSnap = await docRef.GetSnapshotAsync();
        if (!docSnap.Exists) throw new Exception($"Imported transaction doc {docId} not found");

        var doc = docSnap.ConvertTo<ImportedTransactionDoc>();
        var tx = doc.ImportedTransactions.FirstOrDefault(t => t.Id == transactionId) ?? throw new Exception($"Imported transaction {transactionId} not found");

        tx.AccountNumber = updates.AccountNumber ?? tx.AccountNumber;
        tx.AccountSource = updates.AccountSource ?? tx.AccountSource;
        tx.Payee = updates.Payee ?? tx.Payee;
        tx.PostedDate = updates.PostedDate ?? tx.PostedDate;
        tx.Amount = updates.Amount != 0 ? updates.Amount : tx.Amount;
        tx.Status = updates.Status ?? tx.Status;
        tx.Matched = updates.Matched != tx.Matched ? updates.Matched : tx.Matched;
        tx.Ignored = updates.Ignored != tx.Ignored ? updates.Ignored : tx.Ignored;
        tx.DebitAmount = updates.DebitAmount ?? tx.DebitAmount;
        tx.CreditAmount = updates.CreditAmount ?? tx.CreditAmount;

        await docRef.SetAsync(doc, SetOptions.MergeAll);
    }

    public async Task<List<ImportedTransactionDoc>> GetImportedTransactionDocs(string userId)
    {
        var q = _db.Collection("importedTransactions").WhereEqualTo("userId", userId);
        var snapshot = await q.GetSnapshotAsync();
        return snapshot.Documents.Select(doc => doc.ConvertTo<ImportedTransactionDoc>()).ToList();
    }

    public async Task UpdateSharedBudgets(string ownerUid, string sharedUid, List<string> newBudgetIds)
    {
        var q = _db.Collection("sharedBudgets").WhereEqualTo("userId", sharedUid).WhereEqualTo("ownerUid", ownerUid);
        var snapshot = await q.GetSnapshotAsync();
        if (snapshot.Count == 0)
        {
            var docRef = _db.Collection("sharedBudgets").Document();
            await docRef.SetAsync(new SharedBudget { UserId = sharedUid, OwnerUid = ownerUid, BudgetIds = newBudgetIds }, SetOptions.Overwrite);
        }
        else
        {
            var docRef = snapshot.Documents[0].Reference;
            var doc = snapshot.Documents[0].ConvertTo<SharedBudget>();
            doc.BudgetIds = doc.BudgetIds.Union(newBudgetIds).Distinct().ToList();
            await docRef.SetAsync(doc, SetOptions.MergeAll);
        }
    }
}

[FirestoreData]
public class BudgetInfo : Budget
{
    [FirestoreProperty("budgetId")]
    public string BudgetId { get; set; } = string.Empty;
    [FirestoreProperty("isOwner")]
    public bool IsOwner { get; set; }
}

[FirestoreData]
public class Budget
{
    [FirestoreProperty("userId")]
    public string? UserId { get; set; }
    [FirestoreProperty("label")]
    public string? Label { get; set; }
    [FirestoreProperty("month")]
    public string? Month { get; set; }
    [FirestoreProperty("incomeTarget")]
    public double IncomeTarget { get; set; }
    [FirestoreProperty("categories")]
    public List<BudgetCategory> Categories { get; set; } = new();
    [FirestoreProperty("transactions")]
    public List<Transaction> Transactions { get; set; } = new();
    [FirestoreProperty("sharedWith")]
    public List<UserRef> SharedWith { get; set; } = new();
    [FirestoreProperty("sharedWithUids")]
    public List<string> SharedWithUids { get; set; } = new();
    [FirestoreProperty("originalBudgetId")]
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
    public List<TransactionCategory> Categories { get; set; } = new();
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
}

[FirestoreData]
public class TransactionCategory
{
    [FirestoreProperty("category")]
    public string? Category { get; set; }
    [FirestoreProperty("amount")]
    public double Amount { get; set; }
}

[FirestoreData]
public class EditEvent
{
    [FirestoreProperty("userId")]
    public string? UserId { get; set; }
    [FirestoreProperty("userEmail")]
    public string? UserEmail { get; set; }
    [FirestoreProperty("timestamp")]
    public DateTime Timestamp { get; set; }
    [FirestoreProperty("action")]
    public string? Action { get; set; }
}

[FirestoreData]
public class Merchant
{
    [FirestoreProperty("name")]
    public string? Name { get; set; }
    [FirestoreProperty("usageCount")]
    public int UsageCount { get; set; }
}

[FirestoreData]
public class UserRef
{
    [FirestoreProperty("uid")]
    public string? Uid { get; set; }
    [FirestoreProperty("email")]
    public string? Email { get; set; }
}

[FirestoreData]
public class ImportedTransaction
{
    [FirestoreProperty("id")]
    public string? Id { get; set; }
    [FirestoreProperty("accountNumber")]
    public string? AccountNumber { get; set; }
    [FirestoreProperty("accountSource")]
    public string? AccountSource { get; set; }
    [FirestoreProperty("payee")]
    public string? Payee { get; set; }
    [FirestoreProperty("postedDate")]
    public string? PostedDate { get; set; }
    [FirestoreProperty("amount")]
    public double Amount { get; set; }
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
}

[FirestoreData]
public class ImportedTransactionDoc
{
    [FirestoreProperty("id")]
    public string? Id { get; set; }
    [FirestoreProperty("userId")]
    public string? UserId { get; set; }
    [FirestoreProperty("sharedWith")]
    public List<UserRef> SharedWith { get; set; } = new();
    [FirestoreProperty("sharedWithUids")]
    public List<string> SharedWithUids { get; set; } = new();
    [FirestoreProperty("importedTransactions")]
    public List<ImportedTransaction> ImportedTransactions { get; set; } = new();
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