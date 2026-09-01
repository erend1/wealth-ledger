using WealthLedger.Domain.Ledger;

namespace WealthLedger.Domain.Tests.Architecture;

public sealed class LocalDataBoundaryTests
{
    [Fact]
    public void Domain_DoesNotReferenceOperationsOrInfrastructureTechnologies()
    {
        var referencedAssemblies = typeof(LedgerTransaction)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
        var forbiddenPrefixes = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Microsoft.Data.Sqlite",
            "Microsoft.AspNetCore",
            "System.IO.Compression",
            "System.Net.Http",
            "WealthLedger.Infrastructure",
            "WealthLedger.Operations"
        };

        Assert.DoesNotContain(
            referencedAssemblies,
            reference => forbiddenPrefixes.Any(prefix =>
                reference.StartsWith(prefix, StringComparison.Ordinal)));
    }
}
