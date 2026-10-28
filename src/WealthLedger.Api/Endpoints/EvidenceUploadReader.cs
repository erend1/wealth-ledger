using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using WealthLedger.Api.Contracts;
using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Evidence;

namespace WealthLedger.Api.Endpoints;

/// <summary>
/// Reads one bounded evidence upload: a "metadata" JSON part and one "file"
/// part, nothing else.
/// </summary>
/// <remarks>
/// The whole body is bounded before parsing, whatever its declared length,
/// so a chunked body cannot grow past it. The file's declared content type
/// selects its retained format; the attachment policy then checks extension
/// and content. Problems never echo the file name, metadata or bytes.
/// </remarks>
internal static class EvidenceUploadReader
{
    internal const string UploadTooLargeCode = "EVIDENCE_UPLOAD_TOO_LARGE";
    internal const string AttachmentTypeUnsupportedCode =
        "EVIDENCE_ATTACHMENT_TYPE_UNSUPPORTED";

    // The largest retained file plus room for the metadata part and the
    // multipart envelope.
    internal const int MaximumBodyBytes =
        EvidenceAttachmentPolicy.MaximumByteLength + BoundedJsonBody.MaximumCommandBytes;

    private const int MaximumBoundaryLength = 70;
    private const int MaximumSections = 2;
    private const string MetadataPart = "metadata";
    private const string FilePart = "file";

    internal static async Task<EvidenceUploadResult> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !string.Equals(
                contentType.MediaType.Value,
                "multipart/form-data",
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                StatusCodes.Status415UnsupportedMediaType,
                EvidenceEndpoints.RequestInvalidCode,
                "An evidence upload must be multipart/form-data.");
        }

        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;

        if (string.IsNullOrWhiteSpace(boundary)
            || boundary.Length > MaximumBoundaryLength)
        {
            return Malformed();
        }

        var body = await BoundedRequestStream.ReadAllAsync(
            request,
            MaximumBodyBytes,
            cancellationToken);

        if (body is null)
        {
            return Failure(
                StatusCodes.Status413PayloadTooLarge,
                UploadTooLargeCode,
                "The evidence upload exceeds its fixed size bound.");
        }

        RecordEvidenceRequest? metadata = null;
        EvidenceAttachmentUpload? attachment = null;

        try
        {
            using var stream = new MemoryStream(body, writable: false);
            var reader = new MultipartReader(boundary, stream)
            {
                HeadersCountLimit = 8,
                HeadersLengthLimit = 16 * 1024,

                // The body is already bounded; each part is checked against
                // its own bound below, so an oversized file is a 413.
                BodyLengthLimit = MaximumBodyBytes
            };
            var sectionCount = 0;

            while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                if (++sectionCount > MaximumSections
                    || !ContentDispositionHeaderValue.TryParse(
                        section.ContentDisposition,
                        out var disposition)
                    || !disposition.DispositionType.Equals(
                        "form-data",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Malformed();
                }

                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;

                // Some clients send the metadata as a file part; its name
                // decides, not the presence of a file name.
                if (string.Equals(name, MetadataPart, StringComparison.Ordinal)
                    && metadata is null)
                {
                    metadata = await ReadMetadataAsync(section, cancellationToken);

                    if (metadata is null)
                    {
                        return Malformed();
                    }
                }
                else if (string.Equals(name, FilePart, StringComparison.Ordinal)
                         && attachment is null
                         && disposition.IsFileDisposition())
                {
                    var file = await ReadFileAsync(section, disposition, cancellationToken);

                    if (file.Error is not null)
                    {
                        return new EvidenceUploadResult(null, null, file.Error);
                    }

                    attachment = file.Attachment;
                }
                else
                {
                    return Malformed();
                }
            }
        }
        catch (Exception exception)
            when (exception is IOException or InvalidDataException)
        {
            // A broken envelope, or a part past the multipart bounds.
            return Malformed();
        }

        return metadata is null || attachment is null
            ? Malformed()
            : new EvidenceUploadResult(metadata, attachment, null);
    }

    private static async Task<RecordEvidenceRequest?> ReadMetadataAsync(
        MultipartSection section,
        CancellationToken cancellationToken)
    {
        if (section.ContentType is { } declared
            && (!MediaTypeHeaderValue.TryParse(declared, out var type)
                || !string.Equals(
                    type.MediaType.Value,
                    "application/json",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await section.Body.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length > BoundedJsonBody.MaximumCommandBytes)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RecordEvidenceRequest>(
                buffer.ToArray(),
                BoundedJsonBody.StrictOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<(EvidenceAttachmentUpload? Attachment, IResult? Error)> ReadFileAsync(
        MultipartSection section,
        ContentDispositionHeaderValue disposition,
        CancellationToken cancellationToken)
    {
        var fileName = HeaderUtilities.RemoveQuotes(
            disposition.FileNameStar.HasValue
                ? disposition.FileNameStar
                : disposition.FileName).Value;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return (null, Malformed().Error);
        }

        if (!EvidenceUploadMediaTypes.TryMap(section.ContentType, out var mediaType))
        {
            return (
                null,
                Failure(
                    StatusCodes.Status415UnsupportedMediaType,
                    AttachmentTypeUnsupportedCode,
                    "Retained evidence files must be PDF, PNG, JPEG, UTF-8 text or CSV.").Error);
        }

        using var buffer = new MemoryStream();
        await section.Body.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length > EvidenceAttachmentPolicy.MaximumByteLength)
        {
            return (
                null,
                Failure(
                    StatusCodes.Status413PayloadTooLarge,
                    EvidenceErrorCodes.AttachmentTooLarge,
                    "A retained evidence file can be at most 10 MiB.").Error);
        }

        return (
            new EvidenceAttachmentUpload(fileName, mediaType, buffer.ToArray()),
            null);
    }

    private static EvidenceUploadResult Malformed()
        => new(
            null,
            null,
            BoundedProblems.BadRequest(
                EvidenceEndpoints.Problems,
                EvidenceEndpoints.RequestInvalidCode,
                "An evidence upload needs exactly one metadata part and one file part."));

    private static EvidenceUploadResult Failure(int statusCode, string code, string detail)
        => new(
            null,
            null,
            BoundedProblems.Problem(
                statusCode,
                EvidenceEndpoints.Problems.Title,
                code,
                detail));
}

internal readonly record struct EvidenceUploadResult(
    RecordEvidenceRequest? Metadata,
    EvidenceAttachmentUpload? Attachment,
    IResult? Error);
