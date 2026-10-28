using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WealthLedger.Api.Contracts;

namespace WealthLedger.Api.Tests;

public sealed class EvidenceAttachmentApiTests
{
    private const int TenMebibytes = 10 * 1024 * 1024;

    private static readonly byte[] SyntheticPdf =
        Encoding.ASCII.GetBytes("%PDF-1.7\n%synthetic evidence statement\n%%EOF\n");

    private static readonly byte[] SyntheticPng =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static readonly byte[] SyntheticJpeg =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    private static readonly byte[] SyntheticText =
        Encoding.UTF8.GetBytes("Synthetic counted pieces: 2\n");

    private static readonly byte[] SyntheticCsv =
        Encoding.UTF8.GetBytes("asset,quantity\nsynthetic,1\n");

    public static TheoryData<string, string, string, string> RetainedFormats => new()
    {
        { "synthetic-statement.pdf", "application/pdf", "PDF", "application/pdf" },
        { "synthetic-receipt.png", "image/png", "PNG", "image/png" },
        { "synthetic-receipt.jpg", "image/jpeg", "JPEG", "image/jpeg" },
        { "synthetic-count.txt", "text/plain", "PLAIN_TEXT", "text/plain" },
        { "synthetic-export.csv", "text/csv", "CSV", "text/csv" },
        // What Windows browsers declare for a .csv file.
        { "synthetic-export.csv", "application/vnd.ms-excel", "CSV", "text/csv" }
    };

    [Theory]
    [MemberData(nameof(RetainedFormats))]
    public async Task Upload_RoundTripsEachRetainedFormatByteForByte(
        string fileName,
        string declaredContentType,
        string mediaTypeCode,
        string downloadMediaType)
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var content = ContentFor(mediaTypeCode);
        var route = $"{history.HouseholdRoute}/evidence";

        using var created = await UploadAsync(
            client,
            history,
            Upload(M010ApiTestData.Statement(history), fileName, declaredContentType, content),
            "upload-key-1");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var result = (await created.Content.ReadFromJsonAsync<RecordEvidenceResponse>())!;

        // A same-key retry with the same file returns the record and keeps
        // one retained copy.
        using var replay = await UploadAsync(
            client,
            history,
            Upload(M010ApiTestData.Statement(history), fileName, declaredContentType, content),
            "upload-key-1");
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(
            result.EvidenceId,
            (await replay.Content.ReadFromJsonAsync<RecordEvidenceResponse>())!.EvidenceId);

        using var detailResponse = await client.GetAsync($"{route}/{result.EvidenceId}");
        var detailJson = await detailResponse.Content.ReadAsStringAsync();
        var detail = JsonSerializer.Deserialize<EvidenceDetailResponse>(
            detailJson,
            JsonSerializerOptions.Web)!;
        var attachment = Assert.IsType<EvidenceAttachmentMetadataResponse>(detail.Attachment);
        var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        Assert.Equal(
            (fileName, mediaTypeCode, content.Length, sha256),
            (attachment.FileName, attachment.MediaTypeCode, attachment.ByteLength, attachment.Sha256));
        Assert.Equal($"{route}/{result.EvidenceId}/attachment", attachment.DownloadLocation);

        // Metadata never embeds the bytes.
        Assert.DoesNotContain(Convert.ToBase64String(content), detailJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"content\"", detailJson, StringComparison.OrdinalIgnoreCase);

        using var download = await client.GetAsync(attachment.DownloadLocation);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(downloadMediaType, download.Content.Headers.ContentType?.MediaType);
        var disposition = Assert.IsType<ContentDispositionHeaderValue>(
            download.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal(fileName, disposition.FileNameStar ?? disposition.FileName?.Trim('"'));
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("no-store", download.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "sandbox",
            Assert.Single(download.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);

        Assert.Equal(1, (await M010ApiTestData.ReadAuditCountsAsync(factory))[1]);
    }

    [Fact]
    public async Task Upload_AcceptsExactlyTenMebibytesInsideItsEnvelope()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var content = PaddedPdf(TenMebibytes);

        using var created = await UploadAsync(
            client,
            history,
            Upload(M010ApiTestData.Statement(history), "synthetic-large.pdf", "application/pdf", content),
            "upload-ten-mebibytes");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var evidenceId = (await created.Content.ReadFromJsonAsync<RecordEvidenceResponse>())!.EvidenceId;
        using var download = await client.GetAsync(
            $"{history.HouseholdRoute}/evidence/{evidenceId}/attachment");
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_RefusesDisallowedMalformedOrOversizedUploadsBeforeMutation()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var metadata = M010ApiTestData.Statement(history);
        var before = await M010ApiTestData.ReadAuditCountsAsync(factory);

        (Func<HttpContent> Body, HttpStatusCode Status, string Code)[] refusals =
        [
            (() => Upload(metadata, "synthetic.svg", "image/svg+xml", Encoding.UTF8.GetBytes("<svg/>")),
                HttpStatusCode.UnsupportedMediaType, "EVIDENCE_ATTACHMENT_TYPE_UNSUPPORTED"),
            (() => Upload(metadata, "synthetic.html", "text/html", Encoding.UTF8.GetBytes("<html></html>")),
                HttpStatusCode.UnsupportedMediaType, "EVIDENCE_ATTACHMENT_TYPE_UNSUPPORTED"),
            (() => Upload(metadata, "synthetic.bin", "application/octet-stream", SyntheticPdf),
                HttpStatusCode.UnsupportedMediaType, "EVIDENCE_ATTACHMENT_TYPE_UNSUPPORTED"),
            (() => Upload(metadata, "synthetic.pdf", null, SyntheticPdf),
                HttpStatusCode.UnsupportedMediaType, "EVIDENCE_ATTACHMENT_TYPE_UNSUPPORTED"),

            // Markup disguised as text, a workbook declared like a Windows
            // CSV, and a PDF named as an image all fail the content rules.
            (() => Upload(metadata, "synthetic.txt", "text/plain", Encoding.UTF8.GetBytes("<script>x</script>")),
                HttpStatusCode.UnprocessableEntity, "EVIDENCE_ATTACHMENT_CONTENT_REJECTED"),
            (() => Upload(metadata, "synthetic.xls", "application/vnd.ms-excel", [0xD0, 0xCF, 0x11, 0xE0]),
                HttpStatusCode.UnprocessableEntity, "EVIDENCE_ATTACHMENT_TYPE_MISMATCH"),
            (() => Upload(metadata, "synthetic.png", "image/png", SyntheticPdf),
                HttpStatusCode.UnprocessableEntity, "EVIDENCE_ATTACHMENT_CONTENT_REJECTED"),
            (() => Upload(metadata, "synthetic.pdf", "application/pdf", []),
                HttpStatusCode.UnprocessableEntity, "EVIDENCE_ATTACHMENT_EMPTY"),
            (() => Upload(metadata, "synthetic.pdf", "application/pdf", PaddedPdf(TenMebibytes + 1)),
                HttpStatusCode.RequestEntityTooLarge, "EVIDENCE_ATTACHMENT_TOO_LARGE"),
            (() => Upload(metadata, "synthetic.pdf", "application/pdf", PaddedPdf(TenMebibytes + 128 * 1024)),
                HttpStatusCode.RequestEntityTooLarge, "EVIDENCE_UPLOAD_TOO_LARGE"),

            // The envelope must hold exactly one metadata and one file part.
            (() => Parts(("metadata", Json(metadata), null, null)),
                HttpStatusCode.BadRequest, "EVIDENCE_REQUEST_INVALID"),
            (() => Parts(("file", new ByteArrayContent(SyntheticPdf), "synthetic.pdf", "application/pdf")),
                HttpStatusCode.BadRequest, "EVIDENCE_REQUEST_INVALID"),
            (() => Parts(
                    ("metadata", Json(metadata), null, null),
                    ("file", new ByteArrayContent(SyntheticPdf), "synthetic.pdf", "application/pdf"),
                    ("file", new ByteArrayContent(SyntheticPdf), "second.pdf", "application/pdf")),
                HttpStatusCode.BadRequest, "EVIDENCE_REQUEST_INVALID"),
            (() => Parts(
                    ("metadata", Json(metadata), null, null),
                    ("note", new StringContent("Synthetic private"), null, null)),
                HttpStatusCode.BadRequest, "EVIDENCE_REQUEST_INVALID"),
            (() => Parts(
                    ("metadata", new StringContent("{not json", Encoding.UTF8, "application/json"), null, null),
                    ("file", new ByteArrayContent(SyntheticPdf), "synthetic.pdf", "application/pdf")),
                HttpStatusCode.BadRequest, "EVIDENCE_REQUEST_INVALID"),
            (() => Parts(
                    ("metadata", Json(metadata with { SourceKindCode = "BANK_EXPORT" }), null, null),
                    ("file", new ByteArrayContent(SyntheticPdf), "synthetic.pdf", "application/pdf")),
                HttpStatusCode.BadRequest, "EVIDENCE_REQUEST_INVALID"),
            (() => Parts(
                    ("metadata", Json(metadata), null, null),
                    ("file", new ByteArrayContent(SyntheticPdf), null, "application/pdf")),
                HttpStatusCode.BadRequest, "EVIDENCE_REQUEST_INVALID"),
            (() => Json(metadata), HttpStatusCode.UnsupportedMediaType, "EVIDENCE_REQUEST_INVALID")
        ];

        foreach (var (body, status, code) in refusals)
        {
            using var response = await UploadAsync(
                client,
                history,
                body(),
                $"upload-refusal-{Guid.NewGuid():N}");
            await M010ApiTestData.AssertProblemAsync(
                response,
                status,
                code,
                "synthetic.",
                "Synthetic private",
                "SYNTH-STATEMENT-REF");
        }

        // Without a declared length the read bound itself refuses the body.
        using (var chunked = await UploadAsync(
                   client,
                   history,
                   new UnknownLengthContent(
                       Upload(metadata, "synthetic.pdf", "application/pdf", PaddedPdf(TenMebibytes + 128 * 1024))),
                   "upload-chunked"))
        {
            await M010ApiTestData.AssertProblemAsync(
                chunked,
                HttpStatusCode.RequestEntityTooLarge,
                "EVIDENCE_UPLOAD_TOO_LARGE");
        }

        using (var missingKey = await UploadAsync(
                   client,
                   history,
                   Upload(metadata, "synthetic.pdf", "application/pdf", SyntheticPdf),
                   idempotencyKey: null))
        {
            await M010ApiTestData.AssertProblemAsync(
                missingKey,
                HttpStatusCode.BadRequest,
                "IDEMPOTENCY_KEY_REQUIRED");
        }

        Assert.Equal(before, await M010ApiTestData.ReadAuditCountsAsync(factory));
    }

    [Fact]
    public async Task Download_IsHouseholdScopedAndFailsClosedOnAlteredBytes()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = factory.CreateClient();
        var history = await M010ApiTestData.SeedAsync(factory, client);
        var route = $"{history.HouseholdRoute}/evidence";
        var metadataOnlyId = await M010ApiTestData.RecordStatementAsync(client, history);

        using var created = await UploadAsync(
            client,
            history,
            Upload(M010ApiTestData.Statement(history), "synthetic-statement.pdf", "application/pdf", SyntheticPdf),
            "upload-integrity");
        var evidenceId = (await created.Content.ReadFromJsonAsync<RecordEvidenceResponse>())!.EvidenceId;
        var sha256 = Convert.ToHexString(SHA256.HashData(SyntheticPdf)).ToLowerInvariant();

        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"{route}/{metadataOnlyId}/attachment",
            HttpStatusCode.NotFound,
            "EVIDENCE_ATTACHMENT_NOT_FOUND");
        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"/api/households/{history.OtherHouseholdId}/evidence/{evidenceId}/attachment",
            HttpStatusCode.NotFound,
            "EVIDENCE_ATTACHMENT_NOT_FOUND",
            "synthetic-statement",
            sha256);
        await M010ApiTestData.AssertGetProblemAsync(
            client,
            $"{route}/{evidenceId}/attachment?inline=true",
            HttpStatusCode.BadRequest,
            "EVIDENCE_REQUEST_INVALID");

        // Bytes changed outside the Application no longer match the digest.
        await using (var connection = new SqliteConnection(
                         new SqliteConnectionStringBuilder
                         {
                             DataSource = factory.DatabasePath,
                             Pooling = false
                         }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DROP TRIGGER TR_EvidenceAttachment_PreventUpdate;
                UPDATE EvidenceAttachment SET Content = $content WHERE EvidenceId = $id;
                """;
            // Same length, one byte changed: only the digest can tell.
            var altered = SyntheticPdf.ToArray();
            altered[^7] ^= 0x01;
            command.Parameters.AddWithValue("$content", altered);
            command.Parameters.AddWithValue("$id", evidenceId.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        using var refused = await client.GetAsync($"{route}/{evidenceId}/attachment");
        await M010ApiTestData.AssertProblemAsync(
            refused,
            HttpStatusCode.Conflict,
            "EVIDENCE_ATTACHMENT_INTEGRITY_FAILED",
            "synthetic-statement",
            sha256,
            "%PDF");
    }

    internal static MultipartFormDataContent Upload(
        RecordEvidenceRequest metadata,
        string fileName,
        string? contentType,
        byte[] content)
        => Parts(
            ("metadata", Json(metadata), null, null),
            ("file", new ByteArrayContent(content), fileName, contentType));

    internal static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        M010ApiHistory history,
        HttpContent body,
        string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{history.HouseholdRoute}/evidence/uploads")
        {
            Content = body
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private static MultipartFormDataContent Parts(
        params (string Name, HttpContent Content, string? FileName, string? ContentType)[] parts)
    {
        var form = new MultipartFormDataContent();

        foreach (var (name, content, fileName, contentType) in parts)
        {
            // Without a declared type a byte part carries none at all.
            if (contentType is not null)
            {
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            }

            if (fileName is null)
            {
                form.Add(content, name);
            }
            else
            {
                form.Add(content, name, fileName);
            }
        }

        return form;
    }

    private static StringContent Json(RecordEvidenceRequest metadata)
        => new(
            JsonSerializer.Serialize(metadata, JsonSerializerOptions.Web),
            Encoding.UTF8,
            "application/json");

    private static byte[] ContentFor(string mediaTypeCode)
        => mediaTypeCode switch
        {
            "PDF" => SyntheticPdf,
            "PNG" => SyntheticPng,
            "JPEG" => SyntheticJpeg,
            "PLAIN_TEXT" => SyntheticText,
            "CSV" => SyntheticCsv,
            _ => throw new ArgumentOutOfRangeException(nameof(mediaTypeCode))
        };

    // Sends the inner content without Content-Length, as a chunked body.
    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly HttpContent _inner;

        internal UnknownLengthContent(HttpContent inner)
        {
            _inner = inner;

            foreach (var header in inner.Headers)
            {
                if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => _inner.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    // A synthetic PDF of an exact length: the signature, then padding.
    private static byte[] PaddedPdf(int length)
    {
        var content = new byte[length];
        Array.Fill(content, (byte)' ');
        SyntheticPdf.CopyTo(content, 0);
        return content;
    }
}
