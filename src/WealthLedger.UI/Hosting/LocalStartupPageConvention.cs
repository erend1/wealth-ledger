using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace WealthLedger.UI.Hosting;

internal sealed class LocalStartupPageConvention
    : IPageApplicationModelConvention
{
    private static readonly LocalStartupPageAttribute DenyAll =
        new(
            supportsPost: false);

    public void Apply(PageApplicationModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var access = model.HandlerTypeAttributes
                         .OfType<LocalStartupPageAttribute>()
                         .SingleOrDefault()
                     ?? DenyAll;

        model.EndpointMetadata.Add(access);
    }
}
