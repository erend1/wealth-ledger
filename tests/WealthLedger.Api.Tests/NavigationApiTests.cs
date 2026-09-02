using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WealthLedger.Api.Contracts;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Api.Tests;

public sealed class NavigationApiTests
{
    private static readonly Guid OtherHouseholdId =
        Guid.Parse("11000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Navigation_MasterRoutesExposeStableCurrentFieldsAndScope()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeAsync(client);
        await SeedNavigationMastersAsync(factory, setup);

        var householdFirst = await GetPageAsync<HouseholdNavigationResponse>(
            client,
            "/api/households?pageSize=1");
        Assert.Single(householdFirst.Items);
        Assert.NotNull(householdFirst.NextCursor);
        Assert.Equal(setup.HouseholdId, householdFirst.Items[0].HouseholdId);
        Assert.Equal("Synthetic Household", householdFirst.Items[0].Name);
        Assert.Equal("TRY", householdFirst.Items[0].BaseCurrency.Code);
        Assert.Equal("Synthetic Currency", householdFirst.Items[0].BaseCurrency.Name);
        Assert.Equal(2, householdFirst.Items[0].BaseCurrency.MinorUnitDigits);

        var householdSecond = await GetPageAsync<HouseholdNavigationResponse>(
            client,
            "/api/households?pageSize=1&cursor="
            + Uri.EscapeDataString(householdFirst.NextCursor!));
        Assert.Single(householdSecond.Items);
        Assert.Equal(OtherHouseholdId, householdSecond.Items[0].HouseholdId);
        Assert.Null(householdSecond.NextCursor);

        var household = await client.GetFromJsonAsync<HouseholdNavigationResponse>(
            $"/api/households/{setup.HouseholdId}");
        Assert.Equal(setup.HouseholdId, household!.HouseholdId);

        var membersDefault = await GetPageAsync<HouseholdMemberNavigationResponse>(
            client,
            $"/api/households/{setup.HouseholdId}/members");
        var membersAll = await GetPageAsync<HouseholdMemberNavigationResponse>(
            client,
            $"/api/households/{setup.HouseholdId}/members?includeInactive=true");
        var otherMembers = await GetPageAsync<HouseholdMemberNavigationResponse>(
            client,
            $"/api/households/{OtherHouseholdId}/members");
        Assert.Single(membersDefault.Items);
        Assert.Equal(2, membersAll.Items.Count);
        Assert.Single(otherMembers.Items);
        Assert.All(
            membersAll.Items,
            item => Assert.Equal(setup.HouseholdId, item.HouseholdId));

        var portfoliosDefault = await GetPageAsync<PortfolioNavigationResponse>(
            client,
            $"/api/households/{setup.HouseholdId}/portfolios");
        var portfoliosAll = await GetPageAsync<PortfolioNavigationResponse>(
            client,
            $"/api/households/{setup.HouseholdId}/portfolios?includeInactive=true");
        Assert.Single(portfoliosDefault.Items);
        Assert.Equal("ACTIVE", portfoliosDefault.Items[0].StatusCode);
        Assert.Equal(2, portfoliosAll.Items.Count);
        Assert.Contains(
            portfoliosAll.Items,
            item => item.StatusCode == "ARCHIVED"
                    && item.ClosedAtUtc is not null);

        var accountsDefault = await GetPageAsync<AccountNavigationResponse>(
            client,
            $"/api/households/{setup.HouseholdId}/accounts");
        var accountsAll = await GetPageAsync<AccountNavigationResponse>(
            client,
            $"/api/households/{setup.HouseholdId}/accounts?includeInactive=true");
        Assert.Equal(2, accountsDefault.Items.Count);
        Assert.Equal(3, accountsAll.Items.Count);
        var setupAccount = Assert.Single(
            accountsDefault.Items,
            item => item.AccountId == setup.AccountId);
        Assert.NotNull(setupAccount.Institution);
        Assert.Equal(setup.InstitutionId, setupAccount.Institution.InstitutionId);
        Assert.Equal("BROKER", setupAccount.Institution.TypeCode);
        Assert.Contains(
            accountsDefault.Items,
            item => item.Institution is null);

        var institutionsDefault = await GetPageAsync<InstitutionNavigationResponse>(
            client,
            "/api/institutions");
        var institutionsAll = await GetPageAsync<InstitutionNavigationResponse>(
            client,
            "/api/institutions?includeInactive=true");
        Assert.Single(institutionsDefault.Items);
        Assert.Equal("BROKER", institutionsDefault.Items[0].TypeCode);
        Assert.Equal(2, institutionsAll.Items.Count);

        var currencies = await GetPageAsync<CurrencyNavigationResponse>(
            client,
            "/api/currencies");
        Assert.Equal(["TRY", "USD"], currencies.Items.Select(x => x.Code));

        var assetsDefault = await GetPageAsync<AssetNavigationResponse>(
            client,
            "/api/assets");
        var assetsAll = await GetPageAsync<AssetNavigationResponse>(
            client,
            "/api/assets?includeInactive=true");
        Assert.Equal(2, assetsDefault.Items.Count);
        Assert.Equal(3, assetsAll.Items.Count);
        var fund = Assert.Single(
            assetsDefault.Items,
            item => item.AssetId == setup.FundAssetId);
        Assert.Equal("FUND", fund.TypeCode);
        Assert.Equal("FUND_UNIT", fund.BaseUnitCode);
        Assert.Equal("TRY", fund.BaseCurrencyCode);
        Assert.Equal("REQUIRED", fund.LotTrackingModeCode);
    }

    [Fact]
    public async Task Navigation_InvalidInputsAndUnknownScopesReturnSanitizedStableProblems()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeAsync(client);

        await AssertProblemAsync(
            client,
            "/api/assets?pageSize=abc",
            HttpStatusCode.BadRequest,
            "NAVIGATION_PAGE_SIZE_INVALID");
        await AssertProblemAsync(
            client,
            "/api/assets?pageSize=101",
            HttpStatusCode.BadRequest,
            "NAVIGATION_PAGE_SIZE_INVALID");
        await AssertProblemAsync(
            client,
            "/api/assets?includeInactive=1",
            HttpStatusCode.BadRequest,
            "NAVIGATION_FILTER_INVALID");
        await AssertProblemAsync(
            client,
            "/api/currencies?includeInactive=true",
            HttpStatusCode.BadRequest,
            "NAVIGATION_FILTER_INVALID");
        await AssertProblemAsync(
            client,
            "/api/assets?cursor=private%2Bcursor%2Fpayload",
            HttpStatusCode.BadRequest,
            "NAVIGATION_CURSOR_INVALID");
        await AssertProblemAsync(
            client,
            $"/api/households/{Guid.NewGuid()}/accounts",
            HttpStatusCode.NotFound,
            "HOUSEHOLD_NOT_FOUND");
        await AssertProblemAsync(
            client,
            $"/api/households/{Guid.NewGuid()}",
            HttpStatusCode.NotFound,
            "HOUSEHOLD_NOT_FOUND");
        await AssertProblemAsync(
            client,
            $"/api/households/{Guid.Empty}",
            HttpStatusCode.NotFound,
            "HOUSEHOLD_NOT_FOUND");
        await AssertProblemAsync(
            client,
            PositionUrl(setup, Guid.Empty),
            HttpStatusCode.NotFound,
            "POSITION_SCOPE_NOT_FOUND");

        var institutionPage = await GetPageAsync<InstitutionNavigationResponse>(
            client,
            "/api/institutions?pageSize=1&includeInactive=false");
        Assert.Null(institutionPage.NextCursor);

        await SeedNavigationMastersAsync(factory, setup);
        institutionPage = await GetPageAsync<InstitutionNavigationResponse>(
            client,
            "/api/institutions?pageSize=1&includeInactive=true");
        Assert.NotNull(institutionPage.NextCursor);
        await AssertProblemAsync(
            client,
            "/api/assets?includeInactive=true&cursor="
            + Uri.EscapeDataString(institutionPage.NextCursor!),
            HttpStatusCode.BadRequest,
            "NAVIGATION_CURSOR_SCOPE_MISMATCH");
    }

    [Fact]
    public async Task Navigation_RecentLedgerMatchesDetailOmitsExpandedFactsAndPositionsValidateScope()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeAsync(client);
        var contributionResponse = await SendWithIdempotencyAsync(
            client,
            HttpMethod.Post,
            "/api/ledger/contributions",
            new RecordContributionRequest(
                setup.HouseholdId,
                setup.PortfolioId,
                setup.AccountId,
                setup.CashAssetId,
                AmountMinorUnits: 100_000,
                CurrencyCode: "TRY",
                CashFlowCategoryCode: "OTHER",
                ApiTestData.ExecutionDate,
                setup.HouseholdMemberId,
                ExternalReference: "SYNTHETIC-NAV-REFERENCE",
                Note: "Private synthetic note omitted from navigation."));
        Assert.Equal(HttpStatusCode.Created, contributionResponse.StatusCode);
        var contribution = await contributionResponse.Content
            .ReadFromJsonAsync<RecordContributionResponse>();
        var reversalResponse = await SendWithIdempotencyAsync(
            client,
            HttpMethod.Post,
            $"/api/ledger/transactions/{contribution!.TransactionId}/reversals",
            new ReversePostedTransactionRequest(
                "Synthetic navigation reversal."));
        Assert.Equal(HttpStatusCode.Created, reversalResponse.StatusCode);
        var reversal = await reversalResponse.Content
            .ReadFromJsonAsync<ReversePostedTransactionResponse>();

        var response = await client.GetAsync(
            $"/api/households/{setup.HouseholdId}/ledger/transactions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<
            NavigationPageResponse<RecentLedgerTransactionNavigationResponse>>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(reversal!.ReversalTransactionId, page.Items[0].TransactionId);
        Assert.Equal(contribution.TransactionId, page.Items[1].TransactionId);
        Assert.Equal(
            ApiTestData.ExecutionDate,
            page.Items[0].ExecutionDate);
        Assert.Equal(contribution.TransactionId, page.Items[0].ReversalOfTransactionId);
        Assert.Equal(reversal.ReversalTransactionId, page.Items[1].ReversedByTransactionId);
        Assert.Equal("SYNTHETIC-NAV-REFERENCE", page.Items[1].ExternalReference);
        Assert.DoesNotContain("\"note\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cashFlow", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("costs", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdLots", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lotAllocations", json, StringComparison.OrdinalIgnoreCase);

        foreach (var summary in page.Items)
        {
            var detail = await client.GetFromJsonAsync<LedgerTransactionResponse>(
                $"/api/ledger/transactions/{summary.TransactionId}");
            Assert.NotNull(detail);
            Assert.Equal(
                detail.Entries.Select(x => x.EntryId),
                summary.EntryEffects.Select(x => x.EntryId));
            Assert.Equal(
                detail.Entries.Select(x => x.EntrySequence),
                summary.EntryEffects.Select(x => x.EntrySequence));
            Assert.Equal(
                detail.Entries.Select(x => x.QuantityDeltaRawE8),
                summary.EntryEffects.Select(x => x.QuantityDeltaRawE8));
        }

        var originalEffect = Assert.Single(page.Items[1].EntryEffects);
        Assert.Equal(setup.PortfolioId, originalEffect.PortfolioId);
        Assert.Equal("CORE", originalEffect.PortfolioCode);
        Assert.Equal("ACTIVE", originalEffect.PortfolioStatusCode);
        Assert.Equal(setup.AccountId, originalEffect.AccountId);
        Assert.Equal("INVESTMENT", originalEffect.AccountTypeCode);
        Assert.Equal(setup.InstitutionId, originalEffect.InstitutionId);
        Assert.Equal("BROKER", originalEffect.InstitutionTypeCode);
        Assert.Equal(setup.CashAssetId, originalEffect.AssetId);
        Assert.Equal("CASH", originalEffect.AssetTypeCode);
        Assert.Equal("CURRENCY_UNIT", originalEffect.AssetBaseUnitCode);
        Assert.Equal("NONE", originalEffect.AssetLotTrackingModeCode);
        Assert.Equal("PRINCIPAL", originalEffect.RoleCode);

        var validZero = await client.GetAsync(
            PositionUrl(setup, setup.CashAssetId));
        Assert.Equal(HttpStatusCode.OK, validZero.StatusCode);
        var position = await validZero.Content.ReadFromJsonAsync<PositionResponse>();
        Assert.Equal(0, position!.QuantityRawE8);
        Assert.Equal(2, position.SourceEntryCount);

        await AssertProblemAsync(
            client,
            PositionUrl(setup, Guid.NewGuid()),
            HttpStatusCode.NotFound,
            "POSITION_SCOPE_NOT_FOUND");
    }

    [Fact]
    public async Task Navigation_LogsOnlyBoundedOperationalMetadata()
    {
        using var factory = new WealthLedgerApiFactory();
        var provider = new RecordingLoggerProvider();
        using var loggedFactory = factory.WithWebHostBuilder(
            builder => builder.ConfigureLogging(
                logging => logging.AddProvider(provider)));
        using var client = loggedFactory.CreateClient();
        var setup = await InitializeAsync(client);
        var contributionResponse = await SendWithIdempotencyAsync(
            client,
            HttpMethod.Post,
            "/api/ledger/contributions",
            new RecordContributionRequest(
                setup.HouseholdId,
                setup.PortfolioId,
                setup.AccountId,
                setup.CashAssetId,
                AmountMinorUnits: 654_321,
                CurrencyCode: "TRY",
                CashFlowCategoryCode: "OTHER",
                ApiTestData.ExecutionDate,
                setup.HouseholdMemberId,
                ExternalReference: "PRIVATE-NAVIGATION-REFERENCE",
                Note: "Private navigation note."));
        Assert.Equal(HttpStatusCode.Created, contributionResponse.StatusCode);
        provider.Clear();
        var unknownHouseholdId = Guid.NewGuid();

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/households")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync(
                $"/api/households/{setup.HouseholdId}/ledger/transactions"))
                .StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync(
                "/api/assets?cursor=PRIVATE-CURSOR-PAYLOAD"))
                .StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(
                $"/api/households/{unknownHouseholdId}/accounts"))
                .StatusCode);

        var logs = provider.Render();
        Assert.Contains(
            "route /api/households with outcome SUCCESS, item count 1",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "route /api/households/{householdId}/ledger/transactions with outcome SUCCESS, item count 1",
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            setup.HouseholdId.ToString("D"),
            logs,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            unknownHouseholdId.ToString("D"),
            logs,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "PRIVATE-CURSOR-PAYLOAD",
            logs,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "PRIVATE-NAVIGATION-REFERENCE",
            logs,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Private navigation note",
            logs,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("654321", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "EntityFrameworkCore",
            logs,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            " at WealthLedger",
            logs,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Navigation_DoesNotExposeMasterWritesOrBroadSearchRoutes()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var setup = await InitializeAsync(client);
        var countsBeforeReads = await ReadDatabaseCountsAsync(factory);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/households")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync(
                $"/api/households/{setup.HouseholdId}/members"))
                .StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync(
                $"/api/households/{setup.HouseholdId}/portfolios"))
                .StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync(
                $"/api/households/{setup.HouseholdId}/accounts"))
                .StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/institutions")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/currencies")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/assets")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync(
                $"/api/households/{setup.HouseholdId}/ledger/transactions"))
                .StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync(
                PositionUrl(setup, setup.CashAssetId)))
                .StatusCode);

        var masterWrite = await client.PostAsJsonAsync(
            "/api/assets",
            new { code = "OUT_OF_SCOPE" });
        var broadSearch = await client.GetAsync(
            "/api/ledger/transactions?query=OUT_OF_SCOPE");
        var operations = await client.GetAsync("/api/operations/status");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, masterWrite.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, broadSearch.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, operations.StatusCode);
        Assert.Equal(countsBeforeReads, await ReadDatabaseCountsAsync(factory));
    }

    private static async Task<InitializeCoreLedgerResponse> InitializeAsync(
        HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/setup/core-ledger",
            ApiTestData.CreateSetupRequest());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<InitializeCoreLedgerResponse>(
            await response.Content.ReadFromJsonAsync<InitializeCoreLedgerResponse>());
    }

    private static async Task SeedNavigationMastersAsync(
        WealthLedgerApiFactory factory,
        InitializeCoreLedgerResponse setup)
    {
        await using var context = CreateContext(factory);
        var createdAtUtc = DateTime.UtcNow.AddDays(1);
        context.Currencies.Add(
            new CurrencyRow
            {
                Code = "USD",
                Name = "Synthetic Currency Two",
                MinorUnitDigits = 2
            });
        context.Households.Add(
            new HouseholdRow
            {
                Id = OtherHouseholdId,
                Name = "Other Synthetic Household",
                BaseCurrencyCode = "TRY",
                CreatedAtUtc = createdAtUtc
            });
        context.HouseholdMembers.AddRange(
            new HouseholdMemberRow
            {
                Id = Guid.Parse("21000000-0000-0000-0000-000000000001"),
                HouseholdId = setup.HouseholdId,
                DisplayName = "Inactive Synthetic Member",
                IsActive = false,
                CreatedAtUtc = createdAtUtc
            },
            new HouseholdMemberRow
            {
                Id = Guid.Parse("21000000-0000-0000-0000-000000000002"),
                HouseholdId = OtherHouseholdId,
                DisplayName = "Other Synthetic Member",
                IsActive = true,
                CreatedAtUtc = createdAtUtc
            });
        context.Institutions.Add(
            new InstitutionRow
            {
                Id = Guid.Parse("31000000-0000-0000-0000-000000000001"),
                Code = "INACTIVE_BANK",
                Name = "Inactive Synthetic Bank",
                Type = InstitutionType.Bank,
                IsActive = false
            });
        context.Portfolios.Add(
            new PortfolioRow
            {
                Id = Guid.Parse("41000000-0000-0000-0000-000000000001"),
                HouseholdId = setup.HouseholdId,
                Code = "ARCHIVED_GOAL",
                Name = "Archived Synthetic Goal",
                Status = PortfolioStatus.Archived,
                CreatedAtUtc = createdAtUtc,
                ClosedAtUtc = createdAtUtc.AddMinutes(1)
            });
        context.Accounts.AddRange(
            new AccountRow
            {
                Id = Guid.Parse("51000000-0000-0000-0000-000000000001"),
                HouseholdId = setup.HouseholdId,
                InstitutionId = null,
                Code = "NO_INSTITUTION",
                Name = "Synthetic Direct Account",
                Type = AccountType.Cash,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = Guid.Parse("51000000-0000-0000-0000-000000000002"),
                HouseholdId = setup.HouseholdId,
                InstitutionId = null,
                Code = "INACTIVE_ACCOUNT",
                Name = "Inactive Synthetic Account",
                Type = AccountType.Cash,
                IsActive = false,
                OpenedOn = new DateOnly(2026, 1, 1),
                ClosedOn = new DateOnly(2026, 8, 1)
            });
        context.Assets.Add(
            new AssetRow
            {
                Id = Guid.Parse("61000000-0000-0000-0000-000000000001"),
                Code = "INACTIVE_ASSET",
                Name = "Inactive Synthetic Asset",
                Type = AssetType.Equity,
                BaseUnit = AssetUnit.Share,
                BaseCurrencyCode = "TRY",
                LotTrackingMode = LotTrackingMode.Optional,
                IsActive = false,
                CreatedAtUtc = createdAtUtc
            });
        await context.SaveChangesAsync();
    }

    private static WealthLedgerDbContext CreateContext(
        WealthLedgerApiFactory factory)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = factory.DatabasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new WealthLedgerDbContext(options);
    }

    private static async Task<int[]> ReadDatabaseCountsAsync(
        WealthLedgerApiFactory factory)
    {
        await using var context = CreateContext(factory);
        return
        [
            await context.Currencies.CountAsync(),
            await context.Households.CountAsync(),
            await context.HouseholdMembers.CountAsync(),
            await context.Institutions.CountAsync(),
            await context.Portfolios.CountAsync(),
            await context.Accounts.CountAsync(),
            await context.Assets.CountAsync(),
            await context.LedgerTransactions.CountAsync(),
            await context.TransactionEntries.CountAsync(),
            await context.CashFlowDetails.CountAsync(),
            await context.TransactionCostComponents.CountAsync(),
            await context.AssetLots.CountAsync(),
            await context.LotEntryAllocations.CountAsync(),
            await context.PhysicalGoldLotDetails.CountAsync(),
            await context.CommandReceipts.CountAsync()
        ];
    }

    private static async Task<NavigationPageResponse<T>> GetPageAsync<T>(
        HttpClient client,
        string route)
    {
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<NavigationPageResponse<T>>(
            await response.Content.ReadFromJsonAsync<NavigationPageResponse<T>>());
    }

    private static async Task AssertProblemAsync(
        HttpClient client,
        string route,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        var response = await client.GetAsync(route);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(expectedStatus, response.StatusCode);
        var problem = JsonSerializer.Deserialize<ProblemDetails>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(problem);
        Assert.Equal(expectedCode, GetStringExtension(problem, "code"));
        Assert.DoesNotContain("private+cursor/payload", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EntityFramework", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStringExtension(
        ProblemDetails problem,
        string key)
    {
        Assert.True(problem.Extensions.TryGetValue(key, out var value));
        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element =>
                element.GetString()!,
            _ => throw new InvalidOperationException(
                $"Problem extension '{key}' was not a string.")
        };
    }

    private static async Task<HttpResponseMessage> SendWithIdempotencyAsync<T>(
        HttpClient client,
        HttpMethod method,
        string route,
        T body)
    {
        using var request = new HttpRequestMessage(method, route)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", $"navigation-{Guid.NewGuid():N}");
        return await client.SendAsync(request);
    }

    private static string PositionUrl(
        InitializeCoreLedgerResponse setup,
        Guid assetId)
        => $"/api/households/{setup.HouseholdId}/portfolios/"
           + $"{setup.PortfolioId}/accounts/{setup.AccountId}/positions/{assetId}";

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = [];

        public ILogger CreateLogger(string categoryName)
            => new RecordingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        internal void Clear()
        {
            while (_entries.TryDequeue(out _))
            {
            }
        }

        internal string Render() => string.Join(Environment.NewLine, _entries);

        private sealed class RecordingLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly ConcurrentQueue<string> _entries;

            internal RecordingLogger(
                string categoryName,
                ConcurrentQueue<string> entries)
            {
                _categoryName = categoryName;
                _entries = entries;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var message = formatter(state, exception);
                var exceptionText = exception is null
                    ? string.Empty
                    : $"{Environment.NewLine}{exception}";
                _entries.Enqueue(
                    $"{logLevel}|{_categoryName}|{message}{exceptionText}");
            }
        }
    }
}
