using WealthLedger.Api.Contracts;
using WealthLedger.Application.Inventory;

namespace WealthLedger.Api.Mapping;

internal static class InventoryContractMapper
{
    internal static PositionInventoryResponse ToResponse(
        this PositionInventory inventory)
        => new(
            inventory.HouseholdId,
            inventory.AsOf,
            inventory.Items
                .Select(
                    item => new PositionInventoryItemResponse(
                        item.PortfolioId,
                        item.PortfolioCode,
                        item.PortfolioName,
                        NavigationContractMapper.ToCode(item.PortfolioStatus),
                        item.AccountId,
                        item.AccountCode,
                        item.AccountName,
                        NavigationContractMapper.ToCode(item.AccountType),
                        item.AccountIsActive,
                        item.InstitutionId,
                        item.InstitutionCode,
                        item.InstitutionName,
                        item.InstitutionType is null
                            ? null
                            : NavigationContractMapper.ToCode(item.InstitutionType.Value),
                        item.InstitutionIsActive,
                        item.AssetId,
                        item.AssetCode,
                        item.AssetName,
                        NavigationContractMapper.ToCode(item.AssetType),
                        NavigationContractMapper.ToCode(item.AssetBaseUnit),
                        item.AssetBaseCurrencyCode,
                        NavigationContractMapper.ToCode(item.AssetLotTrackingMode),
                        item.AssetIsActive,
                        item.QuantityRawE8,
                        item.SourceEntryCount,
                        item.SourceTransactionCount))
                .ToArray());

    internal static LotInventoryResponse ToResponse(this LotInventory inventory)
        => new(
            inventory.HouseholdId,
            inventory.AsOf,
            inventory.Items.Select(ToResponse).ToArray(),
            inventory.NextCursor);

    private static LotInventoryItemResponse ToResponse(LotInventoryItem item)
        => new(
            item.AssetLotId,
            item.CreatingTransactionId,
            item.OpeningTransactionEntryId,
            item.AssetId,
            item.AssetCode,
            item.AssetName,
            NavigationContractMapper.ToCode(item.AssetType),
            NavigationContractMapper.ToCode(item.AssetBaseUnit),
            item.AssetBaseCurrencyCode,
            NavigationContractMapper.ToCode(item.AssetLotTrackingMode),
            item.AssetIsActive,
            item.AcquiredOn,
            OpeningBalanceContractMapper.ToCode(item.CostStatus),
            item.CostMinorUnits,
            item.CostCurrencyCode,
            item.OriginalQuantityRawE8,
            item.GlobalQuantityRawE8,
            item.Custody
                .Select(
                    custody => new LotCustodyInventoryResponse(
                        custody.PortfolioId,
                        custody.PortfolioCode,
                        custody.PortfolioName,
                        NavigationContractMapper.ToCode(custody.PortfolioStatus),
                        custody.AccountId,
                        custody.AccountCode,
                        custody.AccountName,
                        NavigationContractMapper.ToCode(custody.AccountType),
                        custody.AccountIsActive,
                        custody.InstitutionId,
                        custody.InstitutionCode,
                        custody.InstitutionName,
                        custody.InstitutionType is null
                            ? null
                            : NavigationContractMapper.ToCode(custody.InstitutionType.Value),
                        custody.InstitutionIsActive,
                        custody.QuantityRawE8,
                        custody.PieceCount,
                        OpeningBalanceContractMapper.FormatExact(custody.FineWeightGrams)))
                .ToArray(),
            item.Allocations
                .Select(
                    allocation => new LotAllocationInventoryResponse(
                        allocation.AllocationId,
                        allocation.TransactionEntryId,
                        allocation.TransactionId,
                        NavigationContractMapper.ToCode(allocation.TransactionType),
                        allocation.ExecutionDate,
                        allocation.PortfolioId,
                        allocation.AccountId,
                        allocation.QuantityDeltaRawE8,
                        allocation.PieceDelta))
                .ToArray(),
            item.PhysicalGold is null
                ? null
                : new PhysicalGoldLotInventoryResponse(
                    item.PhysicalGold.FinenessPpm,
                    item.PhysicalGold.OriginalPieceCount,
                    item.PhysicalGold.GlobalPieceCount,
                    OpeningBalanceContractMapper.FormatExact(item.PhysicalGold.GlobalFineWeightGrams)!,
                    item.PhysicalGold.Hallmark,
                    item.PhysicalGold.CertificateReference,
                    item.PhysicalGold.Note));
}
