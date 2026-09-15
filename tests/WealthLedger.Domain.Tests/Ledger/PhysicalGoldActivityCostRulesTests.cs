using WealthLedger.Domain.Common;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Domain.Tests.Ledger;

public sealed class PhysicalGoldActivityCostRulesTests
{
    [Theory]
    [InlineData(TransactionType.Buy, CostType.MakingCharge)]
    [InlineData(TransactionType.Buy, CostType.Commission)]
    [InlineData(TransactionType.Sell, CostType.OtherTax)]
    [InlineData(TransactionType.Transfer, CostType.Insurance)]
    [InlineData(TransactionType.Transfer, CostType.Other)]
    public void IsSupportedCostType_AcceptsBoundedVocabulary(
        TransactionType activity,
        CostType cost)
        => Assert.True(
            PhysicalGoldActivityCostRules.IsSupportedCostType(
                activity,
                cost));

    [Theory]
    [InlineData(TransactionType.Buy, CostType.Insurance)]
    [InlineData(TransactionType.Sell, CostType.MakingCharge)]
    [InlineData(TransactionType.Transfer, CostType.MakingCharge)]
    [InlineData(TransactionType.Sell, CostType.Brokerage)]
    [InlineData(TransactionType.Buy, CostType.WithholdingTax)]
    public void IsSupportedCostType_RejectsUnsupportedVocabulary(
        TransactionType activity,
        CostType cost)
        => Assert.False(
            PhysicalGoldActivityCostRules.IsSupportedCostType(
                activity,
                cost));

    [Fact]
    public void Purchase_RejectsWithheldFromProceeds()
        => Assert.False(
            PhysicalGoldActivityCostRules.IsSupportedTreatment(
                TransactionType.Buy,
                CostTreatment.WithheldFromProceeds));

    [Fact]
    public void Transfer_AcceptsOnlyAdditionalOrInformationalTreatment()
    {
        Assert.True(
            PhysicalGoldActivityCostRules.IsSupportedTreatment(
                TransactionType.Transfer,
                CostTreatment.AdditionalCashOutflow));

        Assert.True(
            PhysicalGoldActivityCostRules.IsSupportedTreatment(
                TransactionType.Transfer,
                CostTreatment.InformationalOnly));

        Assert.False(
            PhysicalGoldActivityCostRules.IsSupportedTreatment(
                TransactionType.Transfer,
                CostTreatment.IncludedInConsideration));
    }

    [Theory]
    [InlineData(CostType.MakingCharge, EntryRole.Fee)]
    [InlineData(CostType.Commission, EntryRole.Fee)]
    [InlineData(CostType.Insurance, EntryRole.Fee)]
    [InlineData(CostType.Other, EntryRole.Fee)]
    [InlineData(CostType.OtherTax, EntryRole.Tax)]
    public void ResolveSupportingEntryRole_IsStable(
        CostType type,
        EntryRole expected)
        => Assert.Equal(
            expected,
            PhysicalGoldActivityCostRules.ResolveSupportingEntryRole(type));

    [Fact]
    public void UnsupportedActivity_FailsClosed()
        => Assert.Throws<DomainRuleViolationException>(
            () => PhysicalGoldActivityCostRules.IsSupportedTreatment(
                TransactionType.Contribution,
                CostTreatment.AdditionalCashOutflow));
}
