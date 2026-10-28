using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WealthLedger.Api.Tests;

/// <summary>
/// Business-date defaults follow the Application's operating date, the
/// host's local date, even when the display time zone is already a day
/// ahead.
/// </summary>
public sealed partial class OperatingDateUiTests
{
    // 22:30 UTC on 1 September is already 01:30 on 2 September in the
    // Europe/Istanbul display zone; a UTC host still operates on 1 September.
    private static readonly DateTimeOffset LateEveningUtc =
        new(2026, 9, 1, 22, 30, 0, TimeSpan.Zero);

    private const string HostOperatingDate = "2026-09-01";

    [Theory]
    [InlineData("/record/opening-balance", "Input.AsOfDate")]
    [InlineData("/record/contribution", "Input.ExecutionDate")]
    [InlineData("/record/fund-purchase", "Input.ExecutionDate")]
    [InlineData("/record/physical-gold-purchase", "Input.ExecutionDate")]
    public async Task RecordForms_DefaultTheHostOperatingDate(string path, string field)
    {
        using var factory = new WealthLedgerApiFactory();
        using var host = WithHostClock(factory);
        using var client = CreateClient(host);

        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HostOperatingDate, InputValue(html, field));
    }

    [Fact]
    public async Task OpeningReview_AcceptsItsDefaultDateWhileTheDisplayZoneIsTomorrow()
    {
        using var factory = new WealthLedgerApiFactory();
        using var host = WithHostClock(factory);
        using var client = CreateClient(host);

        using var page = await client.GetAsync("/record/opening-balance");
        var rawHtml = await page.Content.ReadAsStringAsync();
        var setup = factory.ReadySetup;

        using var review = await client.PostAsync(
            "/record/opening-balance?handler=Review",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.PortfolioId"] = setup.PortfolioId.ToString("D"),
                ["Input.AccountId"] = setup.AccountId.ToString("D"),
                ["Input.AssetId"] = setup.CashAssetId.ToString("D"),
                ["Input.AsOfDate"] = InputValue(rawHtml, "Input.AsOfDate"),
                ["Input.Quantity"] = "100,00",
                ["Input.ExternalReference"] = "SYNTHETIC-OPERATING-DATE",
                ["Input.Note"] = "Synthetic opening reviewed late in the evening.",
                ["Input.IdempotencyKey"] = InputValue(rawHtml, "Input.IdempotencyKey"),
                ["__RequestVerificationToken"] = InputValue(rawHtml, "__RequestVerificationToken")
            }));
        var reviewHtml = WebUtility.HtmlDecode(await review.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("id=\"opening-review-heading\"", reviewHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("bugünden ileri olamaz", reviewHtml, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> WithHostClock(WealthLedgerApiFactory factory)
        => factory.WithWebHostBuilder(
            builder => builder.ConfigureTestServices(
                services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(
                        new HostClock(LateEveningUtc, TimeZoneInfo.Utc));
                }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> host)
        => host.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

    // A page may repeat an input across its forms, such as the antiforgery
    // token; every copy must carry the same value.
    private static string InputValue(string html, string name)
    {
        var values = InputPattern().Matches(html)
            .Select(match => match.Value)
            .Where(tag => tag.Contains($"name=\"{name}\"", StringComparison.Ordinal))
            .Select(tag => ValuePattern().Match(tag))
            .Select(value => value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : null)
            .Distinct()
            .ToArray();

        return Assert.Single(values) ?? throw new InvalidOperationException($"The {name} input has no value.");
    }

    [GeneratedRegex("<input\\b[^>]*>")]
    private static partial Regex InputPattern();

    [GeneratedRegex("\\bvalue=\"([^\"]*)\"")]
    private static partial Regex ValuePattern();

    private sealed class HostClock(DateTimeOffset utcNow, TimeZoneInfo localTimeZone)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public override TimeZoneInfo LocalTimeZone => localTimeZone;
    }
}
