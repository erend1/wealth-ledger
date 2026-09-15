using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Application.PhysicalGold;

internal static class PhysicalGoldCommandFingerprints
{
    internal const string AlgorithmCode = "SHA256";
    internal const int CurrentVersion = 1;

    internal static CommandFingerprint ComputeCurrent(
        PhysicalGoldPurchaseCommand command,
        ValidatedPhysicalGoldTrade? validated = null)
        => Compute(command, AlgorithmCode, CurrentVersion, validated);

    internal static CommandFingerprint Compute(
        PhysicalGoldPurchaseCommand command,
        string algorithmCode,
        int version,
        ValidatedPhysicalGoldTrade? validated = null)
    {
        EnsureSupported(algorithmCode, version);
        ArgumentNullException.ThrowIfNull(command);

        var costs = validated?.Costs
            ?? PhysicalGoldCanonicalizer.NormalizeCosts(
                command.Costs,
                TransactionType.Buy,
                command.CashConsideration.Currency);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Start(writer, LedgerOperationCodes.RecordPhysicalGoldPurchase);
            WriteTradeScope(writer, command);
            writer.WriteNumber("grossWeightRawE8", command.GrossWeight.RawE8);
            writer.WriteNumber("finenessPpm", command.Fineness.Ppm);
            writer.WriteNumber("pieceCount", command.PieceCount);
            WriteMoney(writer, "cashConsideration", command.CashConsideration);
            WritePrice(writer, command.ExecutedUnitPrice);
            WriteNullableGuid(
                writer,
                "counterpartyInstitutionId",
                command.CounterpartyInstitutionId);
            WriteDates(
                writer,
                command.OrderDate,
                command.ExecutionDate,
                command.SettlementDate);
            WriteCosts(writer, costs);
            WriteNullableText(
                writer,
                "hallmark",
                validated?.Hallmark
                    ?? PhysicalGoldCanonicalizer.NormalizeHallmark(
                        command.Hallmark));
            WriteNullableText(
                writer,
                "certificateReference",
                validated?.CertificateReference
                    ?? PhysicalGoldCanonicalizer.NormalizeCertificateReference(
                        command.CertificateReference));
            WriteNullableText(
                writer,
                "lotNote",
                validated?.LotNote
                    ?? PhysicalGoldCanonicalizer.NormalizeLotNote(
                        command.LotNote));
            WriteCommonText(writer, command.ExternalReference, command.Note,
                validated?.ExternalReference, validated?.Note);
            End(writer);
        }

        return Hash(buffer);
    }

    internal static CommandFingerprint ComputeCurrent(
        PhysicalGoldSaleCommand command,
        ValidatedPhysicalGoldTrade? validated = null)
        => Compute(command, AlgorithmCode, CurrentVersion, validated);

    internal static CommandFingerprint Compute(
        PhysicalGoldSaleCommand command,
        string algorithmCode,
        int version,
        ValidatedPhysicalGoldTrade? validated = null)
    {
        EnsureSupported(algorithmCode, version);
        ArgumentNullException.ThrowIfNull(command);

        var costs = validated?.Costs
            ?? PhysicalGoldCanonicalizer.NormalizeCosts(
                command.Costs,
                TransactionType.Sell,
                command.CashConsideration.Currency);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Start(writer, LedgerOperationCodes.RecordPhysicalGoldSale);
            WriteTradeScope(writer, command);
            writer.WriteNumber("grossWeightRawE8", command.GrossWeight.RawE8);
            writer.WriteNumber("pieceCount", command.PieceCount);
            WriteSelections(writer, command.SelectedLots);
            WriteMoney(writer, "cashConsideration", command.CashConsideration);
            WritePrice(writer, command.ExecutedUnitPrice);
            WriteNullableGuid(
                writer,
                "counterpartyInstitutionId",
                command.CounterpartyInstitutionId);
            WriteDates(
                writer,
                command.OrderDate,
                command.ExecutionDate,
                command.SettlementDate);
            WriteCosts(writer, costs);
            WriteCommonText(writer, command.ExternalReference, command.Note,
                validated?.ExternalReference, validated?.Note);
            End(writer);
        }

        return Hash(buffer);
    }

    internal static CommandFingerprint ComputeCurrent(
        PhysicalGoldTransferCommand command,
        ValidatedPhysicalGoldTransfer? validated = null)
        => Compute(command, AlgorithmCode, CurrentVersion, validated);

    internal static CommandFingerprint Compute(
        PhysicalGoldTransferCommand command,
        string algorithmCode,
        int version,
        ValidatedPhysicalGoldTransfer? validated = null)
    {
        EnsureSupported(algorithmCode, version);
        ArgumentNullException.ThrowIfNull(command);

        var costs = validated?.Costs
            ?? (command.Costs is null || command.Costs.Count == 0
                ? []
                : PhysicalGoldCanonicalizer.NormalizeCosts(
                    command.Costs,
                    TransactionType.Transfer,
                    command.Costs[0].Amount.Currency));
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Start(writer, LedgerOperationCodes.RecordPhysicalGoldTransfer);
            writer.WriteString(
                "householdId", command.HouseholdId.ToString("D"));
            writer.WriteString(
                "sourcePortfolioId",
                command.SourcePortfolioId.ToString("D"));
            writer.WriteString(
                "sourceGoldAccountId",
                command.SourceGoldAccountId.ToString("D"));
            writer.WriteString(
                "destinationPortfolioId",
                command.DestinationPortfolioId.ToString("D"));
            writer.WriteString(
                "destinationGoldAccountId",
                command.DestinationGoldAccountId.ToString("D"));
            writer.WriteString(
                "goldAssetId", command.GoldAssetId.ToString("D"));
            WriteNullableGuid(
                writer, "cashPortfolioId", command.CashPortfolioId);
            WriteNullableGuid(
                writer, "cashAccountId", command.CashAccountId);
            WriteNullableGuid(
                writer, "cashAssetId", command.CashAssetId);
            writer.WriteNumber("grossWeightRawE8", command.GrossWeight.RawE8);
            writer.WriteNumber("pieceCount", command.PieceCount);
            WriteSelections(writer, command.SelectedLots);
            writer.WriteString(
                "executionDate",
                command.ExecutionDate.ToString(
                    "yyyy-MM-dd", CultureInfo.InvariantCulture));
            WriteCosts(writer, costs);
            WriteCommonText(writer, command.ExternalReference, command.Note,
                validated?.ExternalReference, validated?.Note);
            End(writer);
        }

        return Hash(buffer);
    }

    private static void WriteTradeScope(
        Utf8JsonWriter writer,
        PhysicalGoldPurchaseCommand command)
    {
        writer.WriteString("householdId", command.HouseholdId.ToString("D"));
        writer.WriteString("portfolioId", command.PortfolioId.ToString("D"));
        writer.WriteString(
            "goldAccountId", command.GoldAccountId.ToString("D"));
        writer.WriteString(
            "cashAccountId", command.CashAccountId.ToString("D"));
        writer.WriteString("goldAssetId", command.GoldAssetId.ToString("D"));
        writer.WriteString("cashAssetId", command.CashAssetId.ToString("D"));
    }

    private static void WriteTradeScope(
        Utf8JsonWriter writer,
        PhysicalGoldSaleCommand command)
    {
        writer.WriteString("householdId", command.HouseholdId.ToString("D"));
        writer.WriteString("portfolioId", command.PortfolioId.ToString("D"));
        writer.WriteString(
            "goldAccountId", command.GoldAccountId.ToString("D"));
        writer.WriteString(
            "cashAccountId", command.CashAccountId.ToString("D"));
        writer.WriteString("goldAssetId", command.GoldAssetId.ToString("D"));
        writer.WriteString("cashAssetId", command.CashAssetId.ToString("D"));
    }

    private static void Start(Utf8JsonWriter writer, string operation)
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", CurrentVersion);
        writer.WriteString("operation", operation);
    }

    private static void End(Utf8JsonWriter writer)
    {
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteDates(
        Utf8JsonWriter writer,
        DateOnly? orderDate,
        DateOnly executionDate,
        DateOnly? settlementDate)
    {
        WriteNullableDate(writer, "orderDate", orderDate);
        writer.WriteString(
            "executionDate",
            executionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        WriteNullableDate(writer, "settlementDate", settlementDate);
    }

    private static void WritePrice(Utf8JsonWriter writer, Domain.ValueObjects.UnitPrice? price)
    {
        writer.WritePropertyName("executedUnitPrice");
        if (price is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteNumber("rawE8", price.RawE8);
        writer.WriteString("currency", price.Currency.Value);
        writer.WriteEndObject();
    }

    private static void WriteMoney(
        Utf8JsonWriter writer,
        string name,
        Domain.ValueObjects.Money money)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("minorUnits", money.MinorUnits);
        writer.WriteString("currency", money.Currency.Value);
        writer.WriteEndObject();
    }

    private static void WriteCosts(
        Utf8JsonWriter writer,
        IReadOnlyList<PhysicalGoldCostInput> costs)
    {
        writer.WriteStartArray("costs");
        foreach (var cost in PhysicalGoldCanonicalizer.OrderCosts(costs))
        {
            writer.WriteStartObject();
            writer.WriteString(
                "type", PhysicalGoldCanonicalizer.ToCostTypeCode(cost.Type));
            writer.WriteString(
                "treatment",
                PhysicalGoldCanonicalizer.ToTreatmentCode(cost.Treatment));
            writer.WriteNumber("amountMinorUnits", cost.Amount.MinorUnits);
            writer.WriteString("currency", cost.Amount.Currency.Value);
            WriteNullableText(writer, "note", cost.Note);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSelections(
        Utf8JsonWriter writer,
        IReadOnlyCollection<PhysicalGoldSelectedLot> selections)
    {
        ArgumentNullException.ThrowIfNull(selections);
        writer.WriteStartArray("selectedLots");
        foreach (var line in PhysicalGoldCanonicalizer.OrderSelections(selections))
        {
            writer.WriteStartObject();
            writer.WriteString("assetLotId", line.AssetLotId.ToString("D"));
            writer.WriteNumber("grossWeightRawE8", line.GrossWeight.RawE8);
            writer.WriteNumber("pieceCount", line.PieceCount);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteCommonText(
        Utf8JsonWriter writer,
        string? externalReference,
        string? note,
        string? validatedExternalReference,
        string? validatedNote)
    {
        WriteNullableText(
            writer,
            "externalReference",
            validatedExternalReference
                ?? PhysicalGoldCanonicalizer.NormalizeExternalReference(
                    externalReference));
        WriteNullableText(
            writer,
            "note",
            validatedNote ?? PhysicalGoldCanonicalizer.NormalizeNote(note));
    }

    private static void WriteNullableGuid(
        Utf8JsonWriter writer,
        string name,
        Guid? value)
    {
        writer.WritePropertyName(name);
        if (value is Guid id)
        {
            writer.WriteStringValue(id.ToString("D"));
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private static void WriteNullableDate(
        Utf8JsonWriter writer,
        string name,
        DateOnly? value)
    {
        writer.WritePropertyName(name);
        if (value is DateOnly date)
        {
            writer.WriteStringValue(
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private static void WriteNullableText(
        Utf8JsonWriter writer,
        string name,
        string? value)
    {
        writer.WritePropertyName(name);
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }

    private static CommandFingerprint Hash(ArrayBufferWriter<byte> buffer)
        => new(
            AlgorithmCode,
            CurrentVersion,
            Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan))
                .ToLowerInvariant());

    private static void EnsureSupported(string algorithmCode, int version)
    {
        if (algorithmCode != AlgorithmCode || version != CurrentVersion)
        {
            throw new NotSupportedException(
                $"Physical-gold fingerprint '{algorithmCode}' version '{version}' is not supported.");
        }
    }
}

internal static class PhysicalGoldIdempotency
{
    internal static void ValidateKey(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (idempotencyKey.Length > 256
            || idempotencyKey.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The idempotency key is not in a supported form.",
                nameof(idempotencyKey));
        }
    }
}
