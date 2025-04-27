using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Mvc;
using FamilyBudgetApi.Services;
using FamilyBudgetApi.Models;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace FamilyBudgetApi.Controllers
{
    [ApiController]
    [Route("api/budget")]
    public class BudgetController : ControllerBase
    {
        private readonly BudgetService _budgetService;

        public BudgetController(BudgetService budgetService)
        {
            _budgetService = budgetService;
        }

        [HttpGet("ping")]
        public async Task<IActionResult> Ping()
        {
            var response = new
            {
                Message = "pong",
                DateTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };
            return Ok(response);
        }

        [HttpGet("accessible")]
        [AuthorizeFirebase]
        public async Task<IActionResult> LoadAccessibleBudgets([FromQuery] string? entityId)
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var budgets = await _budgetService.LoadAccessibleBudgets(userId, entityId);
                return Ok(budgets);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in LoadAccessibleBudgets: {ex.Message}");
                return Unauthorized();
            }
        }

        [HttpGet("shared")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetSharedBudgets()
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var sharedBudgets = await _budgetService.GetSharedBudgets(userId);
                return Ok(sharedBudgets);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetSharedBudgets: {ex.Message}");
                return Unauthorized();
            }
        }

        [HttpGet("{budgetId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetBudget(string budgetId)
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var budget = await _budgetService.GetBudget(budgetId);
                if (budget == null)
                {
                    return NotFound($"Budget {budgetId} not found");
                }
                return Ok(budget);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetBudget: {ex.Message}");
                return Unauthorized();
            }
        }

        [HttpPost("{budgetId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> SaveBudget(string budgetId, [FromBody] Budget budget)
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var userEmail = (await FirebaseAuth.DefaultInstance.GetUserAsync(userId)).Email;
                await _budgetService.SaveBudget(budgetId, budget, userId, userEmail);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveBudget: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{budgetId}/edit-history")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetEditHistory(string budgetId)
        {
            try
            {
                var since = DateTime.UtcNow.AddDays(-30);
                var editHistory = await _budgetService.GetEditHistory(budgetId, since);
                return Ok(editHistory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEditHistory: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("{budgetId}/transactions")]
        [AuthorizeFirebase]
        public async Task<IActionResult> AddTransaction(string budgetId, [FromBody] FamilyBudgetApi.Models.Transaction transaction)
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var userEmail = (await FirebaseAuth.DefaultInstance.GetUserAsync(userId)).Email;
                await _budgetService.AddTransaction(budgetId, transaction, userId, userEmail);
                return Ok(new { TransactionId = transaction.Id });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AddTransaction: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("{budgetId}/transactions/{transactionId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> SaveTransaction(string budgetId, string transactionId, [FromBody] FamilyBudgetApi.Models.Transaction transaction)
        {
            try
            {
                transaction.Id = transactionId;
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var userEmail = (await FirebaseAuth.DefaultInstance.GetUserAsync(userId)).Email;
                await _budgetService.SaveTransaction(budgetId, transaction, userId, userEmail);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveTransaction: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{budgetId}/transactions/{transactionId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> DeleteTransaction(string budgetId, string transactionId)
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var userEmail = (await FirebaseAuth.DefaultInstance.GetUserAsync(userId)).Email;
                await _budgetService.DeleteTransaction(budgetId, transactionId, userId, userEmail);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteTransaction: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("{budgetId}/transactions/batch")]
        [AuthorizeFirebase]
        public async Task<IActionResult> BatchSaveTransactions(string budgetId, [FromBody] List<FamilyBudgetApi.Models.Transaction> transactions)
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var userEmail = (await FirebaseAuth.DefaultInstance.GetUserAsync(userId)).Email;
                await _budgetService.BatchSaveTransactions(budgetId, transactions, userId, userEmail);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in BatchSaveTransactions: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("imported-transactions")]
        [AuthorizeFirebase]
        public async Task<IActionResult> SaveImportedTransactions([FromBody] ImportedTransactionDoc doc)
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var docId = await _budgetService.SaveImportedTransactions(userId, doc);
                return Ok(new { DocId = docId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveImportedTransactions: {ex.Message}\nStack Trace: {ex.StackTrace}");
                return BadRequest(ex.Message);
            }
        }

        [HttpPut("imported-transactions/{docId}/{transactionId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> UpdateImportedTransaction(string docId, string transactionId, [FromBody] UpdateImportedTransactionRequest request)
        {
            try
            {
                await _budgetService.UpdateImportedTransaction(docId, transactionId, request.Matched, request.Ignored);
                Console.WriteLine($"Updated ImportedTransactionDoc {docId} for tx {transactionId}");
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UpdateImportedTransaction: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("imported-transactions")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetImportedTransactions()
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var importedDocs = await _budgetService.GetImportedTransactions(userId);
                return Ok(importedDocs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetImportedTransactions: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("imported-transactions/by-account/{accountId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetImportedTransactionsByAccountId(string accountId)
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var transactions = await _budgetService.GetImportedTransactionsByAccountId(accountId);
                return Ok(transactions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetImportedTransactionsByAccountId: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("imported-transactions/batch-update")]
        [AuthorizeFirebase]
        public async Task<IActionResult> BatchUpdateImportedTransactions([FromBody] List<ImportedTransaction> transactions)
        {
            try
            {
                await _budgetService.BatchUpdateImportedTransactions(transactions);
                Console.WriteLine($"Batch updated {transactions.Count} imported transactions");
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in BatchUpdateImportedTransactions: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("transactions/matched-to-imported/{accountId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetBudgetTransactionsMatchedToImported(string accountId)
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var transactions = await _budgetService.GetBudgetTransactionsMatchedToImported(accountId, userId);
                return Ok(transactions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetBudgetTransactionsMatchedToImported: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("transactions/batch-update")]
        [AuthorizeFirebase]
        public async Task<IActionResult> BatchUpdateBudgetTransactions([FromBody] List<TransactionWithBudgetId> transactions)
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var userEmail = (await FirebaseAuth.DefaultInstance.GetUserAsync(userId)).Email;
                await _budgetService.BatchUpdateBudgetTransactions(transactions, userId, userEmail);
                Console.WriteLine($"Batch updated {transactions.Count} budget transactions");
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in BatchUpdateBudgetTransactions: {ex.Message}");
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("{budgetId}/batch-reconcile")]
        [AuthorizeFirebase]
        public async Task<IActionResult> BatchReconcileTransactions(string budgetId, [FromBody] BatchReconcileRequest request)
        {
            try
            {
                var userId = HttpContext.Items["UserId"]?.ToString() ?? throw new Exception("User ID not found");
                var userEmail = (await FirebaseAuth.DefaultInstance.GetUserAsync(userId)).Email;
                if (request.BudgetId != budgetId) throw new Exception("BudgetId in payload does not match URL");
                await _budgetService.BatchReconcileTransactions(budgetId, request.Reconciliations, userId, userEmail);
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in BatchReconcileTransactions: {ex.Message}\nStack Trace: {ex.StackTrace}");
                return BadRequest(ex.Message);
            }
        }
    }

    public class UpdateImportedTransactionRequest
    {
        public bool? Matched { get; set; }
        public bool? Ignored { get; set; }
    }
}
