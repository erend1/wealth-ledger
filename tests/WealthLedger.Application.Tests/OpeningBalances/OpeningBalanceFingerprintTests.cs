using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.OpeningBalances;

public sealed class OpeningBalanceFingerprintTests
{
    [Fact]
    public void ComputeCurrent_IsStableAcrossLotOrderAndTextNormalization()
    {
        var command = CreateCommand();
        var reordered = command with
        {
            ExternalReference = "  SYNTHETIC-STATEMENT  ",
            Note = "  Synthetic opening source.  ",
            Lots = command.Lots.Reverse().ToArray()
        };

        var first =
            RecordOpeningBalanceCommandFingerprint.ComputeCurrent(command);
        var second =
            RecordOpeningBalanceCommandFingerprint.ComputeCurrent(reordered);

        Assert.Equal(first, second);
        Assert.Equal("SHA256", first.AlgorithmCode);
        Assert.Equal(1, first.Version);
        Assert.Equal(64, first.Value.Length);
    }

    [Fact]
    public void ComputeCurrent_ChangesForEveryEconomicLotFact()
    {
        var command = CreateCommand();
        var original =
            RecordOpeningBalanceCommandFingerprint.ComputeCurrent(command);

        var changedLot = command.Lots[0] with
        {
            CostBasis = CostBasis.Known(
                Money.FromMinorUnits(
                    12_346,
                    CurrencyCode.TRY))
        };

        var changed =
            RecordOpeningBalanceCommandFingerprint.ComputeCurrent(
                command with
                {
                    Lots = [changedLot, command.Lots[1]]
                });

        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void Normalize_RejectsIndistinguishableDuplicateLots()
    {
        var command = CreateCommand();

        var exception = Assert.Throws<OpeningBalanceException>(
            () => OpeningBalanceCommandCanonicalizer.Normalize(
                command with
                {
                    Quantity = Quantity.FromDecimal(200m),
                    Lots = [command.Lots[1], command.Lots[1]]
                }));

        Assert.Equal(
            OpeningBalanceErrorCodes.DuplicateLot,
            exception.ErrorCode);
    }

    private static RecordOpeningBalanceCommand CreateCommand()
        => new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            new DateOnly(2026, 8, 31),
            Quantity.FromDecimal(123.456789m),
            "SYNTHETIC-STATEMENT",
            "Synthetic opening source.",
            [
                new OpeningBalanceLotCommand(
                    Quantity.FromDecimal(23.456789m),
                    new DateOnly(2025, 1, 10),
                    CostBasis.Known(
                        Money.FromMinorUnits(
                            12_345,
                            CurrencyCode.TRY))),
                new OpeningBalanceLotCommand(
                    Quantity.FromDecimal(100m),
                    AcquiredOn: null,
                    CostBasis.Unknown())
            ]);
}
