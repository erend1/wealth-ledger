using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages;

[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class IndexModel : PageModel
{
    private const int RecentPreviewSize = 5;
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly GetLocalDataStatusUseCase _getStatus;
    private readonly ListRecentLedgerTransactionsUseCase _listRecent;
    private readonly RecentLedgerPresenter _recentPresenter;
    private readonly ValuePresenter _values;
    private readonly TimeProvider _timeProvider;

    public IndexModel(
        ReadyHouseholdResolver householdResolver,
        GetLocalDataStatusUseCase getStatus,
        ListRecentLedgerTransactionsUseCase listRecent,
        RecentLedgerPresenter recentPresenter,
        ValuePresenter values,
        TimeProvider timeProvider)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _getStatus = getStatus
            ?? throw new ArgumentNullException(nameof(getStatus));
        _listRecent = listRecent
            ?? throw new ArgumentNullException(nameof(listRecent));
        _recentPresenter = recentPresenter
            ?? throw new ArgumentNullException(nameof(recentPresenter));
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public string HouseholdName { get; private set; } = string.Empty;

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public bool HasHousehold => HouseholdStateResourceKey.Length == 0;

    public bool SafetyAvailable { get; private set; }

    public DisplayValue? Compatibility { get; private set; }

    public DisplayValue? Integrity { get; private set; }

    public DisplayValue? BackupBinding { get; private set; }

    public DisplayValue? BackupVerifiedAt { get; private set; }

    public DisplayValue? BackupAge { get; private set; }

    public IReadOnlyList<RecentLedgerTransactionDisplay> RecentTransactions
    {
        get;
        private set;
    } = [];

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        var householdResolution = await _householdResolver.ResolveAsync(
            cancellationToken);

        if (householdResolution.State != ReadyHouseholdResolutionState.Single)
        {
            HouseholdStateResourceKey = householdResolution.State
                == ReadyHouseholdResolutionState.None
                    ? "Ready_Household_None"
                    : "Ready_Household_Multiple";
            return Page();
        }

        var household = householdResolution.Household!;
        HouseholdName = household.Name;

        var statusResult = await _getStatus.ExecuteAsync(cancellationToken);
        var status = statusResult.Value;

        if (statusResult.Succeeded
            && status?.LatestVerifiedBackup is { } backup)
        {
            SafetyAvailable = true;
            Compatibility = _values.StableCode(status.Compatibility);
            Integrity = _values.StableCode(status.IntegrityStatus);
            BackupBinding = _values.StableCode(backup.WorkspaceBinding);
            BackupVerifiedAt = _values.UtcTimestamp(backup.VerifiedAtUtc);
            BackupAge = _values.ElapsedAge(
                backup.VerifiedAtUtc,
                _timeProvider.GetUtcNow());
        }

        try
        {
            var recent = await _listRecent.ExecuteAsync(
                new ListRecentLedgerTransactionsQuery(
                    household.HouseholdId,
                    PageSize: RecentPreviewSize),
                cancellationToken);
            RecentTransactions = recent.Items
                .Select(_recentPresenter.Present)
                .ToArray();
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException)
        {
            HouseholdStateResourceKey = "Ready_Household_Unavailable";
            RecentTransactions = [];
        }

        return Page();
    }
}
