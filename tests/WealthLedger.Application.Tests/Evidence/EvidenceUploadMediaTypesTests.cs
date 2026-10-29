using WealthLedger.Application.Evidence;
using WealthLedger.Domain.Evidence;

namespace WealthLedger.Application.Tests.Evidence;

public sealed class EvidenceUploadMediaTypesTests
{
    [Theory]
    [InlineData("application/pdf", EvidenceMediaType.Pdf)]
    [InlineData("image/png", EvidenceMediaType.Png)]
    [InlineData("image/jpeg", EvidenceMediaType.Jpeg)]
    [InlineData("text/plain", EvidenceMediaType.PlainText)]
    [InlineData("text/plain; charset=utf-8", EvidenceMediaType.PlainText)]
    [InlineData("text/csv", EvidenceMediaType.Csv)]
    [InlineData(" TEXT/CSV ; charset=UTF-8", EvidenceMediaType.Csv)]
    // Windows browsers declare this for .csv files.
    [InlineData("application/vnd.ms-excel", EvidenceMediaType.Csv)]
    public void TryMap_AcceptsTheRetainedFormats(
        string contentType,
        EvidenceMediaType expected)
    {
        Assert.True(EvidenceUploadMediaTypes.TryMap(contentType, out var mediaType));
        Assert.Equal(expected, mediaType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    [InlineData("application/javascript")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("application/vnd.ms-excel.sheet.macroEnabled.12")]
    [InlineData("image/jpg")]
    [InlineData("application/pdfx")]
    public void TryMap_RefusesEverythingElse(string? contentType)
    {
        Assert.False(EvidenceUploadMediaTypes.TryMap(contentType, out _));
    }

    [Fact]
    public void TryMap_RefusesAnOversizedDeclaration()
    {
        Assert.False(EvidenceUploadMediaTypes.TryMap(
            "text/plain;" + new string(' ', 300),
            out _));
    }

    [Fact]
    public void ToContentType_RoundTripsEveryRetainedFormat()
    {
        foreach (var mediaType in Enum.GetValues<EvidenceMediaType>())
        {
            var contentType = EvidenceUploadMediaTypes.ToContentType(mediaType);

            Assert.True(EvidenceUploadMediaTypes.TryMap(contentType, out var mapped));
            Assert.Equal(mediaType, mapped);
        }

        Assert.Equal(
            "text/csv; charset=utf-8",
            EvidenceUploadMediaTypes.ToContentType(EvidenceMediaType.Csv));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EvidenceUploadMediaTypes.ToContentType((EvidenceMediaType)99));
    }
}
