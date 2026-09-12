using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.OpeningBalances;

public sealed record OpeningBalanceHouseholdReference(
    Guid HouseholdId,
    CurrencyCode BaseCurrency);

public sealed record OpeningBalancePortfolioReference(
    Guid PortfolioId,
    Guid HouseholdId,
    string Code,
    string Name,
    PortfolioStatus Status);

public sealed record OpeningBalanceCurrencyReference(
    CurrencyCode Code,
    string Name,
    int MinorUnitDigits);

public sealed record OpeningBalanceInstitutionReference(
    Guid InstitutionId,
    string Code,
    string Name,
    InstitutionType Type,
    bool IsActive);

public sealed record OpeningBalanceAccountReference(
    Guid AccountId,
    Guid HouseholdId,
    Guid? InstitutionId,
    string Code,
    string Name,
    AccountType Type,
    bool IsActive,
    DateOnly? OpenedOn,
    DateOnly? ClosedOn);

public sealed record OpeningBalanceAssetReference(
    Guid AssetId,
    string Code,
    string Name,
    AssetType Type,
    AssetUnit BaseUnit,
    CurrencyCode? BaseCurrency,
    LotTrackingMode LotTrackingMode,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

public enum OpeningBalanceReferenceWriteStatus
{
    Created,
    Equivalent,
    Conflict
}

public sealed record OpeningBalanceReferenceWriteResult<T>
    where T : class
{
    private OpeningBalanceReferenceWriteResult(
        OpeningBalanceReferenceWriteStatus status,
        T? reference)
    {
        if (status == OpeningBalanceReferenceWriteStatus.Conflict
            && reference is not null)
        {
            throw new ArgumentException(
                "A conflicting reference write cannot return a reference.",
                nameof(reference));
        }

        if (status != OpeningBalanceReferenceWriteStatus.Conflict
            && reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        Status = status;
        Reference = reference;
    }

    public OpeningBalanceReferenceWriteStatus Status { get; }

    public T? Reference { get; }

    public static OpeningBalanceReferenceWriteResult<T> Created(T reference)
        => new(
            OpeningBalanceReferenceWriteStatus.Created,
            reference);

    public static OpeningBalanceReferenceWriteResult<T> Equivalent(T reference)
        => new(
            OpeningBalanceReferenceWriteStatus.Equivalent,
            reference);

    public static OpeningBalanceReferenceWriteResult<T> Conflict()
        => new(
            OpeningBalanceReferenceWriteStatus.Conflict,
            null);
}

public sealed record OpeningBalanceReferenceCreationResult<T>(
    bool WasCreated,
    T Reference)
    where T : class;

public interface IOpeningBalanceReferenceReadStore
{
    Task<OpeningBalanceHouseholdReference?> FindHouseholdAsync(
        Guid householdId,
        CancellationToken cancellationToken = default);

    Task<OpeningBalanceCurrencyReference?> FindCurrencyAsync(
        CurrencyCode code,
        CancellationToken cancellationToken = default);

    Task<OpeningBalanceInstitutionReference?> FindInstitutionAsync(
        Guid institutionId,
        CancellationToken cancellationToken = default);

    Task<OpeningBalancePortfolioReference?> FindPortfolioAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default);

    Task<OpeningBalanceAccountReference?> FindAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task<OpeningBalanceAssetReference?> FindAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken = default);
}

public interface IOpeningBalanceReferenceWriteStore
{
    Task<OpeningBalanceReferenceWriteResult<OpeningBalanceCurrencyReference>>
        TryCreateCurrencyAsync(
            OpeningBalanceCurrencyReference candidate,
            CancellationToken cancellationToken = default);

    Task<OpeningBalanceReferenceWriteResult<OpeningBalanceInstitutionReference>>
        TryCreateInstitutionAsync(
            Institution candidate,
            CancellationToken cancellationToken = default);

    Task<OpeningBalanceReferenceWriteResult<OpeningBalanceAccountReference>>
        TryCreateAccountAsync(
            Account candidate,
            CancellationToken cancellationToken = default);

    Task<OpeningBalanceReferenceWriteResult<OpeningBalanceAssetReference>>
        TryCreateAssetAsync(
            Asset candidate,
            DateTimeOffset createdAtUtc,
            CancellationToken cancellationToken = default);
}
