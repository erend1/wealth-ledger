using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Domain.Tests.Ledger;

public sealed class PhysicalGoldTradeDetailTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Buy_CanPreserveOptionalCounterpartyEvidence()
    {
        var transaction = CreateDraft(TransactionType.Buy);
        var counterpartyId = Guid.NewGuid();

        transaction.AttachPhysicalGoldTradeDetail(counterpartyId);

        Assert.Equal(
            counterpartyId,
            transaction.PhysicalGoldTradeDetail!
                .CounterpartyInstitutionId);
    }

    [Fact]
    public void Sell_CanPreserveAnAbsentCounterparty()
    {
        var transaction = CreateDraft(TransactionType.Sell);

        transaction.AttachPhysicalGoldTradeDetail(null);

        Assert.Null(
            transaction.PhysicalGoldTradeDetail!
                .CounterpartyInstitutionId);
    }

    [Fact]
    public void Transfer_CannotCarryTradeCounterpartyEvidence()
    {
        var transaction = CreateDraft(TransactionType.Transfer);

        Assert.Throws<DomainRuleViolationException>(
            () => transaction.AttachPhysicalGoldTradeDetail(
                Guid.NewGuid()));
    }

    private static LedgerTransaction CreateDraft(TransactionType type)
        => LedgerTransaction.CreateDraft(
            Guid.NewGuid(),
            Guid.NewGuid(),
            type,
            CreatedAt,
            executionDate: new DateOnly(2026, 9, 15));
}
