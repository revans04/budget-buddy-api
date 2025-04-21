// FamilyBudgetApi/Services/AccountService.cs
using Google.Cloud.Firestore;
using FamilyBudgetApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FamilyBudgetApi.Services
{
  public class AccountService
  {
    private readonly FirestoreDb _db;

    public AccountService(FirestoreDb db)
    {
      _db = db;
    }

    public async Task<List<Account>> GetAccounts(string familyId)
    {
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();

      if (!familySnap.Exists) return new List<Account>();
      var family = familySnap.ConvertTo<Family>();
      var accounts = family.Accounts ?? new List<Account>();
      return accounts;
    }

    public async Task<Account> GetAccount(string familyId, string accountId)
    {
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();

      if (!familySnap.Exists) return null;
      var family = familySnap.ConvertTo<Family>();
      var account = (family.Accounts ?? new List<Account>()).FirstOrDefault(a => a.Id == accountId);
      return account;
    }

    public async Task SaveAccount(string familyId, Account account)
    {
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

      await familyRef.SetAsync(new { Accounts = accounts }, SetOptions.MergeAll);
    }

    public async Task DeleteAccount(string familyId, string accountId)
    {
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();
      if (!familySnap.Exists) throw new Exception("Family not found");

      var family = familySnap.ConvertTo<Family>();
      var accounts = family.Accounts ?? new List<Account>();
      accounts.RemoveAll(a => a.Id == accountId);

      await familyRef.SetAsync(new { Accounts = accounts }, SetOptions.MergeAll);
    }

    public async Task ImportAccountsAndSnapshots(string familyId, List<ImportAccountEntry> entries)
    {
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();
      if (!familySnap.Exists) throw new Exception("Family not found");

      var family = familySnap.ConvertTo<Family>();
      var accounts = family.Accounts ?? new List<Account>();
      var snapshots = family.Snapshots ?? new List<Snapshot>();

      // Group entries by account to create/update accounts
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

      // Group entries by date for snapshots
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

      await familyRef.SetAsync(new { Accounts = accounts, Snapshots = snapshots }, SetOptions.MergeAll);
    }

    public async Task<List<Snapshot>> GetSnapshots(string familyId)
    {
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();
      if (!familySnap.Exists) return new List<Snapshot>();

      var family = familySnap.ConvertTo<Family>();
      var snapshots = family.Snapshots ?? new List<Snapshot>();
      return snapshots.OrderByDescending(s => s.Date).ToList();
    }

    public async Task SaveSnapshot(string familyId, Snapshot snapshot)
    {
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

      await familyRef.SetAsync(new { Snapshots = snapshots }, SetOptions.MergeAll);
    }

    public async Task DeleteSnapshot(string familyId, string snapshotId)
    {
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();

      if (!familySnap.Exists) throw new Exception("Family not found");
      var family = familySnap.ConvertTo<Family>();
      var snapshots = family.Snapshots ?? new List<Snapshot>();
      snapshots.RemoveAll(s => s.Id == snapshotId);

      await familyRef.SetAsync(new { Snapshots = snapshots }, SetOptions.MergeAll);
    }

    public async Task BatchDeleteSnapshots(string familyId, List<string> snapshotIds)
    {
      var familyRef = _db.Collection("families").Document(familyId);
      var familySnap = await familyRef.GetSnapshotAsync();

      if (!familySnap.Exists) throw new Exception("Family not found");
      var family = familySnap.ConvertTo<Family>();
      var snapshots = family.Snapshots ?? new List<Snapshot>();

      // Remove all snapshots with the given IDs
      snapshots.RemoveAll(s => snapshotIds.Contains(s.Id));

      // Update the family document with the new snapshots array
      await familyRef.SetAsync(new { Snapshots = snapshots }, SetOptions.MergeAll);
    }


    public async Task<bool> IsFamilyMember(string familyId, string userId)
    {
      var familyDoc = await _db.Collection("families").Document(familyId).GetSnapshotAsync();
      if (!familyDoc.Exists) return false;
      var family = familyDoc.ConvertTo<Family>();
      return family.MemberUids.Contains(userId);
    }
  }
}