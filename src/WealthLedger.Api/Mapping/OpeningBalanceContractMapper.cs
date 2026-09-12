using System.Globalization;
using WealthLedger.Api.Contracts;
using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Api.Mapping;

internal static class OpeningBalanceContractMapper
{
    internal static RecordOpeningBalanceCommand ToCommand(
        this OpeningBalanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AsOfDate == default)
        {
            throw new ArgumentException(
                "Opening as-of date is required.",
                nameof(request));
        }

        if (request.Lots is null)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.LotsRequired,
                "The opening-balance lot collection is required.");
        }

        return new RecordOpeningBalanceCommand(
            request.HouseholdId,
            request.PortfolioId,
            request.AccountId,
            request.AssetId,
            request.AsOfDate,
            ToPositiveQuantity(request.QuantityRawE8),
            request.ExternalReference,
            request.Note,
            request.Lots.Select(ToCommand).ToArray());
    }

    internal static OpeningBalancePreviewResponse ToResponse(
        this OpeningBalancePreview preview)
        => new(
            preview.HouseholdId,
            preview.PortfolioId,
            preview.PortfolioCode,
            preview.PortfolioName,
            preview.AccountId,
            preview.AccountCode,
            preview.AccountName,
            ToCode(preview.AccountType),
            preview.AssetId,
            preview.AssetCode,
            preview.AssetName,
            ToCode(preview.AssetType),
            ToCode(preview.AssetUnit),
            preview.AssetBaseCurrencyCode,
            preview.AsOfDate,
            preview.QuantityRawE8,
            preview.AllocationTotalRawE8,
            preview.AllocationsReconcile,
            preview.UnlottedCostBasisStatus is null
                ? null
                : ToCode(preview.UnlottedCostBasisStatus.Value),
            FormatExact(preview.TotalFineWeightGrams),
            preview.ExternalReference,
            preview.Note,
            preview.Lots.Select(lot => new OpeningBalancePreviewLotResponse(
                lot.Sequence,
                lot.QuantityRawE8,
                lot.AcquiredOn,
                ToCode(lot.CostBasisStatus),
                lot.OriginalCostBasisMinorUnits,
                lot.CostBasisCurrencyCode,
                lot.FinenessPartsPerMillion,
                lot.PieceCount,
                lot.Hallmark,
                lot.CertificateReference,
                lot.Note,
                FormatExact(lot.FineWeightGrams))).ToArray(),
            preview.WarningCodes,
            preview.IsSemanticallyEligible);

    internal static OpeningBalanceVerificationResponse ToResponse(
        this OpeningBalanceVerification verification)
        => new(
            verification.Transaction.ToResponse(),
            verification.PersistedQuantityRawE8,
            verification.AllocationTotalRawE8,
            verification.AllocationsReconcile,
            verification.CurrentPositionRawE8,
            verification.PositionSourceEntryCount,
            verification.CurrentPositionEqualsOpeningQuantity,
            verification.HasAdditionalEffectiveHistory,
            verification.IsIndependentlyReconciled,
            verification.WarningCodes);

    internal static string? FormatExact(decimal? value)
        => value?.ToString(
            "0.############################",
            CultureInfo.InvariantCulture);

    internal static string ToCode(CostBasisStatus value)
        => value switch
        {
            CostBasisStatus.Known => "KNOWN",
            CostBasisStatus.Unknown => "UNKNOWN",
            CostBasisStatus.NotApplicable => "NOT_APPLICABLE",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    internal static string ToCode(InstitutionType value)
        => value switch
        {
            InstitutionType.Bank => "BANK",
            InstitutionType.Broker => "BROKER",
            InstitutionType.AssetManager => "ASSET_MANAGER",
            InstitutionType.Jeweler => "JEWELER",
            InstitutionType.Pension => "PENSION",
            InstitutionType.Other => "OTHER",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    internal static string ToCode(AccountType value)
        => value switch
        {
            AccountType.Cash => "CASH",
            AccountType.Investment => "INVESTMENT",
            AccountType.PhysicalVault => "PHYSICAL_VAULT",
            AccountType.Pension => "PENSION",
            AccountType.PropertyRegistry => "PROPERTY_REGISTRY",
            AccountType.Other => "OTHER",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    internal static string ToCode(AssetType value)
        => value switch
        {
            AssetType.Cash => "CASH",
            AssetType.Currency => "CURRENCY",
            AssetType.Fund => "FUND",
            AssetType.Equity => "EQUITY",
            AssetType.PhysicalGold => "PHYSICAL_GOLD",
            AssetType.RealEstate => "REAL_ESTATE",
            AssetType.Land => "LAND",
            AssetType.Vehicle => "VEHICLE",
            AssetType.Other => "OTHER",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    internal static string ToCode(AssetUnit value)
        => value switch
        {
            AssetUnit.CurrencyUnit => "CURRENCY_UNIT",
            AssetUnit.FundUnit => "FUND_UNIT",
            AssetUnit.Share => "SHARE",
            AssetUnit.GrossGram => "GROSS_GRAM",
            AssetUnit.Piece => "PIECE",
            AssetUnit.Property => "PROPERTY",
            AssetUnit.LandParcel => "LAND_PARCEL",
            AssetUnit.Vehicle => "VEHICLE",
            AssetUnit.Other => "OTHER",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    internal static string ToCode(LotTrackingMode value)
        => value switch
        {
            LotTrackingMode.None => "NONE",
            LotTrackingMode.Optional => "OPTIONAL",
            LotTrackingMode.Required => "REQUIRED",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    internal static InstitutionType ParseInstitutionType(string value)
        => NormalizeCode(value, nameof(value)) switch
        {
            "BANK" => InstitutionType.Bank,
            "BROKER" => InstitutionType.Broker,
            "ASSET_MANAGER" => InstitutionType.AssetManager,
            "JEWELER" => InstitutionType.Jeweler,
            "PENSION" => InstitutionType.Pension,
            "OTHER" => InstitutionType.Other,
            _ => throw InvalidCode("institution type")
        };

    internal static AccountType ParseAccountType(string value)
        => NormalizeCode(value, nameof(value)) switch
        {
            "CASH" => AccountType.Cash,
            "INVESTMENT" => AccountType.Investment,
            "PHYSICAL_VAULT" => AccountType.PhysicalVault,
            "PENSION" => AccountType.Pension,
            _ => throw InvalidCode("opening-balance account type")
        };

    internal static AssetType ParseAssetType(string value)
        => NormalizeCode(value, nameof(value)) switch
        {
            "CASH" => AssetType.Cash,
            "CURRENCY" => AssetType.Currency,
            "FUND" => AssetType.Fund,
            "EQUITY" => AssetType.Equity,
            "PHYSICAL_GOLD" => AssetType.PhysicalGold,
            _ => throw InvalidCode("opening-balance asset type")
        };

    internal static LotTrackingMode ParseLotTrackingMode(string value)
        => NormalizeCode(value, nameof(value)) switch
        {
            "NONE" => LotTrackingMode.None,
            "OPTIONAL" => LotTrackingMode.Optional,
            "REQUIRED" => LotTrackingMode.Required,
            _ => throw InvalidCode("lot-tracking mode")
        };

    private static OpeningBalanceLotCommand ToCommand(
        OpeningBalanceLotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AcquiredOn == default && request.AcquiredOn is not null)
        {
            throw new ArgumentException(
                "Acquisition date is invalid.",
                nameof(request));
        }

        return new OpeningBalanceLotCommand(
            ToPositiveQuantity(request.QuantityRawE8),
            request.AcquiredOn,
            ToCostBasis(request),
            ToGoldDetail(request.PhysicalGoldDetail));
    }

    private static Quantity ToPositiveQuantity(long rawE8)
    {
        if (rawE8 <= 0)
        {
            throw Invalid(
                OpeningBalanceErrorCodes.QuantityInvalid,
                "Opening-balance quantities must be greater than zero.");
        }

        return Quantity.FromRaw(rawE8);
    }

    private static CostBasis ToCostBasis(OpeningBalanceLotRequest request)
    {
        var statusCode = NormalizeCode(
            request.CostBasisStatusCode,
            nameof(request.CostBasisStatusCode));

        try
        {
            return statusCode switch
            {
                "KNOWN" when request.OriginalCostBasisMinorUnits is not null
                             && request.CostBasisCurrencyCode is not null
                    => CostBasis.Known(Money.FromMinorUnits(
                        request.OriginalCostBasisMinorUnits.Value,
                        new CurrencyCode(request.CostBasisCurrencyCode))),

                "UNKNOWN" when request.OriginalCostBasisMinorUnits is null
                               && request.CostBasisCurrencyCode is null
                    => CostBasis.Unknown(),

                "NOT_APPLICABLE" when request.OriginalCostBasisMinorUnits is null
                                      && request.CostBasisCurrencyCode is null
                    => CostBasis.NotApplicable(),

                _ => throw Invalid(
                    OpeningBalanceErrorCodes.CostBasisInvalid,
                    "The opening-lot cost-basis fields are inconsistent.")
            };
        }
        catch (OpeningBalanceException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new OpeningBalanceException(
                OpeningBalanceErrorCategory.Validation,
                OpeningBalanceErrorCodes.CostBasisInvalid,
                "The opening-lot cost basis is invalid.",
                innerException: exception);
        }
    }

    private static PhysicalGoldLotDetail? ToGoldDetail(
        OpeningBalancePhysicalGoldDetailRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        try
        {
            return new PhysicalGoldLotDetail(
                new Fineness(request.FinenessPartsPerMillion),
                request.PieceCount,
                request.Hallmark,
                request.CertificateReference,
                request.Note);
        }
        catch (ArgumentException exception)
        {
            throw new OpeningBalanceException(
                OpeningBalanceErrorCategory.Validation,
                OpeningBalanceErrorCodes.GoldDetailRequired,
                "The physical-gold detail is invalid.",
                innerException: exception);
        }
    }

    private static string NormalizeCode(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim().ToUpperInvariant();
    }

    private static ArgumentException InvalidCode(string subject)
        => new($"The {subject} code is not supported.");

    private static OpeningBalanceException Invalid(
        string code,
        string message)
        => new(
            OpeningBalanceErrorCategory.Validation,
            code,
            message);
}
