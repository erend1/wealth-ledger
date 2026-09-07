using WealthLedger.Application.LocalData;
using WealthLedger.Domain.Portfolios;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Pages.Setup;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Tests.Setup;

public sealed class WorkspaceSetupFormMapperTests
{
    [Fact]
    public void Map_ValidHumanInputPreservesEveryReviewedSetupValue()
    {
        var form = CreateValidForm();
        form.BaseCurrencyCode = " try ";
        form.InstitutionCode = " synthetic_institution ";
        form.HouseholdMemberDisplayName = "  ";

        var result = WorkspaceSetupFormMapper.Map(form);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Command);

        var command = result.Command!;

        Assert.Equal("TRY", command.BaseCurrency.Code.Value);
        Assert.Equal("Synthetic Currency", command.BaseCurrency.Name);
        Assert.Equal(2, command.BaseCurrency.MinorUnitDigits);
        Assert.Equal("Synthetic Household", command.HouseholdName);
        Assert.Null(command.HouseholdMemberDisplayName);
        Assert.Equal(
            "SYNTHETIC_INSTITUTION",
            command.Institution.Code);
        Assert.Equal(
            InstitutionType.Broker,
            command.Institution.Type);
        Assert.Equal("CORE", command.Portfolio.Code);
        Assert.Equal("PRIMARY", command.Account.Code);
        Assert.Equal(AccountType.Investment, command.Account.Type);
        Assert.Equal(
            new DateOnly(2026, 1, 1),
            command.Account.OpenedOn);
        Assert.Equal("SYNTHETIC_CASH", command.CashAsset.Code);
        Assert.Equal("SYNTHETIC_FUND", command.FundAsset.Code);
    }

    [Fact]
    public void Map_InvalidCodesAndDateReturnStableSafeFieldErrors()
    {
        var form = CreateValidForm();
        form.BaseCurrencyCode = "TR1";
        form.InstitutionTypeCode = "NOT_A_TYPE";
        form.AccountTypeCode = "NOT_A_TYPE";
        form.AccountOpenedOn = "not-a-date";
        form.FundAssetCode = "bad code";

        var result = WorkspaceSetupFormMapper.Map(form);

        Assert.False(result.Succeeded);
        Assert.Null(result.Command);
        Assert.Contains(
            result.Errors,
            error => error.FieldName
                     == nameof(form.BaseCurrencyCode));
        Assert.Contains(
            result.Errors,
            error => error.FieldName
                     == nameof(form.InstitutionTypeCode));
        Assert.Contains(
            result.Errors,
            error => error.FieldName
                     == nameof(form.AccountTypeCode));
        Assert.Contains(
            result.Errors,
            error => error.FieldName
                     == nameof(form.AccountOpenedOn));
        Assert.Contains(
            result.Errors,
            error => error.FieldName
                     == nameof(form.FundAssetCode));

        var text = new UiText(
            PresentationCulture.CreateDefault());

        Assert.All(
            result.Errors,
            error => Assert.False(
                string.IsNullOrWhiteSpace(text[error.ResourceKey])));
    }

    [Fact]
    public void FormContractContainsNoInfrastructureOrRawIdentityInputs()
    {
        var propertyNames = typeof(WorkspaceSetupForm)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(
            propertyNames,
            name => name.EndsWith("Id", StringComparison.Ordinal));
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains("Guid", StringComparison.Ordinal));
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains("Raw", StringComparison.Ordinal));
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains("MinorUnits", StringComparison.Ordinal));
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains("Connection", StringComparison.Ordinal));
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains("Migration", StringComparison.Ordinal));
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains("Path", StringComparison.Ordinal));
        Assert.DoesNotContain(
            propertyNames,
            name => name.Contains("Sql", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StartupContextIsInitializedExactlyOnce()
    {
        var context = new LocalUiStartupContext();
        var selection = new LocalStartupSelection(
            LocalStartupMode.WorkspaceUninitialized,
            Status: null,
            Failure: null,
            WorkspaceState: null);

        context.Initialize(selection);

        Assert.Same(selection, context.Selection);
        Assert.Equal(
            LocalStartupMode.WorkspaceUninitialized,
            context.Mode);
        Assert.Throws<InvalidOperationException>(
            () => context.Initialize(selection));
    }

    [Fact]
    public void UiAssemblyDoesNotReferenceForbiddenHostOrPersistenceLayers()
    {
        var referencedAssemblyNames = typeof(LocalUiStartupContext)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            "WealthLedger.Infrastructure",
            referencedAssemblyNames);
        Assert.DoesNotContain(
            "WealthLedger.Api",
            referencedAssemblyNames);
        Assert.DoesNotContain(
            "Microsoft.Data.Sqlite",
            referencedAssemblyNames);
        Assert.DoesNotContain(
            referencedAssemblyNames,
            name => name.StartsWith(
                "Microsoft.EntityFrameworkCore",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            "System.Net.Http",
            referencedAssemblyNames);
    }

    private static WorkspaceSetupForm CreateValidForm()
        => new()
        {
            BaseCurrencyCode = "TRY",
            BaseCurrencyName = "Synthetic Currency",
            MinorUnitDigits = "2",
            HouseholdName = "Synthetic Household",
            HouseholdMemberDisplayName = "Synthetic Member",
            InstitutionCode = "SYNTHETIC_INSTITUTION",
            InstitutionName = "Synthetic Institution",
            InstitutionTypeCode = "BROKER",
            PortfolioCode = "CORE",
            PortfolioName = "Synthetic Portfolio",
            AccountCode = "PRIMARY",
            AccountName = "Synthetic Account",
            AccountTypeCode = "INVESTMENT",
            AccountOpenedOn = "2026-01-01",
            CashAssetCode = "SYNTHETIC_CASH",
            CashAssetName = "Synthetic Cash",
            FundAssetCode = "SYNTHETIC_FUND",
            FundAssetName = "Synthetic Fund"
        };
}
