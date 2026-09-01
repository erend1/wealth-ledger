using System.Net;

namespace WealthLedger.Api.Tests.LocalData;

public sealed class LocalHostingNoOperationsRouteTests
{
    public static TheoryData<HttpMethod, string> UnsupportedOperationsRoutes
        => new()
        {
            { HttpMethod.Get, "/api/backup" },
            { HttpMethod.Post, "/api/backup" },
            { HttpMethod.Post, "/api/restore" },
            { HttpMethod.Post, "/api/database/migrate" },
            { HttpMethod.Get, "/api/files" },
            { HttpMethod.Post, "/api/sql" }
        };

    [Theory]
    [MemberData(nameof(UnsupportedOperationsRoutes))]
    public async Task LocalHosting_OperationsAndSqlRoutesDoNotExist(
        HttpMethod method,
        string path)
    {
        using var factory = new WealthLedgerApiFactory(setupEnabled: false);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, path);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
