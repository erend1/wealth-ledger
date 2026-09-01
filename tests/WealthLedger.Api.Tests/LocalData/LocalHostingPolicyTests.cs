using Microsoft.Extensions.Configuration;
using WealthLedger.Api.Startup;

namespace WealthLedger.Api.Tests.LocalData;

public sealed class LocalHostingPolicyTests
{
    [Theory]
    [InlineData("http://localhost:0")]
    [InlineData("https://localhost:5443")]
    [InlineData("http://127.0.0.1:0")]
    [InlineData("http://[::1]:0")]
    [InlineData("http://127.0.0.1:0;http://[::1]:0")]
    public void LocalHosting_LoopbackUrlsAreAccepted(string urls)
    {
        var configuration = Configuration(
            urls,
            "localhost;127.0.0.1;[::1]");

        var failure = LocalHostingPolicy.Validate(configuration);

        Assert.Null(failure);
    }

    [Theory]
    [InlineData("http://*:5000")]
    [InlineData("http://+:5000")]
    [InlineData("http://0.0.0.0:5000")]
    [InlineData("http://[::]:5000")]
    [InlineData("http://192.168.1.25:5000")]
    [InlineData("http://host.docker.internal:5000")]
    [InlineData("unix:/tmp/wealthledger.sock")]
    [InlineData("http://127.0.0.1:0;http://0.0.0.0:5000")]
    public void LocalHosting_NonLoopbackUrlsFailClosed(string urls)
    {
        var configuration = Configuration(
            urls,
            "localhost;127.0.0.1;[::1]");

        var failure = LocalHostingPolicy.Validate(configuration);

        Assert.NotNull(failure);
        Assert.DoesNotContain(urls, failure.Message);
    }

    [Fact]
    public void LocalHosting_PortOnlyConfigurationIsRejected()
    {
        var values = BaseValues(
            "http://127.0.0.1:0",
            "localhost;127.0.0.1;[::1]");
        values["HTTP_PORTS"] = "8080";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var failure = LocalHostingPolicy.Validate(configuration);

        Assert.NotNull(failure);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("localhost;example.test")]
    [InlineData("")]
    public void LocalHosting_AllowedHostsMustAlsoBeLoopbackOnly(
        string allowedHosts)
    {
        var configuration = Configuration(
            "http://127.0.0.1:0",
            allowedHosts);

        var failure = LocalHostingPolicy.Validate(configuration);

        Assert.NotNull(failure);
    }

    private static IConfiguration Configuration(
        string urls,
        string allowedHosts)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(BaseValues(urls, allowedHosts))
            .Build();

    private static Dictionary<string, string?> BaseValues(
        string urls,
        string allowedHosts)
        => new()
        {
            ["urls"] = urls,
            ["AllowedHosts"] = allowedHosts
        };
}
