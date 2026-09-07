using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI;

public static class DependencyInjection
{
    public static IMvcBuilder AddWealthLedgerUi(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(
            _ => PresentationCulture.CreateDefault());
        services.AddSingleton<ValuePresenter>();
        services.AddSingleton<UiText>();
        services.AddSingleton<LocalUiStartupContext>();

        services.AddAntiforgery(
            options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy =
                    CookieSecurePolicy.SameAsRequest;
            });

        services.Configure<RequestLocalizationOptions>(
            options =>
            {
                var culture = new CultureInfo(
                    PresentationCulture.DefaultCultureName);

                options.DefaultRequestCulture =
                    new RequestCulture(culture);
                options.SupportedCultures = [culture];
                options.SupportedUICultures = [culture];
            });

        return services
            .AddRazorPages(
                options => options.Conventions.Add(
                    new LocalStartupPageConvention()))
            .AddApplicationPart(
                typeof(DependencyInjection).Assembly);
    }

    public static IApplicationBuilder UseWealthLedgerUi(
        this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);

        var localization = application.ApplicationServices
            .GetRequiredService<IOptions<RequestLocalizationOptions>>()
            .Value;

        application.UseRequestLocalization(localization);
        application.UseStaticFiles(
            new StaticFileOptions
            {
                OnPrepareResponse = context =>
                    LocalUiSecurityHeaders.Apply(
                        context.Context.Response.Headers)
            });
        application.UseRouting();
        application.UseMiddleware<LocalStartupPageAccessMiddleware>();

        return application;
    }
}
