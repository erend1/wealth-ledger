using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace WealthLedger.Application.FundTrades;

/// <summary>
/// Identifies exactly which plan a user reviewed.
/// </summary>
/// <remarks>
/// The fingerprint is what makes "show then post" honest. If effective history
/// moves between review and posting, the recomputed plan no longer matches
/// what was displayed, and the sale is refused instead of quietly consuming
/// different lots with different acquisition costs.
///
/// It deliberately covers the scope and the ordered plan rather than the whole
/// command: changing a note should not invalidate a reviewed plan, but
/// changing which lots are consumed must.
/// </remarks>
internal static class FundSalePlanFingerprint
{
    internal const string AlgorithmCode = "SHA256";

    private const int Version1 = 1;

    internal static string Compute(
        FundTradeScope scope,
        long requestedQuantityRawE8,
        IReadOnlyList<ReviewedLotAllocation> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer =
               new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Indented = false,
                       SkipValidation = false
                   }))
        {
            writer.WriteStartObject();

            writer.WriteNumber("version", Version1);

            writer.WriteString(
                "householdId",
                scope.HouseholdId.ToString("D"));

            writer.WriteString(
                "portfolioId",
                scope.PortfolioId.ToString("D"));

            writer.WriteString(
                "fundAccountId",
                scope.FundAccountId.ToString("D"));

            writer.WriteString(
                "fundAssetId",
                scope.FundAssetId.ToString("D"));

            writer.WriteNumber(
                "requestedQuantityRawE8",
                requestedQuantityRawE8);

            writer.WriteStartArray("plan");

            foreach (var line in plan)
            {
                writer.WriteStartObject();

                writer.WriteString(
                    "assetLotId",
                    line.AssetLotId.ToString("D"));

                writer.WriteNumber(
                    "quantityRawE8",
                    line.Quantity.RawE8);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{AlgorithmCode.ToLowerInvariant()}-{Version1}-{Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant()}");
    }

    /// <summary>
    /// Compares a carried fingerprint with a freshly recomputed one in a way
    /// that does not leak timing information about the expected value.
    /// </summary>
    internal static bool Matches(
        string? carried,
        string recomputed)
    {
        if (string.IsNullOrWhiteSpace(carried))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(carried),
            System.Text.Encoding.UTF8.GetBytes(recomputed));
    }
}
