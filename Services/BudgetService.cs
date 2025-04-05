//Services/BudgetService.cs
using Google.Cloud.Firestore;
using FamilyBudgetApi.Models;
using System.Reflection;

namespace FamilyBudgetApi.Services
{

    public class BudgetService
    {
        private readonly FirestoreDb _firestoreDb;
        private readonly FamilyService _familyService;
        private static readonly PropertyInfo[] ImportedTransactionProperties = typeof(ImportedTransaction).GetProperties();


        public BudgetService(FirestoreDb firestoreDb, FamilyService familyService)
        {
            _firestoreDb = firestoreDb;
            _familyService = familyService;
        }

        public async Task<List<BudgetInfo>> LoadAccessibleBudgets(string userId)
        {
            var family = await _familyService.GetUserFamily(userId);
            if (family == null) return new List<BudgetInfo>();

            var budgetsQuery = await _firestoreDb.Collection("budgets")
                .WhereEqualTo("familyId", family.Id)
                .GetSnapshotAsync();

            var budgets = budgetsQuery.Documents
                .Select(doc =>
                {
                    var budget = doc.ConvertTo<BudgetInfo>();
                    budget.BudgetId = doc.Id; // Set BudgetId here
                    budget.IsOwner = family.OwnerUid == userId; // Set IsOwner here
                    return budget;
                })
                .ToList();

            return budgets;
        }

        public async Task<Budget?> GetBudget(string budgetId)
        {
            var budgetRef = _firestoreDb.Collection("budgets").Document(budgetId);
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
            var q = _firestoreDb.Collection("sharedBudgets").WhereEqualTo("userId", userId);
            var snapshot = await q.GetSnapshotAsync();
            return snapshot.Documents.Select(doc => doc.ConvertTo<SharedBudget>()).ToList();
        }

        public async Task SaveBudget(string budgetId, Budget budget, string userId, string userEmail)
        {
            var familyQuery = _firestoreDb.Collection("families")
                .WhereArrayContains("memberUids", userId)
                .WhereEqualTo("id", budget.FamilyId);
            if (!(await familyQuery.GetSnapshotAsync()).Any())
                throw new Exception("User not part of this family");
            await _firestoreDb.Collection("budgets").Document(budgetId).SetAsync(budget, SetOptions.MergeAll);
            await LogEditEvent(budgetId, userId, userEmail, "update_budget"); // Add action parameter if needed
        }

        public async Task<List<EditEvent>> GetEditHistory(string budgetId, DateTime since)
        {
            var editHistoryRef = _firestoreDb.Collection("budgets").Document(budgetId).Collection("editHistory");
            var query = editHistoryRef.WhereGreaterThanOrEqualTo("timestamp", Timestamp.FromDateTime(since.ToUniversalTime()));
            var snapshot = await query.GetSnapshotAsync();
            return snapshot.Documents.Select(doc => doc.ConvertTo<EditEvent>()).ToList();
        }

        public async Task AddTransaction(string budgetId, Models.Transaction transaction, string userId, string userEmail)
        {
            var budget = await GetBudget(budgetId) ?? throw new Exception($"Budget {budgetId} not found");
            transaction.Id ??= _firestoreDb.Collection("budgets").Document().Id;
            budget.Transactions.Add(transaction);
            if (!string.IsNullOrEmpty(transaction.Merchant))
            {
                Console.WriteLine($"Adding merchant {transaction.Merchant} for budget {budgetId}");
                await UpdateMerchants(budgetId, transaction.Merchant, 1);
            }
            await SaveBudget(budgetId, budget, userId, userEmail.Replace("update_budget", "add_transaction"));
        }

        public async Task SaveTransaction(string budgetId, Models.Transaction transaction, string userId, string userEmail)
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
                transaction.Id ??= _firestoreDb.Collection("budgets").Document().Id;
                budget.Transactions.Add(transaction);
            }

            if (!string.IsNullOrEmpty(oldMerchant) && oldMerchant != transaction.Merchant)
            {
                Console.WriteLine($"Decreasing count for old merchant {oldMerchant} in budget {budgetId}");
                await UpdateMerchants(budgetId, oldMerchant, -1);
            }
            if (!string.IsNullOrEmpty(transaction.Merchant) && (oldMerchant == null || oldMerchant != transaction.Merchant))
            {
                Console.WriteLine($"Increasing count for new merchant {transaction.Merchant} in budget {budgetId}");
                await UpdateMerchants(budgetId, transaction.Merchant, 1);
            }

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
            var budgetRef = _firestoreDb.Collection("budgets").Document(budgetId);
            var budgetSnap = await budgetRef.GetSnapshotAsync();
            if (!budgetSnap.Exists) throw new Exception($"Budget {budgetId} not found");

            var budget = budgetSnap.ConvertTo<Budget>();
            if (budget.Merchants == null) // Initialize if null
            {
                budget.Merchants = new List<Merchant>();
                Console.WriteLine($"Initialized Merchants list for budget {budgetId}");
            }

            var merchant = budget.Merchants.FirstOrDefault(m => m.Name == merchantName);
            if (merchant != null)
            {
                merchant.UsageCount += increment;
                Console.WriteLine($"Updated {merchantName} count to {merchant.UsageCount}");
                if (merchant.UsageCount <= 0)
                {
                    budget.Merchants.Remove(merchant);
                    Console.WriteLine($"Removed {merchantName} from budget {budgetId}");
                }
            }
            else if (increment > 0)
            {
                budget.Merchants.Add(new Merchant { Name = merchantName, UsageCount = increment });
                Console.WriteLine($"Added {merchantName} with count {increment} to budget {budgetId}");
            }
            budget.Merchants.Sort((a, b) => b.UsageCount.CompareTo(a.UsageCount));
            await budgetRef.SetAsync(budget, SetOptions.MergeAll);
        }

        private async Task LogEditEvent(string budgetId, string userId, string userEmail, string action)
        {
            var editEventRef = _firestoreDb.Collection("budgets").Document(budgetId).Collection("editHistory").Document();
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
            var docRef = _firestoreDb.Collection("importedTransactions").Document(doc.DocumentId);
            doc.UserId = userId; // Ensure userId is set
            doc.DocId = doc.DocumentId; // Ensure DocId matches DocumentId
            await docRef.SetAsync(doc, SetOptions.Overwrite);
            return doc.DocumentId;
        }


        public async Task<List<ImportedTransactionDoc>> GetImportedTransactions(string userId)
        {
            var family = await _familyService.GetUserFamily(userId);
            if (family == null) return new List<ImportedTransactionDoc>();

            var importedDocs = await _firestoreDb.Collection("importedTransactions")
                .WhereEqualTo("familyId", family.Id)
                .GetSnapshotAsync();

            return importedDocs.Documents
                .Select(doc =>
                {
                    var importedDoc = doc.ConvertTo<ImportedTransactionDoc>();
                    importedDoc.DocumentId = doc.Id; // Set the Firestore document ID
                    return importedDoc;
                })
                .ToList();
        }

        public async Task UpdateImportedTransaction(string docId, string transactionId, bool? matched = null, bool? ignored = null)
        {
            var docRef = _firestoreDb.Collection("importedTransactions").Document(docId);
            var snapshot = await docRef.GetSnapshotAsync();
            if (!snapshot.Exists) throw new Exception($"Imported transaction document {docId} not found");

            var docData = snapshot.ConvertTo<ImportedTransactionDoc>();
            var transactions = docData.importedTransactions.ToList();
            var index = transactions.FindIndex(tx => tx.Id == transactionId);
            if (index == -1) throw new Exception($"Imported transaction {transactionId} not found in document {docId}");

            var existingTransaction = transactions[index];
            var updatedTransaction = new ImportedTransaction();

            // Use cached PropertyInfo to copy all properties from the existing transaction
            foreach (var prop in ImportedTransactionProperties)
            {
                if (prop.CanWrite)
                {
                    var value = prop.GetValue(existingTransaction);
                    prop.SetValue(updatedTransaction, value);
                }
            }

            // Update the specified fields
            if (matched.HasValue)
            {
                updatedTransaction.Matched = matched.Value;
            }
            if (ignored.HasValue)
            {
                updatedTransaction.Ignored = ignored.Value;
            }

            transactions[index] = updatedTransaction;
            await docRef.SetAsync(new { importedTransactions = transactions }, SetOptions.MergeAll);
        }

        public async Task<List<ImportedTransactionDoc>> GetImportedTransactionDocs(string userId)
        {
            var q = _firestoreDb.Collection("importedTransactions").WhereEqualTo("userId", userId);
            var snapshot = await q.GetSnapshotAsync();
            return snapshot.Documents.Select(doc => doc.ConvertTo<ImportedTransactionDoc>()).ToList();
        }

        public async Task UpdateSharedBudgets(string ownerUid, string sharedUid, List<string> newBudgetIds)
        {
            var q = _firestoreDb.Collection("sharedBudgets").WhereEqualTo("userId", sharedUid).WhereEqualTo("ownerUid", ownerUid);
            var snapshot = await q.GetSnapshotAsync();
            if (snapshot.Count == 0)
            {
                var docRef = _firestoreDb.Collection("sharedBudgets").Document();
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

        public async Task BatchReconcileTransactions(string budgetId, List<ReconcileRequest> requests, string userId, string userEmail)
        {
            var startTime = DateTime.UtcNow;
            Console.WriteLine($"Starting batch reconcile for {requests.Count} transactions");

            var budget = await GetBudget(budgetId) ?? throw new Exception($"Budget {budgetId} not found");
            var importedDocs = await GetImportedTransactions(userId);
            var batch = _firestoreDb.StartBatch();
            var updatedDocs = new Dictionary<string, ImportedTransactionDoc>();

            foreach (var req in requests)
            {
                var txIndex = budget.Transactions.FindIndex(t => t.Id == req.Transaction.Id);
                if (txIndex < 0) throw new Exception($"Transaction {req.Transaction.Id} not found in budget {budgetId}");

                var existingTx = budget.Transactions[txIndex];
                budget.Transactions[txIndex] = new Models.Transaction
                {
                    Id = existingTx.Id,
                    Date = existingTx.Date,
                    BudgetMonth = existingTx.BudgetMonth,
                    Merchant = existingTx.Merchant,
                    Categories = existingTx.Categories,
                    Amount = existingTx.Amount,
                    Notes = existingTx.Notes,
                    Recurring = existingTx.Recurring,
                    RecurringInterval = existingTx.RecurringInterval,
                    UserId = existingTx.UserId,
                    IsIncome = existingTx.IsIncome,
                    AccountSource = req.Transaction.AccountSource ?? existingTx.AccountSource,
                    AccountNumber = req.Transaction.AccountNumber ?? existingTx.AccountNumber,
                    PostedDate = req.Transaction.PostedDate ?? existingTx.PostedDate,
                    ImportedMerchant = req.Transaction.ImportedMerchant ?? existingTx.ImportedMerchant,
                    Status = req.Transaction.Status ?? existingTx.Status,
                    CheckNumber = existingTx.CheckNumber // Preserve if not sent
                };

                var targetDoc = importedDocs.FirstOrDefault(doc => doc.importedTransactions.Any(itx => itx.Id == req.ImportedTxId));
                if (targetDoc == null)
                {
                    Console.WriteLine($"ImportedTransactionDoc not found for ImportedTxId: {req.ImportedTxId}");
                    continue;
                }

                var docId = targetDoc.DocumentId;
                if (!updatedDocs.ContainsKey(docId)) updatedDocs[docId] = targetDoc;

                var transactions = updatedDocs[docId].importedTransactions.ToList();
                var txIdx = transactions.FindIndex(tx => tx.Id == req.ImportedTxId);
                if (txIdx == -1)
                {
                    Console.WriteLine($"ImportedTransaction {req.ImportedTxId} not found in document {docId}");
                    continue;
                }

                var existingImportedTx = transactions[txIdx];
                var updatedTransaction = new ImportedTransaction();
                foreach (var prop in ImportedTransactionProperties)
                {
                    if (prop.CanWrite) prop.SetValue(updatedTransaction, prop.GetValue(existingImportedTx));
                }
                updatedTransaction.Matched = true;

                transactions[txIdx] = updatedTransaction;
                updatedDocs[docId].importedTransactions = transactions.ToArray();
                batch.Set(_firestoreDb.Collection("importedTransactions").Document(docId), new { importedTransactions = transactions }, SetOptions.MergeAll);
            }

            if (updatedDocs.Any()) await batch.CommitAsync();
            await SaveBudget(budgetId, budget, userId, userEmail);

            Console.WriteLine($"Batch reconcile completed in {(DateTime.UtcNow - startTime).TotalMilliseconds}ms");
        }

    }

}