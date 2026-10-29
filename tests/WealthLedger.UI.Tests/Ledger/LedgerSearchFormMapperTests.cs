using WealthLedger.Application.LedgerSearch;
using WealthLedger.Domain.Ledger;
using WealthLedger.UI.Pages.Ledger;

namespace WealthLedger.UI.Tests.Ledger;

public sealed class LedgerSearchFormMapperTests
{
    private static readonly Guid HouseholdId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public void TryMap_MapsEveryFilterAndLeavesStatusToThePostedDefault()
    {
        var assetId = Guid.NewGuid();
        var institutionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var form = new LedgerSearchForm
        {
            ExecutedFrom = "2026-01-01",
            ExecutedTo = " 2026-01-31 ",
            Type = ["BUY", "CONTRIBUTION", ""],
            AssetId = assetId.ToString("D"),
            InstitutionId = institutionId.ToString("D"),
            AccountId = accountId.ToString("D"),
            PortfolioId = portfolioId.ToString("D"),
            Reference = "  synth-ref ",
            Reversal = "REVERSED_ORIGINAL",
            Cursor = "opaque"
        };

        var mapped = LedgerSearchFormMapper.TryMap(
            form,
            HouseholdId,
            pageSize: 7,
            out var query,
            out var errors);

        Assert.True(mapped);
        Assert.Empty(errors);
        Assert.NotNull(query);
        Assert.Equal(HouseholdId, query.HouseholdId);
        Assert.Equal(new DateOnly(2026, 1, 1), query.ExecutedFrom);
        Assert.Equal(new DateOnly(2026, 1, 31), query.ExecutedTo);
        Assert.Equal(
            [TransactionType.Buy, TransactionType.Contribution],
            query.Types);
        Assert.Null(query.Statuses);
        Assert.Equal(assetId, query.AssetId);
        Assert.Equal(institutionId, query.InstitutionId);
        Assert.Equal(accountId, query.AccountId);
        Assert.Equal(portfolioId, query.PortfolioId);
        Assert.Equal("synth-ref", query.ExternalReferenceContains);
        Assert.Equal(
            LedgerSearchReversalRelationship.ReversedOriginal,
            query.ReversalRelationship);
        Assert.Equal(7, query.PageSize);
        Assert.Equal("opaque", query.Cursor);
    }

    [Fact]
    public void TryMap_AnEmptyFormIsAnUnfilteredSearch()
    {
        var mapped = LedgerSearchFormMapper.TryMap(
            new LedgerSearchForm { Reference = "   ", Reversal = "ANY" },
            HouseholdId,
            LedgerSearchFormMapper.DefaultPageSize,
            out var query,
            out _);

        Assert.True(mapped);
        Assert.NotNull(query);
        Assert.Null(query.ExecutedFrom);
        Assert.Empty(query.Types!);
        Assert.Null(query.ExternalReferenceContains);
        Assert.Equal(LedgerSearchReversalRelationship.Any, query.ReversalRelationship);
        Assert.False(LedgerSearchFormMapper.HasFilters(
            new LedgerSearchForm { Reversal = "ANY", PageSize = "5", Cursor = "x" }));
    }

    [Theory]
    [InlineData("ExecutedFrom", "2026-02-30", "LedgerSearch_Validation_Date")]
    [InlineData("ExecutedFrom", "01.02.2026", "LedgerSearch_Validation_Date")]
    [InlineData("Type", "NOT_A_TYPE", "LedgerSearch_Validation_Type")]
    [InlineData("AssetId", "not-a-guid", "LedgerSearch_Validation_Selection")]
    [InlineData("AccountId", "00000000-0000-0000-0000-000000000000", "LedgerSearch_Validation_Selection")]
    [InlineData("PortfolioId", "{5f2b0c3e-0000-0000-0000-000000000001}", "LedgerSearch_Validation_Selection")]
    [InlineData("Reversal", "SIDEWAYS", "LedgerSearch_Validation_Reversal")]
    public void TryMap_RejectsAMalformedValueWithAFieldMessage(
        string field,
        string value,
        string resourceKey)
    {
        var form = new LedgerSearchForm();

        switch (field)
        {
            case "ExecutedFrom":
                form.ExecutedFrom = value;
                break;
            case "Type":
                form.Type = [value];
                break;
            case "AssetId":
                form.AssetId = value;
                break;
            case "AccountId":
                form.AccountId = value;
                break;
            case "PortfolioId":
                form.PortfolioId = value;
                break;
            case "Reversal":
                form.Reversal = value;
                break;
        }

        var mapped = LedgerSearchFormMapper.TryMap(
            form,
            HouseholdId,
            LedgerSearchFormMapper.DefaultPageSize,
            out var query,
            out var errors);

        Assert.False(mapped);
        Assert.Null(query);
        Assert.Equal(new LedgerSearchFormError(field, resourceKey), Assert.Single(errors));
    }

    [Fact]
    public void TryMap_RejectsAnInvertedRangeAndAnOversizedReference()
    {
        var mapped = LedgerSearchFormMapper.TryMap(
            new LedgerSearchForm
            {
                ExecutedFrom = "2026-02-01",
                ExecutedTo = "2026-01-01",
                Reference = new string('r', LedgerSearchFormMapper.MaximumReferenceLength + 1)
            },
            HouseholdId,
            LedgerSearchFormMapper.DefaultPageSize,
            out _,
            out var errors);

        Assert.False(mapped);
        Assert.Equal(
            [
                new LedgerSearchFormError("ExecutedTo", "LedgerSearch_Validation_DateRange"),
                new LedgerSearchFormError("Reference", "LedgerSearch_Validation_Reference")
            ],
            errors);
    }

    [Theory]
    [InlineData(null, true, 20)]
    [InlineData("", true, 20)]
    [InlineData("1", true, 1)]
    [InlineData("100", true, 100)]
    [InlineData("0", false, 0)]
    [InlineData("101", false, 101)]
    [InlineData("-5", false, 0)]
    [InlineData("ten", false, 0)]
    public void TryParsePageSize_AcceptsOnlyTheBoundedRange(
        string? value,
        bool accepted,
        int expected)
    {
        var parsed = LedgerSearchFormMapper.TryParsePageSize(value, out var pageSize);

        Assert.Equal(accepted, parsed);

        if (accepted)
        {
            Assert.Equal(expected, pageSize);
        }
    }

    [Fact]
    public void NextPageQuery_KeepsEveryFilterBetweenPageSizeAndCursor()
    {
        var form = new LedgerSearchForm
        {
            ExecutedFrom = " 2026-01-01",
            Type = ["BUY", "SELL"],
            AccountId = "51000000-0000-0000-0000-0000000000a1",
            Reference = "a&b",
            Reversal = "ANY",
            Cursor = "old-cursor"
        };

        var query = LedgerSearchFormMapper.NextPageQuery(form, 1, "next+cursor/=").Value;

        Assert.Equal(
            "?pageSize=1&executedFrom=2026-01-01&type=BUY&type=SELL"
            + "&accountId=51000000-0000-0000-0000-0000000000a1"
            + "&reference=a%26b&cursor=next%2Bcursor%2F%3D",
            query);
        Assert.DoesNotContain("old-cursor", query, StringComparison.Ordinal);
        Assert.DoesNotContain("reversal", query, StringComparison.Ordinal);
    }
}
