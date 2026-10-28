using System.Text;
using WealthLedger.Domain.Evidence;

namespace WealthLedger.Domain.Tests.Evidence;

public sealed class EvidenceAttachmentTests
{
    private static readonly byte[] SyntheticPdf =
        Encoding.ASCII.GetBytes("%PDF-1.7\n%synthetic statement\n%%EOF\n");

    private static readonly byte[] SyntheticPng =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static readonly byte[] SyntheticJpeg =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    [Fact]
    public void Create_ComputesKnownSha256AndLengthFromRetainedBytes()
    {
        var attachment = EvidenceAttachment.Create(
            "synthetic.txt",
            EvidenceMediaType.PlainText,
            "abc"u8);

        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            attachment.Sha256);
        Assert.Equal(3, attachment.ByteLength);
        Assert.Equal("synthetic.txt", attachment.FileName);
        Assert.Equal(EvidenceMediaType.PlainText, attachment.MediaType);
    }

    [Fact]
    public void Create_RetainsAPrivateCopyOfTheBytes()
    {
        var source = Encoding.UTF8.GetBytes("synthetic count,1\n");
        var attachment = EvidenceAttachment.Create(
            "count.csv",
            EvidenceMediaType.Csv,
            source);
        var originalDigest = attachment.Sha256;

        source[0] = (byte)'X';

        Assert.Equal((byte)'s', attachment.Content.Span[0]);
        Assert.Equal(
            originalDigest,
            EvidenceAttachment.ComputeSha256(attachment.Content.Span));
    }

    public static TheoryData<string, EvidenceMediaType, byte[]> AcceptedFiles()
        => new()
        {
            { "statement.pdf", EvidenceMediaType.Pdf, SyntheticPdf },
            { "STATEMENT.PDF", EvidenceMediaType.Pdf, SyntheticPdf },
            { "receipt.png", EvidenceMediaType.Png, SyntheticPng },
            { "receipt.jpg", EvidenceMediaType.Jpeg, SyntheticJpeg },
            { "receipt.jpeg", EvidenceMediaType.Jpeg, SyntheticJpeg },
            { "count.txt", EvidenceMediaType.PlainText, Encoding.UTF8.GetBytes("Synthetic count\r\n\tbar 1\n") },
            { "count.txt", EvidenceMediaType.PlainText, [0xEF, 0xBB, 0xBF, .. "Sayım: 1 külçe"u8] },
            { "count.csv", EvidenceMediaType.Csv, Encoding.UTF8.GetBytes("date,quantity\n2026-01-31,1.5\n") },
            { "notes.txt", EvidenceMediaType.PlainText, Encoding.UTF8.GetBytes("1 < 2 inside the text is fine") }
        };

    [Theory]
    [MemberData(nameof(AcceptedFiles))]
    public void Create_AcceptsEachSupportedFormat(
        string fileName,
        EvidenceMediaType mediaType,
        byte[] content)
    {
        var attachment = EvidenceAttachment.Create(
            fileName,
            mediaType,
            content);

        Assert.Equal(content.Length, attachment.ByteLength);
        Assert.Equal(content, attachment.Content.ToArray());
    }

    [Fact]
    public void Create_AcceptsExactlyTenMebibytes()
    {
        var content = new byte[EvidenceAttachmentPolicy.MaximumByteLength];
        Array.Fill(content, (byte)'a');

        var attachment = EvidenceAttachment.Create(
            "large.txt",
            EvidenceMediaType.PlainText,
            content);

        Assert.Equal(10 * 1024 * 1024, attachment.ByteLength);
    }

    public static TheoryData<string, EvidenceMediaType, byte[], EvidenceAttachmentRejection> RejectedFiles()
        => new()
        {
            { "empty.txt", EvidenceMediaType.PlainText, [], EvidenceAttachmentRejection.Empty },
            { "image.svg", EvidenceMediaType.PlainText, Encoding.UTF8.GetBytes("<svg/>"), EvidenceAttachmentRejection.FileNameExtensionMismatch },
            { "image.txt", EvidenceMediaType.PlainText, Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), EvidenceAttachmentRejection.ContentNotAccepted },
            { "page.csv", EvidenceMediaType.Csv, Encoding.UTF8.GetBytes("  \r\n<!DOCTYPE html><html></html>"), EvidenceAttachmentRejection.ContentNotAccepted },
            { "page.txt", EvidenceMediaType.PlainText, [0xEF, 0xBB, 0xBF, .. "<?xml version=\"1.0\"?>"u8], EvidenceAttachmentRejection.ContentNotAccepted },
            { "page.html", EvidenceMediaType.PlainText, Encoding.UTF8.GetBytes("text"), EvidenceAttachmentRejection.FileNameExtensionMismatch },
            { "tool.exe", EvidenceMediaType.Pdf, [0x4D, 0x5A, 0x90, 0x00], EvidenceAttachmentRejection.FileNameExtensionMismatch },
            { "tool.pdf", EvidenceMediaType.Pdf, [0x4D, 0x5A, 0x90, 0x00], EvidenceAttachmentRejection.ContentNotAccepted },
            { "tool.txt", EvidenceMediaType.PlainText, [0x4D, 0x5A, 0x90, 0x00], EvidenceAttachmentRejection.ContentNotAccepted },
            { "script.txt", EvidenceMediaType.PlainText, Encoding.UTF8.GetBytes("<script>alert(1)</script>"), EvidenceAttachmentRejection.ContentNotAccepted },
            { "book.xlsm", EvidenceMediaType.Csv, [0x50, 0x4B, 0x03, 0x04], EvidenceAttachmentRejection.FileNameExtensionMismatch },
            { "book.pdf", EvidenceMediaType.Pdf, [0x50, 0x4B, 0x03, 0x04], EvidenceAttachmentRejection.ContentNotAccepted },
            { "invalid.txt", EvidenceMediaType.PlainText, [0xC3, 0x28], EvidenceAttachmentRejection.ContentNotAccepted },
            { "nul.csv", EvidenceMediaType.Csv, [(byte)'a', 0x00, (byte)'b'], EvidenceAttachmentRejection.ContentNotAccepted },
            { "c1.txt", EvidenceMediaType.PlainText, [(byte)'a', 0xC2, 0x85, (byte)'b'], EvidenceAttachmentRejection.ContentNotAccepted },
            { "bom.txt", EvidenceMediaType.PlainText, [0xEF, 0xBB, 0xBF], EvidenceAttachmentRejection.ContentNotAccepted },
            { "double-bom.txt", EvidenceMediaType.PlainText, [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, .. "<html></html>"u8], EvidenceAttachmentRejection.ContentNotAccepted },
            { "zero-width.csv", EvidenceMediaType.Csv, [.. " ​⁠<svg/>"u8], EvidenceAttachmentRejection.ContentNotAccepted },
            { "statement.png", EvidenceMediaType.Png, SyntheticPdf, EvidenceAttachmentRejection.ContentNotAccepted },
            { "receipt.jpg", EvidenceMediaType.Jpeg, SyntheticPng, EvidenceAttachmentRejection.ContentNotAccepted },
            { "statement.pdf", EvidenceMediaType.PlainText, Encoding.UTF8.GetBytes("text"), EvidenceAttachmentRejection.FileNameExtensionMismatch },
            { "statement", EvidenceMediaType.Pdf, SyntheticPdf, EvidenceAttachmentRejection.FileNameExtensionMismatch },
            { ".pdf", EvidenceMediaType.Pdf, SyntheticPdf, EvidenceAttachmentRejection.FileNameInvalid },
            { "folder/", EvidenceMediaType.Pdf, SyntheticPdf, EvidenceAttachmentRejection.FileNameInvalid },
            { "   ", EvidenceMediaType.Pdf, SyntheticPdf, EvidenceAttachmentRejection.FileNameInvalid },
            { new string('a', 252) + ".pdf", EvidenceMediaType.Pdf, SyntheticPdf, EvidenceAttachmentRejection.FileNameInvalid }
        };

    [Theory]
    [MemberData(nameof(RejectedFiles))]
    public void Create_RejectsDisallowedFilesBeforeRetainingAnything(
        string fileName,
        EvidenceMediaType mediaType,
        byte[] content,
        EvidenceAttachmentRejection expected)
    {
        var exception = Assert.Throws<EvidenceAttachmentRejectedException>(
            () => EvidenceAttachment.Create(
                fileName,
                mediaType,
                content));

        Assert.Equal(expected, exception.Reason);
    }

    [Fact]
    public void Create_RejectsContentOverTenMebibytesBeforeInspectingIt()
    {
        var content = new byte[EvidenceAttachmentPolicy.MaximumByteLength + 1];

        var exception = Assert.Throws<EvidenceAttachmentRejectedException>(
            () => EvidenceAttachment.Create(
                "large.txt",
                EvidenceMediaType.PlainText,
                content));

        Assert.Equal(EvidenceAttachmentRejection.TooLarge, exception.Reason);
    }

    [Fact]
    public void Create_RejectsUnpairedSurrogateInFileName()
    {
        // Built here: xUnit theory data would serialize it as U+FFFD.
        var fileName = new string(['\uD800', 'b', '.', 'p', 'd', 'f']);

        var exception = Assert.Throws<EvidenceAttachmentRejectedException>(
            () => EvidenceAttachment.Create(
                fileName,
                EvidenceMediaType.Pdf,
                SyntheticPdf));

        Assert.Equal(EvidenceAttachmentRejection.FileNameInvalid, exception.Reason);
    }

    [Fact]
    public void Create_RejectsNullFileName()
    {
        var exception = Assert.Throws<EvidenceAttachmentRejectedException>(
            () => EvidenceAttachment.Create(
                null,
                EvidenceMediaType.Pdf,
                SyntheticPdf));

        Assert.Equal(EvidenceAttachmentRejection.FileNameInvalid, exception.Reason);
    }

    [Fact]
    public void Create_RejectsUndefinedMediaType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EvidenceAttachment.Create(
                "statement.pdf",
                (EvidenceMediaType)99,
                SyntheticPdf));
    }

    [Fact]
    public void Rejection_MessageNeverContainsTheFileName()
    {
        var exception = Assert.Throws<EvidenceAttachmentRejectedException>(
            () => EvidenceAttachment.Create(
                "SYNTHETIC-PRIVATE-NAME.svg",
                EvidenceMediaType.PlainText,
                "<svg/>"u8));

        Assert.DoesNotContain(
            "SYNTHETIC",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\synthetic\folder\statement.pdf", "statement.pdf")]
    [InlineData("../../synthetic/statement.pdf", "statement.pdf")]
    [InlineData("state<ment>:|?*\".pdf", "state_ment______.pdf")]
    [InlineData("invoice\u202Efdp.txt", "invoice_fdp.txt")]
    [InlineData("line\nbreak.txt", "line_break.txt")]
    [InlineData("  statement.pdf. .  ", "statement.pdf")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("lpt1.backup.csv", "_lpt1.backup.csv")]
    [InlineData("e\u0301tat.txt", "\u00E9tat.txt")]
    [InlineData("Hesap özeti 2026.pdf", "Hesap özeti 2026.pdf")]
    public void TrySanitizeFileName_KeepsOnlyASafeLastSegment(
        string original,
        string expected)
    {
        Assert.True(
            EvidenceAttachmentPolicy.TrySanitizeFileName(
                original,
                out var sanitized));
        Assert.Equal(expected, sanitized);
    }

    [Fact]
    public void TrySanitizeFileName_RejectsAnOversizedOriginalPath()
    {
        Assert.False(
            EvidenceAttachmentPolicy.TrySanitizeFileName(
                new string('a', 5_000) + ".pdf",
                out _));
    }
}
