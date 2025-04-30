using Google.Cloud.Firestore;
using FamilyBudgetApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace FamilyBudgetApi.Services
{
    public class BudgetService
    {
        private readonly FirestoreDb _firestoreDb;
        private readonly FamilyService _familyService;
        private static readonly PropertyInfo[] TransactionProperties = typeof(Models.Transaction).GetProperties();
        private static readonly PropertyInfo[] ImportedTransactionProperties = typeof(ImportedTransaction).GetProperties();
        private static readonly Dictionary<string, PropertyInfo> TransactionPropertyMap = TransactionProperties.ToDictionary(p => p.Name);
        private static readonly Dictionary<string, PropertyInfo> ImportedTransactionPropertyMap = ImportedTransactionProperties.ToDictionary(p => p.Name);

        public BudgetService(FirestoreDb firestoreDb, FamilyService familyService)
        {
            _firestoreDb = firestoreDb;
            _familyService = familyService;
        }

        public async Task<List<BudgetInfo>> LoadAccessibleBudgets(string userId, string? entityId = null)
        {
            var family = await _familyService.GetUserFamily(userId);
            if (family == null) return new List<BudgetInfo>();

            var budgetsQuery = _firestoreDb.Collection("budgets")
                .WhereEqualTo("familyId", family.Id);
            if (!string.IsNullOrEmpty(entityId))
            {
                budgetsQuery = budgetsQuery.WhereEqualTo("entityId", entityId);
            }

            var budgetsSnapshot = await budgetsQuery.GetSnapshotAsync();
            var budgets = budgetsSnapshot.Documents
                .Select(doc =>
                {
                    var budget = doc.ConvertTo<BudgetInfo>();
                    budget.BudgetId = doc.Id;
                    budget.IsOwner = family.OwnerUid == userId;
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

        public async Task DeleteBudget(string budgetId, string userId, string userEmail)
        {
            var budgetRef = _firestoreDb.Collection("budgets").Document(budgetId);
            var budgetSnap = await budgetRef.GetSnapshotAsync();
            if (!budgetSnap.Exists)
            {
                throw new Exception($"Budget {budgetId} not found");
            }

            var budget = budgetSnap.ConvertTo<Budget>();
            if (!await CanAccessBudget(budget, userId))
            {
                throw new Exception($"User {userId} is not authorized to delete budget {budgetId}");
            }

            // Check if the user is the entity owner or an admin
            var family = await _familyService.GetUserFamily(userId);
            if (family == null)
            {
                throw new Exception($"Family not found for user {userId}");
            }
            var entity = family.Entities?.FirstOrDefault(e => e.Id == budget.EntityId);
            if (entity == null)
            {
                throw new Exception($"Entity {budget.EntityId} not found for budget {budgetId}");
            }
            if (!entity.Members.Any(m => m.Uid == userId && m.Role == "Admin"))
            {
                throw new Exception($"User {userId} does not have permission to delete budget {budgetId}");
            }

            // Delete the budget
            await budgetRef.DeleteAsync();

            // Log the deletion event
            await LogEditEvent(budgetId, userId, userEmail, "delete_budget");

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

            if (budget.Merchants == null)
            {
                budget.Merchants = new List<Merchant>();
                Console.WriteLine($"Initialized Merchants list for budget {budgetId} in SaveBudget");
            }

            await _firestoreDb.Collection("budgets").Document(budgetId).SetAsync(budget, SetOptions.MergeAll);
            await LogEditEvent(budgetId, userId, userEmail, "update_budget");
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
                await UpdateMerchants(budgetId, transaction.Merchant, 1, budget);
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
                await UpdateMerchants(budgetId, oldMerchant, -1, budget);
            }
            if (!string.IsNullOrEmpty(transaction.Merchant) && (oldMerchant == null || oldMerchant != transaction.Merchant))
            {
                Console.WriteLine($"Increasing count for new merchant {transaction.Merchant} in budget {budgetId}");
                await UpdateMerchants(budgetId, transaction.Merchant, 1, budget);
            }

            await SaveBudget(budgetId, budget, userId, userEmail.Replace("update_budget", transaction.Id != null ? "update_transaction" : "add_transaction"));
        }

        public async Task BatchSaveTransactions(string budgetId, List<Models.Transaction> transactions, string userId, string userEmail)
        {
            var budget = await GetBudget(budgetId) ?? throw new Exception($"Budget {budgetId} not found");
            foreach (var transaction in transactions)
            {
                transaction.Id ??= _firestoreDb.Collection("budgets").Document().Id;
                budget.Transactions.Add(transaction);
                if (!string.IsNullOrEmpty(transaction.Merchant))
                {
                    Console.WriteLine($"Adding merchant {transaction.Merchant} for budget {budgetId}");
                    await UpdateMerchants(budgetId, transaction.Merchant, 1, budget);
                }
            }

            await SaveBudget(budgetId, budget, userId, userEmail.Replace("update_budget", "batch_add_transactions"));
        }

        public async Task DeleteTransaction(string budgetId, string transactionId, string userId, string userEmail)
        {
            var budget = await GetBudget(budgetId) ?? throw new Exception($"Budget {budgetId} not found");
            var transaction = budget.Transactions.FirstOrDefault(t => t.Id == transactionId) ?? throw new Exception($"Transaction {transactionId} not found");
            transaction.Deleted = true;
            if (!string.IsNullOrEmpty(transaction.Merchant))
            {
                Console.WriteLine($"Decreasing count for merchant {transaction.Merchant} in budget {budgetId}");
                await UpdateMerchants(budgetId, transaction.Merchant, -1, budget);
            }
            await SaveBudget(budgetId, budget, userId, userEmail.Replace("update_budget", "delete_transaction"));
        }

        private async Task UpdateMerchants(string budgetId, string merchantName, int increment, Budget budget)
        {
            if (string.IsNullOrEmpty(merchantName)) return;

            if (budget.Merchants == null)
            {
                budget.Merchants = new List<Merchant>();
                Console.WriteLine($"Initialized Merchants list for budget {budgetId} in UpdateMerchants");
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
            var docRef = _firestoreDb.Collection("importedTransactions").Document(doc.Id);
            doc.UserId = userId;
            await docRef.SetAsync(doc, SetOptions.Overwrite);
            return doc.Id;
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
                    return importedDoc;
                })
                .ToList();
        }

        public async Task DeleteImportedTransactionDoc(string id)
        {
            var itdRef = _firestoreDb.Collection("importedTransactions").Document(id);
            var itdSnap = await itdRef.GetSnapshotAsync();
            if (!itdSnap.Exists) throw new Exception("Imported Transaction Doc not found");

            await itdRef.DeleteAsync();
        }

        public async Task<List<ImportedTransaction>> GetImportedTransactionsByAccountId(string accountId)
        {
            Console.WriteLine($"Fetching imported transactions for account {accountId}");
            var importedDocs = await _firestoreDb.Collection("importedTransactions").GetSnapshotAsync();

            var transactions = new List<ImportedTransaction>();
            foreach (var doc in importedDocs)
            {
                var importedDoc = doc.ConvertTo<ImportedTransactionDoc>();
                var matchingTxs = importedDoc.importedTransactions
                    .Where(tx => tx.AccountId == accountId && !(tx.Deleted ?? false))
                    .ToList();
                transactions.AddRange(matchingTxs);
            }

            Console.WriteLine($"Found {transactions.Count} imported transactions for account {accountId}");
            return transactions;
        }

        public async Task BatchUpdateImportedTransactions(List<ImportedTransaction> transactions)
        {
            Console.WriteLine($"Batch updating {transactions.Count} imported transactions");
            var groupedByDoc = transactions.GroupBy(tx => tx.Id.Split('-')[0]);
            foreach (var group in groupedByDoc)
            {
                var docId = group.Key;
                var docRef = _firestoreDb.Collection("importedTransactions").Document(docId);
                var docSnap = await docRef.GetSnapshotAsync();
                if (!docSnap.Exists)
                {
                    Console.WriteLine($"Imported transaction doc {docId} not found");
                    continue;
                }
                var importedDoc = docSnap.ConvertTo<ImportedTransactionDoc>();
                var importedTxs = importedDoc.importedTransactions.ToList();
                foreach (var tx in group)
                {
                    var index = importedTxs.FindIndex(t => t.Id == tx.Id);
                    if (index >= 0)
                    {
                        importedTxs[index] = tx;
                    }
                }
                importedDoc.importedTransactions = importedTxs.ToArray();
                await docRef.SetAsync(importedDoc, SetOptions.MergeAll);
            }
            Console.WriteLine($"Batch updated {transactions.Count} imported transactions");
        }

        public async Task<List<TransactionWithBudgetId>> GetBudgetTransactionsMatchedToImported(string accountId, string userId)
        {
            Console.WriteLine($"Fetching budget transactions matched to imported for account {accountId} and user {userId}");
            // First, get all imported transactions for this account
            var importedTxs = await GetImportedTransactionsByAccountId(accountId);
            var matchedImportedTxs = importedTxs
                .Where(tx => tx.Matched && !tx.Ignored && !(tx.Deleted ?? false))
                .ToList();

            // Get all budgets accessible to the user
            var budgets = new List<Budget>();
            var family = await _familyService.GetUserFamily(userId);
            if (family == null)
            {
                Console.WriteLine($"No family found for user {userId}");
                return new List<TransactionWithBudgetId>();
            }

            var budgetDocs = await _firestoreDb.Collection("budgets")
                .WhereEqualTo("familyId", family.Id)
                .GetSnapshotAsync();

            foreach (var budgetDoc in budgetDocs)
            {
                var budget = budgetDoc.ConvertTo<Budget>();
                if (await CanAccessBudget(budget, userId))
                {
                    budgets.Add(budget);
                }
            }

            // Find budget transactions that match the imported transactions
            var matchedBudgetTxs = new List<TransactionWithBudgetId>();
            foreach (var budget in budgets)
            {
                var budgetTxs = budget.Transactions ?? new List<Models.Transaction>();
                foreach (var importedTx in matchedImportedTxs)
                {
                    // Match criteria: same accountNumber, postedDate, amount, and payee/merchant
                    var matchingBudgetTxs = budgetTxs
                        .Where(tx =>
                            (!tx.Deleted ?? false) &&
                            tx.AccountNumber == importedTx.AccountNumber &&
                            tx.PostedDate == importedTx.PostedDate &&
                            (tx.Amount == importedTx.DebitAmount || tx.Amount == importedTx.CreditAmount) &&
                            tx.Merchant == importedTx.Payee)
                        .ToList();
                    matchedBudgetTxs.AddRange(matchingBudgetTxs.Select(tx => new TransactionWithBudgetId
                    {
                        BudgetId = budget.BudgetId,
                        Transaction = tx
                    }));
                }
            }

            Console.WriteLine($"Found {matchedBudgetTxs.Count} budget transactions matched to imported for account {accountId}");
            return matchedBudgetTxs;
        }

        public async Task BatchUpdateBudgetTransactions(List<TransactionWithBudgetId> transactions, string userId, string userEmail)
        {
            Console.WriteLine($"Batch updating {transactions.Count} budget transactions by user {userId}");
            var groupedByBudget = transactions.GroupBy(tx => tx.BudgetId);
            foreach (var group in groupedByBudget)
            {
                var budgetId = group.Key;
                var budget = await GetBudget(budgetId) ?? throw new Exception($"Budget {budgetId} not found");

                if (!await CanAccessBudget(budget, userId))
                    throw new Exception("User does not have access to this budget");

                var transactionsList = budget.Transactions ?? new List<Models.Transaction>();
                foreach (var txWithBudget in group)
                {
                    var tx = txWithBudget.Transaction;
                    var index = transactionsList.FindIndex(t => t.Id == tx.Id);
                    if (index >= 0)
                    {
                        transactionsList[index] = tx;
                    }
                }

                budget.Transactions = transactionsList;
                await SaveBudget(budgetId, budget, userId, userEmail.Replace("update_budget", $"batch_update_transactions_{group.Count()}"));
            }
            Console.WriteLine($"Batch updated {transactions.Count} budget transactions");
        }

        public async Task<bool> CanAccessBudget(Budget budget, string userId)
        {
            Console.WriteLine($"Checking if user {userId} can access budget {budget.BudgetId}");
            var familyDoc = await _firestoreDb.Collection("families").Document(budget.FamilyId).GetSnapshotAsync();
            if (!familyDoc.Exists)
            {
                Console.WriteLine($"Family {budget.FamilyId} not found for budget {budget.BudgetId}");
                return false;
            }
            var family = familyDoc.ConvertTo<Family>();
            if (!family.MemberUids.Contains(userId))
            {
                Console.WriteLine($"User {userId} is not a member of family {budget.FamilyId}");
                return false;
            }

            if (string.IsNullOrEmpty(budget.EntityId))
            {
                Console.WriteLine($"Budget {budget.BudgetId} has no entity, access granted for user {userId}");
                return true;
            }

            var entity = family.Entities?.FirstOrDefault(e => e.Id == budget.EntityId);
            if (entity == null)
            {
                Console.WriteLine($"Entity {budget.EntityId} not found in family {budget.FamilyId} for budget {budget.BudgetId}");
                return false;
            }

            var canAccess = entity.Members.Any(m => m.Uid == userId);
            Console.WriteLine($"User {userId} {(canAccess ? "can" : "cannot")} access budget {budget.BudgetId}");
            return canAccess;
        }

        public async Task UpdateImportedTransaction(string docId, string transactionId, bool? matched = null, bool? ignored = null, bool? deleted = null)
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

            foreach (var prop in ImportedTransactionProperties)
            {
                if (prop.CanWrite)
                {
                    var value = prop.GetValue(existingTransaction);
                    prop.SetValue(updatedTransaction, value);
                }
            }

            if (matched.HasValue)
            {
                updatedTransaction.Matched = matched.Value;
            }
            if (ignored.HasValue)
            {
                updatedTransaction.Ignored = ignored.Value;
            }
            if (deleted.HasValue)
            {
                updatedTransaction.Deleted = deleted.Value;
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

            var importedTxIndex = new Dictionary<string, (ImportedTransactionDoc doc, int index)>();
            foreach (var doc in importedDocs)
            {
                for (int i = 0; i < doc.importedTransactions.Length; i++)
                {
                    var tx = doc.importedTransactions[i];
                    importedTxIndex[tx.Id] = (doc, i);
                }
            }

            const int batchSize = 50;
            var requestBatches = requests.Select((req, index) => new { req, index })
                                         .GroupBy(x => x.index / batchSize)
                                         .Select(g => g.Select(x => x.req).ToList());

            var batch = _firestoreDb.StartBatch();
            foreach (var requestBatch in requestBatches)
            {
                var updatedDocs = new Dictionary<string, ImportedTransactionDoc>();

                foreach (var req in requestBatch)
                {
                    var budgetTxIndex = budget.Transactions.FindIndex(t => t.Id == req.BudgetTransactionId);
                    if (budgetTxIndex < 0) throw new Exception($"Budget transaction {req.BudgetTransactionId} not found in budget {budgetId}");

                    var budgetTx = budget.Transactions[budgetTxIndex];

                    if (!importedTxIndex.TryGetValue(req.ImportedTransactionId, out var targetEntry))
                    {
                        Console.WriteLine($"ImportedTransactionDoc not found for ImportedTxId: {req.ImportedTransactionId}");
                        continue;
                    }

                    var docId = targetEntry.doc.Id;
                    if (!updatedDocs.ContainsKey(docId)) updatedDocs[docId] = targetEntry.doc;

                    var transactions = updatedDocs[docId].importedTransactions.ToList();
                    var importedTx = transactions[targetEntry.index];

                    if (req.Match)
                    {
                        foreach (var importedProp in ImportedTransactionProperties)
                        {
                            if (TransactionPropertyMap.TryGetValue(importedProp.Name, out var budgetProp) && budgetProp.CanWrite)
                            {
                                var value = importedProp.GetValue(importedTx);
                                if (value != null)
                                {
                                    if (importedProp.Name == "Payee" && budgetProp.Name == "ImportedMerchant")
                                        budgetProp.SetValue(budgetTx, value);
                                    else if (importedProp.Name == budgetProp.Name)
                                        budgetProp.SetValue(budgetTx, value);
                                }
                            }
                        }
                        budgetTx.Status = "C";
                    }

                    if (req.Match) importedTx.Matched = true;
                    if (req.Ignore) importedTx.Ignored = true;

                    transactions[targetEntry.index] = importedTx;
                    updatedDocs[docId].importedTransactions = transactions.ToArray();
                    batch.Set(_firestoreDb.Collection("importedTransactions").Document(docId),
                        new { importedTransactions = transactions }, SetOptions.MergeAll);
                }
            }

            var budgetRef = _firestoreDb.Collection("budgets").Document(budgetId);
            var budgetUpdate = new Dictionary<string, object>
            {
                { "transactions", budget.Transactions }
            };
            batch.Set(budgetRef, budgetUpdate, SetOptions.MergeAll);
            await batch.CommitAsync();
            await LogEditEvent(budgetId, userId, userEmail, "update_budget");

            Console.WriteLine($"Batch reconcile completed in {(DateTime.UtcNow - startTime).TotalMilliseconds}ms");
        }
    }
}
