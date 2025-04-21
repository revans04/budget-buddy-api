// FamilyBudgetApi/Models/Account.cs
using Google.Cloud.Firestore;
using System;

namespace FamilyBudgetApi.Models
{
    [FirestoreData]
    public class Account
    {
        [FirestoreProperty]
        public string Id { get; set; }

        [FirestoreProperty]
        public string? UserId { get; set; } // Optional, for personal accounts

        [FirestoreProperty]
        public string Name { get; set; }

        [FirestoreProperty]
        public string Type { get; set; } // "Bank", "CreditCard", "Investment", "Property", "Loan"

        [FirestoreProperty]
        public string Category { get; set; } // "Asset", "Liability"

        [FirestoreProperty]
        public string? AccountNumber { get; set; }

        [FirestoreProperty]
        public string Institution { get; set; }

        [FirestoreProperty]
        public Timestamp CreatedAt { get; set; }

        [FirestoreProperty]
        public Timestamp UpdatedAt { get; set; }

        [FirestoreProperty]
        public double? Balance { get; set; }

        [FirestoreProperty]
        public AccountDetails Details { get; set; }
    }

    [FirestoreData]
    public class AccountDetails
    {
        [FirestoreProperty]
        public double? InterestRate { get; set; }

        [FirestoreProperty]
        public double? AppraisedValue { get; set; }

        [FirestoreProperty]
        public string? MaturityDate { get; set; }

        [FirestoreProperty]
        public string? Address { get; set; }
    }

    [FirestoreData]
    public class ImportAccountEntry
    {
        [FirestoreProperty]
        public string AccountName { get; set; }

        [FirestoreProperty]
        public string Type { get; set; }

        [FirestoreProperty]
        public string? AccountNumber { get; set; }

        [FirestoreProperty]
        public string? Institution { get; set; }

        [FirestoreProperty]
        public Timestamp Date { get; set; }

        [FirestoreProperty]
        public double? Balance { get; set; }

        [FirestoreProperty]
        public double? InterestRate { get; set; }

        [FirestoreProperty]
        public double? AppraisedValue { get; set; }

        [FirestoreProperty]
        public string? Address { get; set; }
    }

}