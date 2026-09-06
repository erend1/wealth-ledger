using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Net;
using System.Text;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Setup;
using WealthLedger.Infrastructure.LocalData;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure;

namespace WealthLedger.Api.Tests.LocalData;

public sealed class LocalHostingProcessTests : IAsyncLifetime
{
    private readonly string _testRoot;
    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public LocalHostingProcessTests()
    {
        _testRoot =
            Path.Combine(
                Path.GetTempPath(),
                "WealthLedger.Api.Tests",
                nameof(LocalHostingProcessTests),
                Guid.NewGuid().ToString("N"));

        _databasePath =
            Path.Combine(
                _testRoot,
                "live",
                "wealthledger.db");

        _backupDirectory =
            Path.Combine(
                _testRoot,
                "backups");

        Directory.CreateDirectory(
            _backupDirectory);
    }

    [Fact]
    public async Task LocalHosting_DefaultTrackedHostStartsOnlyOnLoopback()
    {
        await InitializeDatabaseAsync();
        await using var process = ApiProcess.Start(_databasePath, _backupDirectory);

        var listeningUrl = await process.WaitForListeningUrlAsync();
        var uri = new Uri(listeningUrl);

        Assert.True(
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(uri.Host, out var address)
            && IPAddress.IsLoopback(address));  

        using var client = new HttpClient();
        using var response = await client.GetAsync(
            new Uri(uri, "/api/not-a-route"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("0.0.0.0", listeningUrl);
    }

    [Fact]
    public async Task LocalHosting_NonLoopbackOverrideExitsBeforeEndpointService()
    {
        await InitializeDatabaseAsync();
        await using var process = ApiProcess.Start(
            _databasePath,
            _backupDirectory,
            "--urls=http://0.0.0.0:0");

        var exitCode = await process.WaitForExitAsync();
        var output = process.CombinedOutput;

        Assert.Equal(
            (int)LocalDataFailureCategory.InvalidInputOrConfiguration,
            exitCode);
        Assert.Contains("STARTUP INVALIDINPUTORCONFIGURATION", output);
        Assert.DoesNotContain("Now listening on", output);
        Assert.DoesNotContain("ConnectionString", output);
        Assert.DoesNotContain(" at ", output);
    }

    [Fact]
    public async Task
        LocalHosting_ReadyOwnershipCollisionBlocksSecondHostAndRecoversAfterExit()
    {
        await PrepareReadyWorkspaceAsync();

        await using var first =
            ApiProcess.Start(
                _databasePath,
                _backupDirectory);

        var firstUrl =
            await first.WaitForListeningUrlAsync();

        Assert.Contains(
            "Local startup mode: Ready",
            first.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        using var firstClient =
            new HttpClient();

        using var firstResponse =
            await firstClient.GetAsync(
                new Uri(
                    new Uri(firstUrl),
                    "/api/households"));

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

        /*
         * The second process sees the first Ready host's
         * process-lifetime ownership lease. It must remain alive
         * in Blocked mode rather than exiting or exposing ledger
         * endpoints.
         */
        await using var collision =
            ApiProcess.Start(
                _databasePath,
                _backupDirectory);

        var collisionUrl =
            await collision.WaitForListeningUrlAsync();

        Assert.Contains(
            "Local startup mode: Blocked",
            collision.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            nameof(LocalDataFailureCategory.OwnershipBusy),
            collision.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        using var collisionClient =
            new HttpClient();

        using var collisionResponse =
            await collisionClient.GetAsync(
                new Uri(
                    new Uri(collisionUrl),
                    "/api/households"));

        Assert.Equal(
            HttpStatusCode.NotFound,
            collisionResponse.StatusCode);

        await first.StopAsync();

        /*
         * The Blocked collision process owns no lifetime lease,
         * so a fresh process can become Ready after the original
         * Ready owner exits.
         */
        await using var restarted =
            ApiProcess.Start(
                _databasePath,
                _backupDirectory);

        var restartedUrl =
            await restarted.WaitForListeningUrlAsync();

        Assert.Contains(
            "Local startup mode: Ready",
            restarted.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        using var restartedClient =
            new HttpClient();

        using var restartedResponse =
            await restartedClient.GetAsync(
                new Uri(
                    new Uri(restartedUrl),
                    "/api/households"));

        Assert.Equal(
            HttpStatusCode.OK,
            restartedResponse.StatusCode);
    }

    [Fact]
    public async Task
        LocalHosting_MissingDatabaseStartsStorageUninitializedWithoutCreatingIt()
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(
                _databasePath)!);

        await using var process =
            ApiProcess.Start(
                _databasePath,
                _backupDirectory);

        var listeningUrl =
            await process.WaitForListeningUrlAsync();

        Assert.False(
            File.Exists(_databasePath));

        Assert.Contains(
            "Local startup mode: StorageUninitialized",
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        using var client =
            new HttpClient();

        using var response =
            await client.GetAsync(
                new Uri(
                    new Uri(listeningUrl),
                    "/api/households"));

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        Assert.DoesNotContain(
            "SQLite Error",
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Data Source",
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        LocalHosting_RetiredStartupSwitchCannotMigratePendingDatabase()
    {
        await InitializeDatabaseAsync(
            "20260827072019_002_CommandReceipt");

        await using var process =
            ApiProcess.Start(
                _databasePath,
                _backupDirectory,
                "--Database:ApplyMigrationsOnStartup=true");

        var listeningUrl =
            await process.WaitForListeningUrlAsync();

        Assert.Contains(
            "Local startup mode: Blocked",
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            nameof(LocalDataFailureCategory.DatabaseNotReady),
            process.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);

        using var client =
            new HttpClient();

        using var response =
            await client.GetAsync(
                new Uri(
                    new Uri(listeningUrl),
                    "/api/households"));

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        await using var context =
            CreateContext();

        var applied =
            await context.Database
                .GetAppliedMigrationsAsync();

        Assert.Equal(
            2,
            applied.Count());

        Assert.DoesNotContain(
            applied,
            migration =>
                migration.EndsWith(
                    "_003_ReversalDependencySemantics",
                    StringComparison.Ordinal));

        Assert.DoesNotContain(
            applied,
            migration =>
                migration.EndsWith(
                    "_004_LedgerNavigationQueries",
                    StringComparison.Ordinal));

        Assert.DoesNotContain(
            applied,
            migration =>
                migration.EndsWith(
                    "_005_WorkspaceIdentity",
                    StringComparison.Ordinal));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        await Task.Yield();

        var resolvedRoot = Path.GetFullPath(_testRoot);
        var allowedRoot = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "WealthLedger.Api.Tests"));

        if (Directory.Exists(resolvedRoot)
            && resolvedRoot.StartsWith(
                allowedRoot + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }
    }

    private async Task InitializeDatabaseAsync(string? targetMigration = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var context = CreateContext();

        if (targetMigration is null)
        {
            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.MigrateAsync(targetMigration);
        }
    }

    private WealthLedgerDbContext CreateContext()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new WealthLedgerDbContext(options);
    }

    private async Task PrepareReadyWorkspaceAsync()
    {
        await InitializeDatabaseAsync();

        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Storage:DatabasePath"] =
                            _databasePath,

                        ["Backup:Directory"] =
                            _backupDirectory,

                        ["Backup:DestinationSeparationConfirmed"] =
                            "true",

                        ["Backup:DestinationEncryptionConfirmed"] =
                            "true"
                    })
                .Build();

        var services =
            new ServiceCollection();

        services.AddSingleton<TimeProvider>(
            TimeProvider.System);

        services.AddWealthLedgerInfrastructure(
            configuration,
            new LocalDataRuntimeContext(
                "Testing",
                _testRoot));

        services.AddScoped<
            InitializeCoreLedgerUseCase>();

        services.AddScoped<
            CreateLocalBackupUseCase>();

        await using var serviceProvider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true
                });

        await using var scope =
            serviceProvider.CreateAsyncScope();

        _ = await scope.ServiceProvider
            .GetRequiredService<
                InitializeCoreLedgerUseCase>()
            .ExecuteAsync(
                ApiTestData.CreateSetupCommand());

        var backupResult =
            await scope.ServiceProvider
                .GetRequiredService<
                    CreateLocalBackupUseCase>()
                .ExecuteAsync();

        Assert.True(
            backupResult.Succeeded,
            backupResult.Failure?.Message);
    }

    private sealed class ApiProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _standardOutput = new();
        private readonly StringBuilder _standardError = new();
        private readonly object _outputLock = new();
        private readonly TaskCompletionSource<string> _listeningUrl =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ApiProcess(Process process)
        {
            _process = process;
        }

        internal string CombinedOutput
        {
            get
            {
                lock (_outputLock)
                {
                    return _standardOutput + Environment.NewLine
                        + _standardError;
                }
            }
        }

        internal static ApiProcess Start(
            string databasePath,
            string backupDirectory,
            params string[] additionalArguments)
        {
            var apiAssemblyPath = typeof(Program).Assembly.Location;
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(apiAssemblyPath)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(apiAssemblyPath);
            startInfo.ArgumentList.Add(
                $"--Storage:DatabasePath={Path.GetFullPath(databasePath)}");
            startInfo.ArgumentList.Add(
                $"--Backup:Directory={Path.GetFullPath(backupDirectory)}");

            foreach (var argument in additionalArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
            startInfo.Environment["DOTNET_NOLOGO"] = "1";
            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            var result = new ApiProcess(process);
            process.OutputDataReceived += result.OnOutput;
            process.ErrorDataReceived += result.OnError;

            if (!process.Start())
            {
                throw new InvalidOperationException("The API process did not start.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return result;
        }

        internal async Task<string> WaitForListeningUrlAsync()
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(20));
            var exitTask = _process.WaitForExitAsync(timeout.Token);
            var completed = await Task.WhenAny(_listeningUrl.Task, exitTask);

            if (completed == _listeningUrl.Task)
            {
                return await _listeningUrl.Task;
            }

            await exitTask;
            throw new InvalidOperationException(
                $"The API exited before listening. Output: {CombinedOutput}");
        }

        internal async Task<int> WaitForExitAsync()
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(20));
            await _process.WaitForExitAsync(timeout.Token);
            _process.WaitForExit();
            return _process.ExitCode;
        }

        internal async Task StopAsync()
        {
            if (_process.HasExited)
            {
                return;
            }

            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _process.Dispose();
        }

        private void OnOutput(object sender, DataReceivedEventArgs eventArgs)
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            lock (_outputLock)
            {
                _standardOutput.AppendLine(eventArgs.Data);
            }

            const string marker = "Now listening on: ";
            var markerIndex = eventArgs.Data.IndexOf(
                marker,
                StringComparison.Ordinal);

            if (markerIndex >= 0)
            {
                _listeningUrl.TrySetResult(
                    eventArgs.Data[(markerIndex + marker.Length)..].Trim());
            }
        }

        private void OnError(object sender, DataReceivedEventArgs eventArgs)
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            lock (_outputLock)
            {
                _standardError.AppendLine(eventArgs.Data);
            }
        }
    }
}
