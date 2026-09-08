using System.Diagnostics;
using System.Text;

namespace WealthLedger.UI.BrowserTests;

internal sealed class BrowserTestWorkspace : IAsyncDisposable
{
    private readonly string _allowedRoot;
    private readonly List<string> _hostOutputs = [];
    private LocalApiProcess? _host;

    private BrowserTestWorkspace(
        string rootPath,
        string allowedRoot)
    {
        RootPath = rootPath;
        _allowedRoot = allowedRoot;
        DatabasePath = Path.Combine(
            RootPath,
            "data",
            "wealthledger.db");
        BackupDirectory = Path.Combine(
            RootPath,
            "backups");
        BrowserArtifactsDirectory = Path.Combine(
            RootPath,
            "browser-artifacts");

        Directory.CreateDirectory(
            Path.GetDirectoryName(DatabasePath)!);
        Directory.CreateDirectory(BrowserArtifactsDirectory);
    }

    internal string RootPath { get; }

    internal string DatabasePath { get; }

    internal string BackupDirectory { get; }

    internal string BrowserArtifactsDirectory { get; }

    internal Uri? BaseAddress { get; private set; }

    internal bool CleanedUp { get; private set; }

    internal IReadOnlyList<string> HostOutputs => _hostOutputs;

    internal static BrowserTestWorkspace Create()
    {
        var allowedRoot = Path.GetFullPath(
            Path.Combine(
                Path.GetTempPath(),
                "WealthLedger.UI.BrowserTests"));
        var rootPath = Path.Combine(
            allowedRoot,
            Guid.NewGuid().ToString("N"));

        return new BrowserTestWorkspace(
            Path.GetFullPath(rootPath),
            allowedRoot);
    }

    internal async Task<Uri> StartHostAsync()
    {
        if (_host is not null)
        {
            throw new InvalidOperationException(
                "The synthetic browser host is already running.");
        }

        _host = LocalApiProcess.Start(
            DatabasePath,
            BackupDirectory);
        BaseAddress = await _host.WaitForListeningUrlAsync();
        return BaseAddress;
    }

    internal async Task StopHostAsync()
    {
        if (_host is null)
        {
            BaseAddress = null;
            return;
        }

        await _host.StopAsync();
        Assert.True(_host.HasExited);
        _hostOutputs.Add(_host.CombinedOutput);
        await _host.DisposeAsync();
        _host = null;
        BaseAddress = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopHostAsync();

        var resolvedRoot = Path.GetFullPath(RootPath);
        var allowedPrefix = _allowedRoot
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!resolvedRoot.StartsWith(allowedPrefix, comparison))
        {
            throw new InvalidOperationException(
                "Refusing to remove a browser-test path outside its synthetic root.");
        }

        if (Directory.Exists(resolvedRoot))
        {
            Directory.Delete(resolvedRoot, recursive: true);
        }

        CleanedUp = !Directory.Exists(resolvedRoot)
                    && _host is null;
    }

    private sealed class LocalApiProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _standardOutput = new();
        private readonly StringBuilder _standardError = new();
        private readonly object _outputLock = new();
        private readonly TaskCompletionSource<Uri> _listeningUrl =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private LocalApiProcess(Process process)
        {
            _process = process;
        }

        internal bool HasExited => _process.HasExited;

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

        internal static LocalApiProcess Start(
            string databasePath,
            string backupDirectory)
        {
            var apiAssemblyPath = typeof(global::Program).Assembly.Location;
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
            startInfo.ArgumentList.Add(
                "--Backup:DestinationSeparationConfirmed=true");
            startInfo.ArgumentList.Add(
                "--Backup:DestinationEncryptionConfirmed=true");
            startInfo.ArgumentList.Add("--Setup:Enabled=false");
            startInfo.ArgumentList.Add("--Urls=http://127.0.0.1:0");
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
            startInfo.Environment["DOTNET_NOLOGO"] = "1";

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            var result = new LocalApiProcess(process);
            process.OutputDataReceived += result.OnOutput;
            process.ErrorDataReceived += result.OnError;

            if (!process.Start())
            {
                throw new InvalidOperationException(
                    "The synthetic browser host did not start.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return result;
        }

        internal async Task<Uri> WaitForListeningUrlAsync()
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));
            var exitTask = _process.WaitForExitAsync(timeout.Token);
            var timeoutTask = Task.Delay(
                Timeout.InfiniteTimeSpan,
                timeout.Token);
            var completed = await Task.WhenAny(
                _listeningUrl.Task,
                exitTask,
                timeoutTask);

            if (completed == _listeningUrl.Task)
            {
                return await _listeningUrl.Task;
            }

            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    "The synthetic browser host exited before listening.");
            }

            throw new TimeoutException(
                "The synthetic browser host did not begin listening in time.");
        }

        internal async Task StopAsync()
        {
            if (_process.HasExited)
            {
                return;
            }

            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
            _process.WaitForExit();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _process.Dispose();
        }

        private void OnOutput(
            object sender,
            DataReceivedEventArgs eventArgs)
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

            if (markerIndex < 0)
            {
                return;
            }

            var candidate = eventArgs.Data[
                (markerIndex + marker.Length)..].Trim();

            if (Uri.TryCreate(
                    candidate,
                    UriKind.Absolute,
                    out var listeningUri))
            {
                _listeningUrl.TrySetResult(listeningUri);
            }
        }

        private void OnError(
            object sender,
            DataReceivedEventArgs eventArgs)
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
