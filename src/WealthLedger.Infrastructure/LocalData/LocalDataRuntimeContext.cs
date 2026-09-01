namespace WealthLedger.Infrastructure.LocalData;

public sealed record LocalDataRuntimeContext(
    string EnvironmentName,
    string ContentRootPath);

internal sealed record LocalDataPathEnvironment(
    string EnvironmentName,
    string ContentRootPath,
    string ApplicationBasePath,
    string CurrentDirectory,
    string LocalApplicationDataPath,
    string UserProfilePath)
{
    internal static LocalDataPathEnvironment Create(
        LocalDataRuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);

        return new LocalDataPathEnvironment(
            runtimeContext.EnvironmentName,
            runtimeContext.ContentRootPath,
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile));
    }
}
