using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using WealthLedger.Api.Startup;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Presentation;

namespace WealthLedger.Api.Tests.LocalData;

public sealed class LocalPresentationStartupTests
{
    [Fact]
    public void Validate_AvailablePresentationCultureSucceeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton(
            new PresentationCulture(
                CultureInfo.GetCultureInfo("tr-TR"),
                TimeZoneInfo.Utc));
        using var provider = services.BuildServiceProvider();

        var failure = LocalPresentationStartup.Validate(provider);

        Assert.Null(failure);
    }

    [Fact]
    public void Validate_MissingTimeZoneReturnsSanitizedStartupFailure()
    {
        const string privateDiagnostic =
            "PRIVATE_TIME_ZONE_DIAGNOSTIC";
        var services = new ServiceCollection();
        services.AddSingleton<PresentationCulture>(
            _ => throw new TimeZoneNotFoundException(
                privateDiagnostic));
        using var provider = services.BuildServiceProvider();

        var failure = LocalPresentationStartup.Validate(provider);

        Assert.NotNull(failure);
        Assert.Equal(
            LocalDataFailureCategory.InvalidInputOrConfiguration,
            failure.Category);
        Assert.Equal(
            "The local presentation culture or time zone is unavailable.",
            failure.Message);
        Assert.DoesNotContain(privateDiagnostic, failure.Message);
        Assert.DoesNotContain(
            nameof(TimeZoneNotFoundException),
            failure.Message);
    }
}
