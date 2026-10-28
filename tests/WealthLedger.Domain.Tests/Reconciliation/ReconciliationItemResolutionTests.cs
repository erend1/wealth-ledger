using WealthLedger.Domain.Reconciliation;

namespace WealthLedger.Domain.Tests.Reconciliation;

public sealed class ReconciliationItemResolutionTests
{
    private static readonly Guid ItemId =
        Guid.Parse("e3000000-0000-0000-0000-000000000001");

    private static readonly Guid TransactionId =
        Guid.Parse("e3000000-0000-0000-0000-0000000000f1");

    private static readonly DateTimeOffset ResolvedAt =
        new(2026, 10, 3, 10, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void CorrectedByTransaction_CarriesOnlyTheTransaction()
    {
        var resolution = ReconciliationItemResolution.CorrectedByTransaction(
            ItemId,
            TransactionId,
            ResolvedAt);

        Assert.Equal(ItemId, resolution.ItemId);
        Assert.Equal(ReconciliationResolutionKind.CorrectedByTransaction, resolution.Kind);
        Assert.Equal(TransactionId, resolution.CorrectiveTransactionId);
        Assert.Null(resolution.Explanation);
        Assert.Equal(TimeSpan.Zero, resolution.ResolvedAtUtc.Offset);
        Assert.Equal(ResolvedAt.UtcDateTime, resolution.ResolvedAtUtc.UtcDateTime);
    }

    [Fact]
    public void DocumentedException_NormalizesTheExplanation()
    {
        var resolution = ReconciliationItemResolution.DocumentedException(
            ItemId,
            "  Synthetic rounding on the custodian report.\r\nReviewed.  ",
            ResolvedAt);

        Assert.Equal(ReconciliationResolutionKind.DocumentedException, resolution.Kind);
        Assert.Null(resolution.CorrectiveTransactionId);
        Assert.Equal(
            "Synthetic rounding on the custodian report.\r\nReviewed.",
            resolution.Explanation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bell\u0007")]
    [InlineData("override‮")]
    public void DocumentedException_RequiresASafeExplanation(string explanation)
    {
        Assert.Throws<ArgumentException>(
            () => ReconciliationItemResolution.DocumentedException(
                ItemId,
                explanation,
                ResolvedAt));
    }

    [Fact]
    public void DocumentedException_BoundsTheExplanation()
    {
        var limit = new string('x', ReconciliationItemResolution.MaximumExplanationLength);

        Assert.Equal(
            limit,
            ReconciliationItemResolution.DocumentedException(ItemId, limit, ResolvedAt).Explanation);
        Assert.Throws<ArgumentException>(
            () => ReconciliationItemResolution.DocumentedException(ItemId, limit + "x", ResolvedAt));
    }

    [Fact]
    public void Factories_RejectEmptyIdentities()
    {
        Assert.Throws<ArgumentException>(
            () => ReconciliationItemResolution.CorrectedByTransaction(Guid.Empty, TransactionId, ResolvedAt));
        Assert.Throws<ArgumentException>(
            () => ReconciliationItemResolution.CorrectedByTransaction(ItemId, Guid.Empty, ResolvedAt));
        Assert.Throws<ArgumentException>(
            () => ReconciliationItemResolution.DocumentedException(Guid.Empty, "Synthetic reason", ResolvedAt));
    }

    [Fact]
    public void HasSameFacts_ComparesKindTransactionAndExplanationOnly()
    {
        var corrected = ReconciliationItemResolution.CorrectedByTransaction(ItemId, TransactionId, ResolvedAt);
        var documented = ReconciliationItemResolution.DocumentedException(ItemId, "Synthetic reason", ResolvedAt);

        Assert.True(corrected.HasSameFacts(
            ReconciliationItemResolution.CorrectedByTransaction(ItemId, TransactionId, ResolvedAt.AddDays(1))));
        Assert.False(corrected.HasSameFacts(
            ReconciliationItemResolution.CorrectedByTransaction(ItemId, Guid.NewGuid(), ResolvedAt)));
        Assert.False(corrected.HasSameFacts(documented));
        Assert.True(documented.HasSameFacts(
            ReconciliationItemResolution.DocumentedException(ItemId, " Synthetic reason ", ResolvedAt)));
        Assert.False(documented.HasSameFacts(
            ReconciliationItemResolution.DocumentedException(ItemId, "Another synthetic reason", ResolvedAt)));
    }

    [Fact]
    public void ToString_NeverPrintsTheExplanation()
    {
        var resolution = ReconciliationItemResolution.DocumentedException(
            ItemId,
            "Synthetic private reason",
            ResolvedAt);

        Assert.Equal(nameof(ReconciliationItemResolution), resolution.ToString());
    }
}
