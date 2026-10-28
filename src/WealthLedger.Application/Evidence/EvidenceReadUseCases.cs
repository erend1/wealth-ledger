using WealthLedger.Domain.Evidence;

namespace WealthLedger.Application.Evidence;

/// <summary>
/// Lists a household's evidence metadata, newest effective date first, in
/// bounded pages.
/// </summary>
/// <remarks>
/// Superseded records stay in the list with their successor identified.
/// Attachment bytes are never read.
/// </remarks>
public sealed class ListEvidenceUseCase
{
    private readonly IEvidenceReadStore _readStore;

    public ListEvidenceUseCase(IEvidenceReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<EvidencePage> ExecuteAsync(
        ListEvidenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.HouseholdId == Guid.Empty)
        {
            throw new ArgumentException(
                "HouseholdId cannot be empty.",
                nameof(query));
        }

        if (query.PageSize is < 1 or > 100)
        {
            throw EvidenceException.Invalid(
                EvidenceErrorCodes.PageSizeInvalid,
                "Page size must be an integer between 1 and 100.");
        }

        var after = EvidenceCursorCodec.Decode(
            query.Cursor,
            query.HouseholdId);

        if (!await _readStore.HouseholdExistsAsync(
                query.HouseholdId,
                cancellationToken))
        {
            throw EvidenceException.Missing(
                EvidenceErrorCodes.HouseholdNotFound,
                "The evidence household does not exist.");
        }

        var rows = await _readStore.ListAsync(
            query.HouseholdId,
            checked(query.PageSize + 1),
            after,
            cancellationToken);

        if (rows.Count <= query.PageSize)
        {
            return new EvidencePage(rows, NextCursor: null);
        }

        var page = rows.Take(query.PageSize).ToArray();
        var last = page[^1];
        var nextCursor = EvidenceCursorCodec.Encode(
            query.HouseholdId,
            new EvidenceCursorKey(
                last.EffectiveDate,
                last.CreatedAtUtc,
                last.EvidenceId));

        return new EvidencePage(page, nextCursor);
    }
}

/// <summary>
/// Reads one evidence record's complete metadata and transaction links.
/// </summary>
public sealed class GetEvidenceUseCase
{
    private readonly IEvidenceReadStore _readStore;

    public GetEvidenceUseCase(IEvidenceReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<EvidenceDetail> ExecuteAsync(
        GetEvidenceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        EvidenceReadValidation.EnsureIdentities(query.HouseholdId, query.EvidenceId);

        var detail = await _readStore.GetAsync(
            query.HouseholdId,
            query.EvidenceId,
            cancellationToken);

        if (detail is null
            || detail.EvidenceId != query.EvidenceId
            || detail.HouseholdId != query.HouseholdId)
        {
            throw EvidenceException.Missing(
                EvidenceErrorCodes.NotFound,
                "The evidence record does not exist in this household.");
        }

        return detail;
    }
}

/// <summary>
/// Reads retained attachment bytes for an explicit download, after proving
/// that they still match their stored length and SHA-256.
/// </summary>
/// <remarks>
/// Content that no longer matches its digest, its declared type or its
/// sanitized name is never returned: the read fails closed with an integrity
/// error instead.
/// </remarks>
public sealed class GetEvidenceAttachmentContentUseCase
{
    private readonly IEvidenceReadStore _readStore;

    public GetEvidenceAttachmentContentUseCase(IEvidenceReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<EvidenceAttachmentContent> ExecuteAsync(
        GetEvidenceAttachmentContentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        EvidenceReadValidation.EnsureIdentities(query.HouseholdId, query.EvidenceId);

        var stored = await _readStore.ReadAttachmentAsync(
            query.HouseholdId,
            query.EvidenceId,
            cancellationToken);

        if (stored is null || stored.EvidenceId != query.EvidenceId)
        {
            throw EvidenceException.Missing(
                EvidenceErrorCodes.AttachmentNotFound,
                "The evidence record has no retained attachment in this household.");
        }

        // Bytes that bypassed the Application, for example rows written by
        // direct SQL, must still match their digest and their declared type.
        if (stored.ByteLength != stored.Content.LongLength
            || !string.Equals(
                EvidenceAttachment.ComputeSha256(stored.Content),
                stored.Sha256,
                StringComparison.Ordinal)
            || !Enum.IsDefined(stored.MediaType)
            || EvidenceAttachmentPolicy.Inspect(
                stored.FileName,
                stored.MediaType,
                stored.Content,
                out var sanitizedFileName) is not null
            || !string.Equals(sanitizedFileName, stored.FileName, StringComparison.Ordinal))
        {
            throw new EvidenceException(
                EvidenceErrorCategory.Integrity,
                EvidenceErrorCodes.AttachmentIntegrityFailed,
                "The retained evidence attachment no longer matches its recorded digest.");
        }

        return new EvidenceAttachmentContent(
            stored.EvidenceId,
            stored.FileName,
            stored.MediaType,
            stored.Sha256,
            stored.Content);
    }
}

/// <summary>
/// Reads one evidence record's observations with current scope labels.
/// </summary>
public sealed class ListEvidenceObservationsUseCase
{
    private readonly IEvidenceReadStore _readStore;

    public ListEvidenceObservationsUseCase(IEvidenceReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<EvidenceObservationList> ExecuteAsync(
        ListEvidenceObservationsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        EvidenceReadValidation.EnsureIdentities(query.HouseholdId, query.EvidenceId);

        var list = await _readStore.ListObservationsAsync(
            query.HouseholdId,
            query.EvidenceId,
            cancellationToken);

        if (list is null
            || list.EvidenceId != query.EvidenceId
            || list.HouseholdId != query.HouseholdId)
        {
            throw EvidenceException.Missing(
                EvidenceErrorCodes.NotFound,
                "The evidence record does not exist in this household.");
        }

        return list;
    }
}

internal static class EvidenceReadValidation
{
    internal static void EnsureIdentities(Guid householdId, Guid evidenceId)
    {
        if (householdId == Guid.Empty || evidenceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Household and evidence identities cannot be empty.");
        }
    }
}
