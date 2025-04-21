// FamilyBudgetApi/Controllers/AccountController.cs
using Microsoft.AspNetCore.Mvc;
using FamilyBudgetApi.Services;
using FamilyBudgetApi.Models;
using System.Threading.Tasks;

namespace FamilyBudgetApi.Controllers
{
    [ApiController]
    [Route("api/families/{familyId}")]
    public class AccountController : ControllerBase
    {
        private readonly AccountService _accountService;
        private readonly FamilyService _familyService;

        public AccountController(AccountService accountService, FamilyService familyService)
        {
            _accountService = accountService;
            _familyService = familyService;
        }

        [HttpGet("accounts")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetAccounts(string familyId)
        {
            var userId = HttpContext.Items["UserId"]?.ToString();
            if (!await _accountService.IsFamilyMember(familyId, userId))
                return Unauthorized("Not a member of this family");
            var accounts = await _accountService.GetAccounts(familyId);
            return Ok(accounts);
        }

        [HttpGet("accounts/{accountId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetAccount(string familyId, string accountId)
        {
            var userId = HttpContext.Items["UserId"]?.ToString();
            if (!await _accountService.IsFamilyMember(familyId, userId))
                return Unauthorized("Not a member of this family");

            var account = await _accountService.GetAccount(familyId, accountId);
            if (account == null)
                return NotFound();

            if (account.UserId != null && account.UserId != userId)
                return Unauthorized("Cannot access another user's personal account");
            return Ok(account);
        }

        [HttpPut("accounts/{accountId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> SaveAccount(string familyId, string accountId, [FromBody] Account account)
        {
            var userId = HttpContext.Items["UserId"]?.ToString();
            if (!await _accountService.IsFamilyMember(familyId, userId))
                return Unauthorized("Not a member of this family");

            if (account.Id != accountId)
                return BadRequest("Invalid account ID");

            if (account.UserId != null && account.UserId != userId)
                return Unauthorized("Cannot edit another user's personal account");

            var validTypes = new[] { "Bank", "CreditCard", "Investment", "Property", "Loan" };
            var validCategories = new[] { "Asset", "Liability" };
            if (!validTypes.Contains(account.Type) || !validCategories.Contains(account.Category))
                return BadRequest("Invalid account type or category");

            await _accountService.SaveAccount(familyId, account);
            return Ok();
        }

        [HttpDelete("accounts/{accountId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> DeleteAccount(string familyId, string accountId)
        {
            var userId = HttpContext.Items["UserId"]?.ToString();
            if (!await _accountService.IsFamilyMember(familyId, userId))
                return Unauthorized("Not a member of this family");

            var account = await _accountService.GetAccount(familyId, accountId);
            if (account == null)
                return NotFound();

            if (account.UserId != null && account.UserId != userId)
                return Unauthorized("Cannot delete another user's personal account");

            await _accountService.DeleteAccount(familyId, accountId);
            return Ok();
        }

        [HttpPost("accounts/import")]
        [AuthorizeFirebase]
        public async Task<IActionResult> ImportAccounts(string familyId, [FromBody] List<ImportAccountEntry> entries)
        {
            var userId = HttpContext.Items["UserId"]?.ToString();
            if (!await _accountService.IsFamilyMember(familyId, userId))
                return Unauthorized("Not a member of this family");

            var validTypes = new[] { "Bank", "CreditCard", "Investment", "Property", "Loan" };
            foreach (var entry in entries)
            {
                if (!validTypes.Contains(entry.Type))
                    return BadRequest($"Invalid account type: {entry.Type}");
                if (string.IsNullOrEmpty(entry.AccountName))
                    return BadRequest("Account name is required");
            }

            await _accountService.ImportAccountsAndSnapshots(familyId, entries);
            return Ok();
        }

        [HttpGet("snapshots")]
        [AuthorizeFirebase]
        public async Task<IActionResult> GetSnapshots(string familyId)
        {
            var userId = HttpContext.Items["UserId"]?.ToString();
            if (!await _accountService.IsFamilyMember(familyId, userId))
                return Unauthorized("Not a member of this family");

            var snapshots = await _accountService.GetSnapshots(familyId);
            return Ok(snapshots);
        }

        [HttpPut("snapshots/{snapshotId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> SaveSnapshot(string familyId, string snapshotId, [FromBody] Snapshot snapshot)
        {
            var userId = HttpContext.Items["UserId"]?.ToString();
            if (!await _accountService.IsFamilyMember(familyId, userId))
                return Unauthorized("Not a member of this family");

            if (snapshot.Id != snapshotId)
                return BadRequest("Invalid snapshot ID");

            await _accountService.SaveSnapshot(familyId, snapshot);
            return Ok();
        }

        [HttpDelete("snapshots/{snapshotId}")]
        [AuthorizeFirebase]
        public async Task<IActionResult> DeleteSnapshot(string familyId, string snapshotId)
        {
            var userId = HttpContext.Items["UserId"]?.ToString();

            if (!await _accountService.IsFamilyMember(familyId, userId))
            {
                return Unauthorized("Not a member of this family");
            }

            await _accountService.DeleteSnapshot(familyId, snapshotId);
            return NoContent();
        }

        [HttpPost("snapshots/batch-delete")]
        [AuthorizeFirebase]
        public async Task<IActionResult> BatchDeleteSnapshots(string familyId, [FromBody] List<string> snapshotIds)
        {
            var userId = HttpContext.Items["UserId"]?.ToString();
            if (!await _accountService.IsFamilyMember(familyId, userId))
                return Unauthorized("Not a member of this family");

            if (snapshotIds == null || snapshotIds.Count == 0)
                return BadRequest("No snapshot IDs provided");

            await _accountService.BatchDeleteSnapshots(familyId, snapshotIds);
            return NoContent();
        }
    }
}