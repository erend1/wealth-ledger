namespace WealthLedger.Api.Tests
{
    internal enum ApiTestStartupMode
    {
        Blocked,
        Ready,
        InitialBackupRequired,
        WorkspaceUninitialized,
        StorageUninitialized
    }
}
