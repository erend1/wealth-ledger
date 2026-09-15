using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.FundTrades;

/// <summary>
/// Builds the posted transaction graph for a validated fund trade.
/// </summary>
/// <remarks>
/// Entry order is fixed so the persisted shape is deterministic and the
/// database guards can rely on it: principal first, then consideration, then
/// at most one aggregated fee entry and one aggregated tax entry.
/// </remarks>
internal static class FundTradeBuilder
{
    internal static (LedgerTransaction Transaction, TransactionEntry Principal)
        BuildTransaction(
            ValidatedFundTrade validated,
            DateTimeOffset recordedAtUtc)
    {
        var transaction =
            LedgerTransaction.CreateDraft(
                Guid.NewGuid(),
                validated.Scope.HouseholdId,
                validated.TradeType,
                recordedAtUtc,
                orderDate: validated.OrderDate,
                executionDate: validated.ExecutionDate,
                settlementDate: validated.SettlementDate,
                externalReference: validated.ExternalReference,
                note: validated.Note);

        var isPurchase =
            validated.TradeType == TransactionType.Buy;

        var fundDeltaRawE8 =
            isPurchase
                ? validated.FundQuantity.RawE8
                : checked(-validated.FundQuantity.RawE8);

        var principal =
            transaction.AddEntry(
                validated.Scope.PortfolioId,
                validated.Scope.FundAccountId,
                validated.Scope.FundAssetId,
                QuantityDelta.FromRaw(fundDeltaRawE8),
                EntryRole.Principal,
                validated.ExecutedUnitPrice);

        var considerationRawE8 =
            FundTradeCashConversion.ToQuantityRawE8(
                validated.CashConsideration,
                validated.Currency.MinorUnitDigits);

        transaction.AddEntry(
            validated.Scope.PortfolioId,
            validated.Scope.CashAccountId,
            validated.Scope.CashAssetId,
            QuantityDelta.FromRaw(
                isPurchase
                    ? checked(-considerationRawE8)
                    : considerationRawE8),
            EntryRole.Consideration);

        AddSupportingEntry(
            transaction,
            validated,
            EntryRole.Fee,
            validated.Economics.AdditionalFeeTotal);

        AddSupportingEntry(
            transaction,
            validated,
            EntryRole.Tax,
            validated.Economics.AdditionalTaxTotal);

        foreach (var cost in validated.Costs)
        {
            transaction.AddCost(
                cost.Type,
                cost.Treatment,
                cost.Amount,
                cost.Note);
        }

        return (transaction, principal);
    }

    /// <summary>
    /// Creates the single acquisition lot for a purchase.
    /// </summary>
    internal static AssetLot BuildAcquisitionLot(
        ValidatedFundTrade validated,
        TransactionEntry principal,
        DateTimeOffset recordedAtUtc)
    {
        var asset =
            Asset.Create(
                validated.FundAsset.AssetId,
                validated.FundAsset.Code,
                validated.FundAsset.Name,
                validated.FundAsset.Type,
                validated.FundAsset.BaseUnit,
                validated.FundAsset.BaseCurrency,
                validated.FundAsset.LotTrackingMode);

        return AssetLot.Create(
            Guid.NewGuid(),
            asset,
            principal,
            validated.FundQuantity,
            validated.ExecutionDate,
            CostBasis.Known(
                validated.Economics.AcquisitionLotCost!),
            recordedAtUtc);
    }

    /// <summary>
    /// Adds one aggregated supporting entry when additional outflow exists.
    /// </summary>
    /// <remarks>
    /// A zero total adds no entry at all. Both directions decrease cash: a
    /// fee paid on a sale is still money leaving the account.
    /// </remarks>
    private static void AddSupportingEntry(
        LedgerTransaction transaction,
        ValidatedFundTrade validated,
        EntryRole role,
        Money total)
    {
        if (total.MinorUnits == 0)
        {
            return;
        }

        var rawE8 =
            FundTradeCashConversion.ToQuantityRawE8(
                total,
                validated.Currency.MinorUnitDigits);

        transaction.AddEntry(
            validated.Scope.PortfolioId,
            validated.Scope.CashAccountId,
            validated.Scope.CashAssetId,
            QuantityDelta.FromRaw(checked(-rawE8)),
            role);
    }
}
