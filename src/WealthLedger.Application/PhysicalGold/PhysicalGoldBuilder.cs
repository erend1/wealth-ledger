using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.PhysicalGold;

internal static class PhysicalGoldBuilder
{
    internal static (LedgerTransaction Transaction, TransactionEntry Principal)
        BuildTrade(
            ValidatedPhysicalGoldTrade validated,
            DateTimeOffset recordedAtUtc)
    {
        var transaction = LedgerTransaction.CreateDraft(
            Guid.NewGuid(),
            validated.Scope.HouseholdId,
            validated.TradeType,
            recordedAtUtc,
            validated.OrderDate,
            validated.ExecutionDate,
            validated.SettlementDate,
            validated.ExternalReference,
            validated.Note);
        var isPurchase = validated.TradeType == TransactionType.Buy;
        var principal = transaction.AddEntry(
            validated.Scope.PortfolioId,
            validated.Scope.GoldAccountId,
            validated.Scope.GoldAssetId,
            QuantityDelta.FromRaw(
                isPurchase
                    ? validated.GrossWeight.RawE8
                    : checked(-validated.GrossWeight.RawE8)),
            EntryRole.Principal,
            validated.ExecutedUnitPrice);

        var cashRawE8 = PhysicalGoldCashConversion.ToQuantityRawE8(
            validated.CashConsideration,
            validated.Currency.MinorUnitDigits);
        transaction.AddEntry(
            validated.Scope.PortfolioId,
            validated.Scope.CashAccountId,
            validated.Scope.CashAssetId,
            QuantityDelta.FromRaw(
                isPurchase ? checked(-cashRawE8) : cashRawE8),
            EntryRole.Consideration);

        AddSupportingEntry(
            transaction,
            validated.Scope.PortfolioId,
            validated.Scope.CashAccountId,
            validated.Scope.CashAssetId,
            validated.Currency.MinorUnitDigits,
            EntryRole.Fee,
            validated.Economics.AdditionalFeeTotal);
        AddSupportingEntry(
            transaction,
            validated.Scope.PortfolioId,
            validated.Scope.CashAccountId,
            validated.Scope.CashAssetId,
            validated.Currency.MinorUnitDigits,
            EntryRole.Tax,
            validated.Economics.AdditionalTaxTotal);

        foreach (var cost in validated.Costs)
        {
            transaction.AddCost(
                cost.Type, cost.Treatment, cost.Amount, cost.Note);
        }

        transaction.AttachPhysicalGoldTradeDetail(
            validated.Counterparty?.InstitutionId);
        return (transaction, principal);
    }

    internal static AssetLot BuildAcquisitionLot(
        ValidatedPhysicalGoldTrade validated,
        TransactionEntry principal,
        DateTimeOffset recordedAtUtc)
    {
        if (validated.TradeType != TransactionType.Buy
            || validated.Fineness is null
            || validated.Economics.AcquisitionLotCost is null)
        {
            throw new InvalidOperationException(
                "Only a validated purchase can create a physical-gold lot.");
        }

        var asset = Asset.Create(
            validated.GoldAsset.AssetId,
            validated.GoldAsset.Code,
            validated.GoldAsset.Name,
            validated.GoldAsset.Type,
            validated.GoldAsset.BaseUnit,
            validated.GoldAsset.BaseCurrency,
            validated.GoldAsset.LotTrackingMode);
        var detail = new PhysicalGoldLotDetail(
            validated.Fineness,
            validated.PieceCount,
            validated.Hallmark,
            validated.CertificateReference,
            validated.LotNote);

        return AssetLot.Create(
            Guid.NewGuid(),
            asset,
            principal,
            validated.GrossWeight,
            validated.ExecutionDate,
            CostBasis.Known(validated.Economics.AcquisitionLotCost),
            recordedAtUtc,
            detail);
    }

    internal static (
        LedgerTransaction Transaction,
        TransactionEntry Source,
        TransactionEntry Destination) BuildTransfer(
            ValidatedPhysicalGoldTransfer validated,
            DateTimeOffset recordedAtUtc)
    {
        var transaction = LedgerTransaction.CreateDraft(
            Guid.NewGuid(),
            validated.Scope.HouseholdId,
            TransactionType.Transfer,
            recordedAtUtc,
            orderDate: null,
            executionDate: validated.ExecutionDate,
            settlementDate: null,
            externalReference: validated.ExternalReference,
            note: validated.Note);
        var source = transaction.AddEntry(
            validated.Scope.SourcePortfolioId,
            validated.Scope.SourceGoldAccountId,
            validated.Scope.GoldAssetId,
            QuantityDelta.FromRaw(
                checked(-validated.GrossWeight.RawE8)),
            EntryRole.Transfer);
        var destination = transaction.AddEntry(
            validated.Scope.DestinationPortfolioId,
            validated.Scope.DestinationGoldAccountId,
            validated.Scope.GoldAssetId,
            QuantityDelta.FromRaw(validated.GrossWeight.RawE8),
            EntryRole.Transfer);

        if (validated.CashPortfolio is not null
            && validated.CashAccount is not null
            && validated.CashAsset is not null)
        {
            AddSupportingEntry(
                transaction,
                validated.CashPortfolio.PortfolioId,
                validated.CashAccount.AccountId,
                validated.CashAsset.AssetId,
                validated.Currency.MinorUnitDigits,
                EntryRole.Fee,
                validated.Economics.AdditionalFeeTotal);
            AddSupportingEntry(
                transaction,
                validated.CashPortfolio.PortfolioId,
                validated.CashAccount.AccountId,
                validated.CashAsset.AssetId,
                validated.Currency.MinorUnitDigits,
                EntryRole.Tax,
                validated.Economics.AdditionalTaxTotal);
        }

        foreach (var cost in validated.Costs)
        {
            transaction.AddCost(
                cost.Type, cost.Treatment, cost.Amount, cost.Note);
        }

        return (transaction, source, destination);
    }

    private static void AddSupportingEntry(
        LedgerTransaction transaction,
        Guid portfolioId,
        Guid accountId,
        Guid assetId,
        int minorUnitDigits,
        EntryRole role,
        Money amount)
    {
        if (amount.MinorUnits == 0)
        {
            return;
        }

        transaction.AddEntry(
            portfolioId,
            accountId,
            assetId,
            QuantityDelta.FromRaw(
                checked(-PhysicalGoldCashConversion.ToQuantityRawE8(
                    amount, minorUnitDigits))),
            role);
    }
}
