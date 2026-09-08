using Microsoft.Data.Sqlite;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Setup;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Infrastructure.LocalData
{
    internal sealed class SqliteCoreLedgerSetupSessionFactory
        : ICoreLedgerSetupSessionFactory
    {
        private readonly LocalDataPathResolver _pathResolver;
        private readonly LocalDatabaseOwnershipGuard _ownershipGuard;
        private readonly SqliteDatabaseVerifier _databaseVerifier;

        internal SqliteCoreLedgerSetupSessionFactory(
            LocalDataPathResolver pathResolver,
            LocalDatabaseOwnershipGuard ownershipGuard,
            SqliteDatabaseVerifier databaseVerifier)
        {
            _pathResolver = pathResolver
                ?? throw new ArgumentNullException(nameof(pathResolver));

            _ownershipGuard = ownershipGuard
                ?? throw new ArgumentNullException(nameof(ownershipGuard));

            _databaseVerifier = databaseVerifier
                ?? throw new ArgumentNullException(nameof(databaseVerifier));
        }

        public async Task<
            LocalDataOperationResult<ICoreLedgerSetupSession>> OpenAsync(
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return LocalDataOperationResult<ICoreLedgerSetupSession>.Failed(
                    LocalDataFailureCategory.Cancelled,
                    "Core ledger setup was cancelled before ownership was acquired.");
            }

            var databasePathResult =
                _pathResolver.ResolveDatabasePath();

            if (!databasePathResult.Succeeded)
            {
                return Failure(
                    databasePathResult.Failure!);
            }

            var databasePath =
                databasePathResult.Value!.FullPath;

            if (!File.Exists(databasePath))
            {
                return LocalDataOperationResult<ICoreLedgerSetupSession>.Failed(
                    LocalDataFailureCategory.NotFound,
                    "The database does not exist. Initialize local storage first.");
            }

            var ownershipResult =
                _ownershipGuard.Acquire(
                    databasePath,
                    createDirectory: false);

            if (!ownershipResult.Succeeded)
            {
                return Failure(
                    ownershipResult.Failure!);
            }

            var ownership =
                ownershipResult.Value!;

            try
            {
                var verificationResult =
                    await _databaseVerifier.VerifyAsync(
                        databasePath,
                        cancellationToken);

                if (!verificationResult.Succeeded)
                {
                    await ownership.DisposeAsync();

                    return Failure(
                        verificationResult.Failure!);
                }

                var verification =
                    verificationResult.Value!;

                if (verification.Compatibility
                        != LocalDatabaseCompatibility.Compatible
                    || verification.PendingMigrations.Count != 0)
                {
                    await ownership.DisposeAsync();

                    return LocalDataOperationResult<
                        ICoreLedgerSetupSession>.Failed(
                            LocalDataFailureCategory.DatabaseNotReady,
                            "Core ledger setup requires a current compatible database.");
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    await ownership.DisposeAsync();

                    return LocalDataOperationResult<
                        ICoreLedgerSetupSession>.Failed(
                            LocalDataFailureCategory.Cancelled,
                            "Core ledger setup was cancelled before the setup session opened.");
                }

                var context =
                    SqliteLocalDataConnectionFactory.CreateContext(
                        databasePath,
                        SqliteOpenMode.ReadWrite);

                var store =
                    new EfCoreLedgerSetupStore(
                        context);

                ICoreLedgerSetupSession session =
                    new SqliteCoreLedgerSetupSession(
                        context,
                        store,
                        ownership);

                return LocalDataOperationResult<
                    ICoreLedgerSetupSession>.Success(
                        session);
            }
            catch
            {
                await ownership.DisposeAsync();

                throw;
            }
        }

        private static LocalDataOperationResult<
            ICoreLedgerSetupSession> Failure(
            LocalDataFailure failure)
            => LocalDataOperationResult<
                ICoreLedgerSetupSession>.Failed(
                    failure.Category,
                    failure.Message);
    }

    internal sealed class SqliteCoreLedgerSetupSession
        : ICoreLedgerSetupSession
    {
        private WealthLedgerDbContext? _context;
        private EfCoreLedgerSetupStore? _store;
        private LocalDatabaseOwnershipLease? _ownership;

        internal SqliteCoreLedgerSetupSession(
            WealthLedgerDbContext context,
            EfCoreLedgerSetupStore store,
            LocalDatabaseOwnershipLease ownership)
        {
            _context = context
                ?? throw new ArgumentNullException(nameof(context));

            _store = store
                ?? throw new ArgumentNullException(nameof(store));

            _ownership = ownership
                ?? throw new ArgumentNullException(nameof(ownership));
        }

        public Task<bool> TryInitializeAsync(
            CoreLedgerSetup setup,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(setup);

            var store =
                Volatile.Read(ref _store);

            if (store is null)
            {
                throw new ObjectDisposedException(
                    nameof(SqliteCoreLedgerSetupSession));
            }

            return store.TryInitializeAsync(
                setup,
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _ = Interlocked.Exchange(
                ref _store,
                null);

            var context =
                Interlocked.Exchange(
                    ref _context,
                    null);

            var ownership =
                Interlocked.Exchange(
                    ref _ownership,
                    null);

            try
            {
                if (context is not null)
                {
                    await context.DisposeAsync();
                }
            }
            finally
            {
                if (ownership is not null)
                {
                    await ownership.DisposeAsync();
                }
            }
        }
    }
}
