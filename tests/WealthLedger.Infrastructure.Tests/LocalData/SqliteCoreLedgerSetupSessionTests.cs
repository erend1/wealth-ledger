using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

public sealed class SqliteCoreLedgerSetupSessionTests
{
    [Fact]
    public async Task OpenAsync_CurrentCompatibleDatabase_AcquiresOwnershipUntilDisposed()
    {
        await using var harness =
            await LocalBackupTestHarness.CreateAsync();

        var factory =
            harness.CreateCoreLedgerSetupSessionFactory();

        Assert.True(
            harness.OwnershipGuard.IsAvailable(
                harness.DatabasePath));

        var opened =
            await factory.OpenAsync();

        Assert.True(
            opened.Succeeded,
            opened.Failure?.Message);

        Assert.False(
            harness.OwnershipGuard.IsAvailable(
                harness.DatabasePath));

        await opened.Value!.DisposeAsync();

        Assert.True(
            harness.OwnershipGuard.IsAvailable(
                harness.DatabasePath));
    }

    [Fact]
    public async Task OpenAsync_WhenAnotherSetupSessionOwnsDatabase_ReturnsOwnershipBusy()
    {
        await using var harness =
            await LocalBackupTestHarness.CreateAsync();

        var factory =
            harness.CreateCoreLedgerSetupSessionFactory();

        var first =
            await factory.OpenAsync();

        Assert.True(
            first.Succeeded,
            first.Failure?.Message);

        await using var firstSession =
            first.Value!;

        var second =
            await factory.OpenAsync();

        Assert.False(
            second.Succeeded);

        Assert.NotNull(
            second.Failure);

        Assert.Equal(
            LocalDataFailureCategory.OwnershipBusy,
            second.Failure!.Category);
    }

    [Fact]
    public async Task OpenAsync_AfterPreviousSessionDisposed_CanAcquireOwnershipAgain()
    {
        await using var harness =
            await LocalBackupTestHarness.CreateAsync();

        var factory =
            harness.CreateCoreLedgerSetupSessionFactory();

        var first =
            await factory.OpenAsync();

        Assert.True(
            first.Succeeded,
            first.Failure?.Message);

        await first.Value!.DisposeAsync();

        var second =
            await factory.OpenAsync();

        Assert.True(
            second.Succeeded,
            second.Failure?.Message);

        await second.Value!.DisposeAsync();
    }

    [Fact]
    public async Task OpenAsync_WhenDatabaseRequiresMigration_ReturnsDatabaseNotReady()
    {
        await using var harness =
            await LocalBackupTestHarness.CreateAsync(
                targetMigration:
                    "20260831113310_003_ReversalDependencySemantics");

        var factory =
            harness.CreateCoreLedgerSetupSessionFactory();

        var result =
            await factory.OpenAsync();

        Assert.False(
            result.Succeeded);

        Assert.NotNull(
            result.Failure);

        Assert.Equal(
            LocalDataFailureCategory.DatabaseNotReady,
            result.Failure!.Category);

        Assert.True(
            harness.OwnershipGuard.IsAvailable(
                harness.DatabasePath));
    }

    [Fact]
    public async Task OpenAsync_WhenAlreadyCancelled_ReturnsCancelledWithoutHoldingOwnership()
    {
        await using var harness =
            await LocalBackupTestHarness.CreateAsync();

        var factory =
            harness.CreateCoreLedgerSetupSessionFactory();

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        var result =
            await factory.OpenAsync(
                cancellationSource.Token);

        Assert.False(
            result.Succeeded);

        Assert.NotNull(
            result.Failure);

        Assert.Equal(
            LocalDataFailureCategory.Cancelled,
            result.Failure!.Category);

        Assert.True(
            harness.OwnershipGuard.IsAvailable(
                harness.DatabasePath));
    }
}