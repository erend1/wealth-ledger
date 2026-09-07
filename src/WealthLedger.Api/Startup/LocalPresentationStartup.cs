using System.Globalization;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Presentation;

namespace WealthLedger.Api.Startup;

internal static class LocalPresentationStartup
{
    internal static LocalDataFailure? Validate(
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        try
        {
            _ = serviceProvider.GetRequiredService<PresentationCulture>();
            return null;
        }
        catch (Exception exception)
            when (exception is CultureNotFoundException
                  or TimeZoneNotFoundException
                  or InvalidTimeZoneException)
        {
            return new LocalDataFailure(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                "The local presentation culture or time zone is unavailable.");
        }
    }
}
