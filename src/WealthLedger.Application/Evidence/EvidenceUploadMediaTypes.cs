using WealthLedger.Domain.Evidence;

namespace WealthLedger.Application.Evidence;

/// <summary>
/// Maps a file's declared content type onto the closed set of retained
/// formats, and back for an explicit download.
/// </summary>
/// <remarks>
/// The declared type only selects which rules apply. The attachment policy
/// still checks the extension and the content signature, so a mapped type
/// never admits a file on its own. Windows browsers declare
/// <c>application/vnd.ms-excel</c> for a <c>.csv</c> file; it maps to CSV,
/// and the policy still refuses a real <c>.xls</c> workbook by its extension
/// and content. Parameters such as <c>charset</c> are ignored: text content is
/// accepted only as UTF-8 whatever it declares.
/// </remarks>
public static class EvidenceUploadMediaTypes
{
    private const int MaximumContentTypeLength = 256;

    public static bool TryMap(
        string? contentType,
        out EvidenceMediaType mediaType)
    {
        mediaType = default;

        if (string.IsNullOrWhiteSpace(contentType)
            || contentType.Length > MaximumContentTypeLength)
        {
            return false;
        }

        var separator = contentType.IndexOf(';', StringComparison.Ordinal);
        var essence = (separator < 0 ? contentType : contentType[..separator])
            .Trim()
            .ToLowerInvariant();

        switch (essence)
        {
            case "application/pdf":
                mediaType = EvidenceMediaType.Pdf;
                return true;
            case "image/png":
                mediaType = EvidenceMediaType.Png;
                return true;
            case "image/jpeg":
                mediaType = EvidenceMediaType.Jpeg;
                return true;
            case "text/plain":
                mediaType = EvidenceMediaType.PlainText;
                return true;
            case "text/csv":
            case "application/vnd.ms-excel":
                mediaType = EvidenceMediaType.Csv;
                return true;
            default:
                return false;
        }
    }

    public static string ToContentType(EvidenceMediaType mediaType)
        => mediaType switch
        {
            EvidenceMediaType.Pdf => "application/pdf",
            EvidenceMediaType.Png => "image/png",
            EvidenceMediaType.Jpeg => "image/jpeg",
            EvidenceMediaType.PlainText => "text/plain; charset=utf-8",
            EvidenceMediaType.Csv => "text/csv; charset=utf-8",
            _ => throw new ArgumentOutOfRangeException(
                nameof(mediaType),
                mediaType,
                "The evidence media type has no content type.")
        };
}
