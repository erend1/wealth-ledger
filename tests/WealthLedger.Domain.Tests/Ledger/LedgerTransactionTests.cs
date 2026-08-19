using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.Ledger
{
    public sealed class LedgerTransactionTests
    {
        private static readonly Guid HouseholdId =
            Guid.NewGuid();

        private static readonly Guid PortfolioId =
            Guid.NewGuid();

        private static readonly Guid AccountId =
            Guid.NewGuid();

        private static readonly Guid TryAssetId =
            Guid.NewGuid();

        private static readonly Guid AisAssetId =
            Guid.NewGuid();

        private static readonly Guid KpcAssetId =
            Guid.NewGuid();

        private static readonly DateTimeOffset CreatedAt =
            new(
                2026,
                8,
                19,
                9,
                0,
                0,
                TimeSpan.Zero);

        private static readonly DateTimeOffset PostedAt =
            CreatedAt.AddMinutes(10);

        private static readonly DateOnly ExecutionDate =
            new(2026, 8, 19);

        [Fact]
        public void Contribution_CanBePosted()
        {
            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Contribution,
                    CreatedAt,
                    executionDate: ExecutionDate);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                TryAssetId,
                QuantityDelta.FromDecimal(30_000m),
                EntryRole.Principal);

            transaction.AttachCashFlowDetail(
                CashFlowCategory.Salary,
                Guid.NewGuid());

            transaction.Post(PostedAt);

            Assert.Equal(
                TransactionStatus.Posted,
                transaction.Status);

            Assert.Equal(
                PostedAt,
                transaction.PostedAtUtc);

            Assert.Single(
                transaction.Entries);
        }

        [Fact]
        public void Contribution_WithoutCashFlowDetail_CannotBePosted()
        {
            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Contribution,
                    CreatedAt,
                    executionDate: ExecutionDate);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                TryAssetId,
                QuantityDelta.FromDecimal(30_000m),
                EntryRole.Principal);

            Assert.Throws<DomainRuleViolationException>(
                () => transaction.Post(PostedAt));
        }

        [Fact]
        public void Buy_WithPositivePrincipalAndNegativeConsideration_CanBePosted()
        {
            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Buy,
                    CreatedAt,
                    executionDate: ExecutionDate);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                AisAssetId,
                QuantityDelta.FromDecimal(
                    6412.34918m),
                EntryRole.Principal,
                UnitPrice.FromDecimal(
                    4.678473m,
                    CurrencyCode.TRY));

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                TryAssetId,
                QuantityDelta.FromDecimal(
                    -30_000m),
                EntryRole.Consideration);

            transaction.Post(PostedAt);

            Assert.Equal(
                TransactionStatus.Posted,
                transaction.Status);

            Assert.Equal(
                2,
                transaction.Entries.Count);
        }

        [Fact]
        public void Buy_WithNegativePrincipal_CannotBePosted()
        {
            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Buy,
                    CreatedAt,
                    executionDate: ExecutionDate);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                AisAssetId,
                QuantityDelta.FromDecimal(-100m),
                EntryRole.Principal);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                TryAssetId,
                QuantityDelta.FromDecimal(-500m),
                EntryRole.Consideration);

            Assert.Throws<DomainRuleViolationException>(
                () => transaction.Post(PostedAt));
        }

        [Fact]
        public void Sell_WithNegativePrincipalAndPositiveConsideration_CanBePosted()
        {
            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Sell,
                    CreatedAt,
                    executionDate: ExecutionDate);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                KpcAssetId,
                QuantityDelta.FromDecimal(-1000m),
                EntryRole.Principal,
                UnitPrice.FromDecimal(
                    12.50m,
                    CurrencyCode.TRY));

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                TryAssetId,
                QuantityDelta.FromDecimal(
                    12_485m),
                EntryRole.Consideration);

            transaction.AddCost(
                CostType.Commission,
                CostTreatment.WithheldFromProceeds,
                Money.FromMinorUnits(
                    1_500,
                    CurrencyCode.TRY));

            transaction.Post(PostedAt);

            Assert.Equal(
                TransactionStatus.Posted,
                transaction.Status);

            Assert.Single(
                transaction.Costs);
        }

        [Fact]
        public void Transfer_WithBalancedAssetMovement_CanBePosted()
        {
            var destinationAccountId =
                Guid.NewGuid();

            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Transfer,
                    CreatedAt,
                    executionDate: ExecutionDate);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                KpcAssetId,
                QuantityDelta.FromDecimal(-100m),
                EntryRole.Transfer);

            transaction.AddEntry(
                PortfolioId,
                destinationAccountId,
                KpcAssetId,
                QuantityDelta.FromDecimal(100m),
                EntryRole.Transfer);

            transaction.Post(PostedAt);

            Assert.Equal(
                TransactionStatus.Posted,
                transaction.Status);
        }

        [Fact]
        public void Transfer_WithUnbalancedAssetMovement_CannotBePosted()
        {
            var destinationAccountId =
                Guid.NewGuid();

            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Transfer,
                    CreatedAt,
                    executionDate: ExecutionDate);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                KpcAssetId,
                QuantityDelta.FromDecimal(-100m),
                EntryRole.Transfer);

            transaction.AddEntry(
                PortfolioId,
                destinationAccountId,
                KpcAssetId,
                QuantityDelta.FromDecimal(90m),
                EntryRole.Transfer);

            Assert.Throws<DomainRuleViolationException>(
                () => transaction.Post(PostedAt));
        }

        [Fact]
        public void OpeningBalance_CanRegisterPreExistingAsset()
        {
            var goldAssetId =
                Guid.NewGuid();

            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.OpeningBalance,
                    CreatedAt,
                    executionDate: ExecutionDate);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                goldAssetId,
                QuantityDelta.FromDecimal(120.45m),
                EntryRole.Principal);

            transaction.Post(PostedAt);

            Assert.Equal(
                TransactionStatus.Posted,
                transaction.Status);
        }

        [Fact]
        public void OpeningBalance_WithUnitPrice_CannotBePosted()
        {
            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.OpeningBalance,
                    CreatedAt,
                    executionDate: ExecutionDate);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                Guid.NewGuid(),
                QuantityDelta.FromDecimal(100m),
                EntryRole.Principal,
                UnitPrice.FromDecimal(
                    5000m,
                    CurrencyCode.TRY));

            Assert.Throws<DomainRuleViolationException>(
                () => transaction.Post(PostedAt));
        }

        [Fact]
        public void PostedTransaction_CannotAcceptNewEntries()
        {
            var transaction =
                CreateValidContribution();

            transaction.Post(PostedAt);

            Assert.Throws<DomainRuleViolationException>(
                () => transaction.AddEntry(
                    PortfolioId,
                    AccountId,
                    TryAssetId,
                    QuantityDelta.FromDecimal(1m),
                    EntryRole.Principal));
        }

        private static LedgerTransaction CreateValidContribution()
        {
            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Contribution,
                    CreatedAt,
                    executionDate: ExecutionDate);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                TryAssetId,
                QuantityDelta.FromDecimal(30_000m),
                EntryRole.Principal);

            transaction.AttachCashFlowDetail(
                CashFlowCategory.Salary);

            return transaction;
        }

        [Fact]
        public void PostedTransaction_CannotBeCancelled()
        {
            var transaction =
                CreateValidContribution();

            transaction.Post(PostedAt);

            Assert.Throws<DomainRuleViolationException>(
                transaction.Cancel);
        }

        [Fact]
        public void Transaction_WithoutExecutionDate_CannotBePosted()
        {
            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Contribution,
                    CreatedAt);

            transaction.AddEntry(
                PortfolioId,
                AccountId,
                TryAssetId,
                QuantityDelta.FromDecimal(30_000m),
                EntryRole.Principal);

            transaction.AttachCashFlowDetail(
                CashFlowCategory.Salary);

            Assert.Throws<DomainRuleViolationException>(
                () => transaction.Post(PostedAt));
        }

        [Fact]
        public void Buy_WithOrderDate_CanBeMarkedOrdered()
        {
            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Buy,
                    CreatedAt,
                    orderDate: ExecutionDate);

            transaction.MarkOrdered();

            Assert.Equal(
                TransactionStatus.Ordered,
                transaction.Status);
        }

        [Fact]
        public void Contribution_CannotBeMarkedOrdered()
        {
            var transaction =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Contribution,
                    CreatedAt,
                    orderDate: ExecutionDate);

            Assert.Throws<DomainRuleViolationException>(
                transaction.MarkOrdered);
        }

        [Fact]
        public void Reversal_CreatesExactInverseEntries()
        {
            var original =
                LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Buy,
                    CreatedAt,
                    executionDate: ExecutionDate);

            original.AddEntry(
                PortfolioId,
                AccountId,
                AisAssetId,
                QuantityDelta.FromDecimal(
                    6412.34918m),
                EntryRole.Principal,
                UnitPrice.FromDecimal(
                    4.678473m,
                    CurrencyCode.TRY));

            original.AddEntry(
                PortfolioId,
                AccountId,
                TryAssetId,
                QuantityDelta.FromDecimal(
                    -30_000m),
                EntryRole.Consideration);

            original.Post(PostedAt);

            var reversal =
                LedgerTransaction.CreateReversal(
                    Guid.NewGuid(),
                    original,
                    PostedAt.AddDays(2),
                    "Correcting an incorrectly recorded purchase.");

            var originalEntries =
                original.Entries
                    .OrderBy(x => x.Sequence)
                    .ToList();

            var reversalEntries =
                reversal.Entries
                    .OrderBy(x => x.Sequence)
                    .ToList();

            Assert.Equal(
                original.Id,
                reversal.ReversalOfTransactionId);

            Assert.Equal(
                original.ExecutionDate,
                reversal.ExecutionDate);

            Assert.Equal(
                originalEntries.Count,
                reversalEntries.Count);

            for (var i = 0;
                 i < originalEntries.Count;
                 i++)
            {
                Assert.Equal(
                    originalEntries[i].AssetId,
                    reversalEntries[i].AssetId);

                Assert.Equal(
                    -originalEntries[i]
                        .QuantityDelta.RawE8,
                    reversalEntries[i]
                        .QuantityDelta.RawE8);

                Assert.Equal(
                    originalEntries[i].Role,
                    reversalEntries[i].Role);

                Assert.Equal(
                    originalEntries[i].UnitPrice,
                    reversalEntries[i].UnitPrice);
            }
        }

        [Fact]
        public void Reversal_CanBePosted()
        {
            var original =
                CreateValidContribution();

            original.Post(PostedAt);

            var reversal =
                LedgerTransaction.CreateReversal(
                    Guid.NewGuid(),
                    original,
                    PostedAt.AddDays(1));

            reversal.Post(
                PostedAt.AddDays(1));

            Assert.Equal(
                TransactionStatus.Posted,
                reversal.Status);
        }

        [Fact]
        public void Reversal_CannotBeCreatedAsOrdinaryDraft()
        {
            Assert.Throws<DomainRuleViolationException>(
                () => LedgerTransaction.CreateDraft(
                    Guid.NewGuid(),
                    HouseholdId,
                    TransactionType.Reversal,
                    CreatedAt,
                    executionDate: ExecutionDate));
        }

        [Fact]
        public void DraftTransaction_CannotBeReversed()
        {
            var original =
                CreateValidContribution();

            Assert.Throws<DomainRuleViolationException>(
                () => LedgerTransaction.CreateReversal(
                    Guid.NewGuid(),
                    original,
                    CreatedAt.AddHours(1)));
        }
    }
}