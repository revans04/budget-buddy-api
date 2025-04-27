// FamilyBudgetApi/Services/AccountService.cs
using Google.Cloud.Firestore;
using FamilyBudgetApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FamilyBudgetApi.Services
{
  public class AccountService
  {
    private readonly FirestoreDb _db;
    private readonly ILogger<AccountService> _logger;

    public AccountService(FirestoreDb db, ILogger<AccountService> logger)
    {
      _db = db;
      _logger = logger;
    }

    public async Task<List<Account>> GetAccounts(string familyId)
    {
      _logger.LogInformation("Fetching accounts for familyId: {FamilyId}", familyId);
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();

      if (!familySnap.Exists)
      {
        _logger.LogWarning("Family not found: {FamilyId}", familyId);
        return new List<Account>();
      }

      _logger.LogInformation("Raw Firestore data: {@FamilyData}", familySnap.ToDictionary());

      var family = familySnap.ConvertTo<Family>();
      var accounts = family.Accounts ?? new List<Account>();
      _logger.LogInformation("Fetched {AccountCount} accounts: {@Accounts}", accounts.Count, accounts);
      return accounts;
    }

    public async Task<Account> GetAccount(string familyId, string accountId)
    {
      _logger.LogInformation("Fetching account {AccountId} for familyId: {FamilyId}", accountId, familyId);
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();

      if (!familySnap.Exists)
      {
        _logger.LogWarning("Family not found: {FamilyId}", familyId);
        return null;
      }

      var family = familySnap.ConvertTo<Family>();
      var account = (family.Accounts ?? new List<Account>()).FirstOrDefault(a => a.Id == accountId);
      if (account == null)
      {
        _logger.LogWarning("Account not found: {AccountId}", accountId);
      }
      else
      {
        _logger.LogInformation("Fetched account: {@Account}", account);
      }
      return account;
    }

    public async Task SaveAccount(string familyId, Account account)
    {
      _logger.LogInformation("Saving account {AccountId} for familyId: {FamilyId}", account.Id, familyId);
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();
      if (!familySnap.Exists) throw new Exception("Family not found");

      var family = familySnap.ConvertTo<Family>();
      var accounts = family.Accounts ?? new List<Account>();
      var index = accounts.FindIndex(a => a.Id == account.Id);
      if (index >= 0)
      {
        accounts[index] = account;
      }
      else
      {
        accounts.Add(account);
      }

      await familyRef.SetAsync(new { accounts = accounts }, SetOptions.MergeAll); // Use lowercase "accounts"
      _logger.LogInformation("Account saved successfully: {AccountId}", account.Id);
    }

    public async Task DeleteAccount(string familyId, string accountId)
    {
      _logger.LogInformation("Deleting account {AccountId} for familyId: {FamilyId}", accountId, familyId);
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();
      if (!familySnap.Exists) throw new Exception("Family not found");

      var family = familySnap.ConvertTo<Family>();
      var accounts = family.Accounts ?? new List<Account>();
      accounts.RemoveAll(a => a.Id == accountId);

      await familyRef.SetAsync(new { accounts = accounts }, SetOptions.MergeAll); // Use lowercase "accounts"
      _logger.LogInformation("Account deleted successfully: {AccountId}", accountId);
    }

    public async Task ImportAccountsAndSnapshots(string familyId, List<ImportAccountEntry> entries)
    {
      _logger.LogInformation("Importing accounts and snapshots for familyId: {FamilyId}, entries: {EntryCount}", familyId, entries.Count);
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();
      if (!familySnap.Exists) throw new Exception("Family not found");

      var family = familySnap.ConvertTo<Family>();
      var accounts = family.Accounts ?? new List<Account>();
      var snapshots = family.Snapshots ?? new List<Snapshot>();

      var accountGroups = entries
          .GroupBy(e => new { e.AccountName, e.Type })
          .ToDictionary(g => g.Key, g => g.ToList());

      var accountsDict = new Dictionary<string, Account>();
      foreach (var group in accountGroups)
      {
        var entry = group.Value.First();
        var accountId = Guid.NewGuid().ToString();
        var accountDetails = new AccountDetails
        {
          InterestRate = entry.InterestRate,
          AppraisedValue = entry.AppraisedValue,
          Address = entry.Address,
        };
        var account = new Account
        {
          Id = accountId,
          Name = entry.AccountName,
          Type = entry.Type,
          Category = entry.Type == "CreditCard" || entry.Type == "Loan" ? "Liability" : "Asset",
          AccountNumber = entry.AccountNumber,
          Institution = entry.Institution,
          Balance = group.Value.OrderByDescending(e => e.Date).First().Balance,
          Details = accountDetails,
          CreatedAt = group.Value.OrderByDescending(e => e.Date).First().Date,
          UpdatedAt = group.Value.OrderByDescending(e => e.Date).First().Date
        };
        accountsDict[accountId] = account;

        var existingIndex = accounts.FindIndex(a => a.Name == account.Name && a.Type == account.Type);
        if (existingIndex >= 0)
        {
          accounts[existingIndex] = account;
        }
        else
        {
          accounts.Add(account);
        }
      }

      var newSnapshots = entries
          .GroupBy(e => e.Date)
          .Select(g => new Snapshot
          {
            Id = Guid.NewGuid().ToString(),
            Date = g.Key,
            Accounts = [.. g
                  .Select(e =>
                  {
                    var account = accountsDict.Values.FirstOrDefault(a =>
                              a.Name == e.AccountName && a.Type == e.Type);
                    return account != null ? new SnapshotAccount
                    {
                      AccountId = account.Id,
                      Value = e.Balance ?? 0,
                      Type = account.Type,
                      AccountName = account.Name
                    } : null;
                  })
                  .Where(sa => sa != null)],
            NetWorth = g.Sum(e => e.Type == "CreditCard" || e.Type == "Loan" ? -(e.Balance ?? 0) : (e.Balance ?? 0)),
            CreatedAt = g.Key
          })
          .ToList();

      snapshots.AddRange(newSnapshots);

      await familyRef.SetAsync(new { accounts = accounts, snapshots = snapshots }, SetOptions.MergeAll); // Use lowercase
      _logger.LogInformation("Imported {AccountCount} accounts and {SnapshotCount} snapshots", accountsDict.Count, newSnapshots.Count);
    }

    public async Task<List<Snapshot>> GetSnapshots(string familyId)
    {
      _logger.LogInformation("Fetching snapshots for familyId: {FamilyId}", familyId);
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();
      if (!familySnap.Exists)
      {
        _logger.LogWarning("Family not found: {FamilyId}", familyId);
        return new List<Snapshot>();
      }

      _logger.LogInformation("Raw Firestore data: {@FamilyData}", familySnap.ToDictionary());

      var family = familySnap.ConvertTo<Family>();
      var snapshots = family.Snapshots ?? new List<Snapshot>();
      _logger.LogInformation("Fetched {SnapshotCount} snapshots: {@Snapshots}", snapshots.Count, snapshots);
      return snapshots.OrderByDescending(s => s.Date).ToList();
    }

    public async Task SaveSnapshot(string familyId, Snapshot snapshot)
    {
      _logger.LogInformation("Saving snapshot {SnapshotId} for familyId: {FamilyId}", snapshot.Id, familyId);
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();
      if (!familySnap.Exists) throw new Exception("Family not found");

      var family = familySnap.ConvertTo<Family>();
      var snapshots = family.Snapshots ?? new List<Snapshot>();
      var index = snapshots.FindIndex(s => s.Id == snapshot.Id);
      if (index >= 0)
      {
        snapshots[index] = snapshot;
      }
      else
      {
        snapshots.Add(snapshot);
      }

      await familyRef.SetAsync(new { snapshots = snapshots }, SetOptions.MergeAll); // Use lowercase "snapshots"
      _logger.LogInformation("Snapshot saved successfully: {SnapshotId}", snapshot.Id);
    }

    public async Task DeleteSnapshot(string familyId, string snapshotId)
    {
      _logger.LogInformation("Deleting snapshot {SnapshotId} for familyId: {FamilyId}", snapshotId, familyId);
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();

      if (!familySnap.Exists) throw new Exception("Family not found");
      var family = familySnap.ConvertTo<Family>();
      var snapshots = family.Snapshots ?? new List<Snapshot>();
      snapshots.RemoveAll(s => s.Id == snapshotId);

      await familyRef.SetAsync(new { snapshots = snapshots }, SetOptions.MergeAll); // Use lowercase "snapshots"
      _logger.LogInformation("Snapshot deleted successfully: {SnapshotId}", snapshotId);
    }

    public async Task BatchDeleteSnapshots(string familyId, List<string> snapshotIds)
    {
      _logger.LogInformation("Batch deleting {SnapshotCount} snapshots for familyId: {FamilyId}", snapshotIds.Count, familyId);
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();

      if (!familySnap.Exists) throw new Exception("Family not found");
      var family = familySnap.ConvertTo<Family>();
      var snapshots = family.Snapshots ?? new List<Snapshot>();

      snapshots.RemoveAll(s => snapshotIds.Contains(s.Id));

      await familyRef.SetAsync(new { snapshots = snapshots }, SetOptions.MergeAll); // Use lowercase "snapshots"
      _logger.LogInformation("Batch deleted {SnapshotCount} snapshots", snapshotIds.Count);
    }

    public async Task<bool> IsFamilyMember(string familyId, string userId)
    {
      _logger.LogInformation("Checking if user {UserId} is a member of family {FamilyId}", userId, familyId);
      var familyDoc = await _db.Collection("families").Document(familyId).GetSnapshotAsync();
      if (!familyDoc.Exists)
      {
        _logger.LogWarning("Family not found: {FamilyId}", familyId);
        return false;
      }
      var family = familyDoc.ConvertTo<Family>();
      var isMember = family.MemberUids.Contains(userId);
      _logger.LogInformation("User {UserId} is {IsMember} a member of family {FamilyId}", userId, isMember ? "" : "not", familyId);
      return isMember;
    }
  }
}
