using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WealthLedger.Api.Tests;

/// <summary>
/// Drives M010 evidence capture, linking and download through the Ready UI.
/// </summary>
public sealed partial class ReconcileEvidenceUiTests
{
    private const string LabelText = "Synthetic January statement";
    private const string NoteText = "Synthetic private evidence note.";
    private const string ReferenceText = "SYNTH-UI-STATEMENT";

    private static readonly byte[] SyntheticPng =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    [Fact]
    public async Task NewEvidence_RecordsMetadataOnlyEvidenceAndReplaysTheSameKey()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var ledgerBefore = await M010ApiTestData.ReadLedgerCountsAsync(factory);
        var form = await GetEvidenceFormAsync(client);

        Assert.Contains("enctype=\"multipart/form-data\"", form.Html);
        Assert.DoesNotContain("<dt>Piyasa", form.Html);

        using var post = await PostEvidenceAsync(client, form, Statement(history));
        var location = Assert.IsType<Uri>(post.Headers.Location).OriginalString;

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Matches("^/reconcile/evidence/[0-9a-f-]{36}$", location);

        using var replay = await PostEvidenceAsync(client, form, Statement(history));

        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.Equal(location, replay.Headers.Location?.OriginalString);
        Assert.Equal(ledgerBefore, await M010ApiTestData.ReadLedgerCountsAsync(factory));
        Assert.Equal(1, (await M010ApiTestData.ReadAuditCountsAsync(factory))[0]);

        var (detailStatus, detail) = await M010UiTestData.GetPageAsync(client, location);

        Assert.Equal(HttpStatusCode.OK, detailStatus);
        Assert.Contains($"<h1 id=\"evidence-heading\">{LabelText}</h1>", detail);
        Assert.Contains("Hesap ekstresi", detail);
        Assert.Contains("31.01.2026", detail);
        Assert.Contains(NoteText, detail);
        Assert.Contains("Saklanmış dosya yok; bu kayıt yalnızca üst veri içerir.", detail);
        Assert.DoesNotContain("handler=Attachment", detail);
        Assert.DoesNotContain(">Düzenle<", detail);
        Assert.DoesNotContain(">Sil<", detail);

        var (indexStatus, index) = await M010UiTestData.GetPageAsync(client, "/reconcile");

        Assert.Equal(HttpStatusCode.OK, indexStatus);
        Assert.Contains($"href=\"{location}\"", index);
        Assert.Contains(LabelText, index);

        var logs = string.Join(Environment.NewLine, factory.Logs.Messages);
        Assert.DoesNotContain(LabelText, logs);
        Assert.DoesNotContain(NoteText, logs);
        Assert.DoesNotContain(ReferenceText, logs);
    }

    [Fact]
    public async Task NewEvidence_RetainsAFileAndServesItOnlyAsAVerifiedDownload()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var form = await GetEvidenceFormAsync(client);
        const string fileName = "synthetic-count.png";

        using var post = await PostEvidenceAsync(
            client,
            form,
            Statement(history),
            (SyntheticPng, fileName, "image/png"));
        var location = Assert.IsType<Uri>(post.Headers.Location).OriginalString;

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        var (_, detail) = await M010UiTestData.GetPageAsync(client, location);
        var digest = Convert.ToHexStringLower(SHA256.HashData(SyntheticPng));

        Assert.Contains(fileName, detail);
        Assert.Contains("PNG görseli", detail);
        Assert.Contains("12 bayt", detail);
        Assert.Contains(digest, detail);
        Assert.Contains($"href=\"{location}?handler=Attachment\"", detail);
        Assert.DoesNotContain("<img", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<object", detail, StringComparison.OrdinalIgnoreCase);

        using var download = await client.GetAsync(location + "?handler=Attachment");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(fileName, download.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store", download.Headers.CacheControl?.ToString());
        Assert.Contains(
            "sandbox",
            download.Headers.GetValues("Content-Security-Policy").Single(),
            StringComparison.Ordinal);
        Assert.Equal(SyntheticPng, await download.Content.ReadAsByteArrayAsync());

        var logs = string.Join(Environment.NewLine, factory.Logs.Messages);
        Assert.DoesNotContain(fileName, logs);
        Assert.DoesNotContain(digest, logs);
    }

    [Fact]
    public async Task NewEvidence_RefusesInvalidFieldsAndFilesBeforeAnyMutation()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var auditBefore = await M010ApiTestData.ReadAuditCountsAsync(factory);
        var form = await GetEvidenceFormAsync(client);

        var invalidFields = Statement(history);
        invalidFields["Input.Label"] = "   ";
        invalidFields["Input.EffectiveDate"] = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)).ToString("yyyy-MM-dd");

        using (var response = await PostEvidenceAsync(client, form, invalidFields))
        {
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Contains("href=\"#Input_Label\"", html);
            Assert.Contains("Ad zorunludur.", html);
            Assert.Contains("href=\"#Input_EffectiveDate\"", html);
            Assert.Contains("Kanıt tarihi bugünden sonra olamaz.", html);
        }

        foreach (var (file, message) in new (
                     (byte[] Bytes, string FileName, string ContentType) File,
                     string Message)[]
                 {
                     ((Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), "synthetic.svg", "image/svg+xml"),
                         "Bu dosya türü saklanamaz"),
                     ((Encoding.UTF8.GetBytes("not a png"), "synthetic.png", "image/png"),
                         "Dosya"),
                     ((new byte[10 * 1024 * 1024 + 1], "synthetic.pdf", "application/pdf"),
                         "10 MiB")
                 })
        {
            using var response = await PostEvidenceAsync(client, form, Statement(history), file);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Contains(message, html);
            Assert.DoesNotContain(file.FileName, html);
            M010UiTestData.AssertNoInternals(html);
        }

        using (var withoutToken = await client.PostAsync(
                   "/reconcile/evidence/new",
                   new FormUrlEncodedContent(Statement(history))))
        {
            Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        }

        Assert.Equal(auditBefore, await M010ApiTestData.ReadAuditCountsAsync(factory));
    }

    [Fact]
    public async Task Evidence_LinksSelectedPostedTransactionsWithoutChangingThem()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var location = await RecordStatementAsync(client, history);
        var ledgerBefore = await M010ApiTestData.ReadLedgerCountsAsync(factory);

        var (_, detail) = await M010UiTestData.GetPageAsync(client, location);
        var candidates = CandidatePattern()
            .Matches(detail)
            .Select(match => Guid.Parse(match.Groups[1].Value))
            .ToArray();

        // Only Posted transactions in the statement's account, on or before
        // its effective date, are offered.
        Assert.Equal(
            new[] { history.SecondJanuaryId, history.FirstJanuaryId }.Order(),
            candidates.Order());

        using var link = await M010UiTestData.PostFormAsync(
            client,
            location + "?handler=Link",
            [
                new("__RequestVerificationToken", M010UiTestData.TokenFrom(detail)),
                new("transactionIds", history.FirstJanuaryId.ToString("D")),
                new("transactionIds", history.SecondJanuaryId.ToString("D"))
            ]);

        Assert.Equal(HttpStatusCode.Redirect, link.StatusCode);
        Assert.Equal(
            $"{location}?linked=2&alreadyLinked=0",
            link.Headers.Location?.OriginalString);

        var (_, linked) = await M010UiTestData.GetPageAsync(
            client,
            link.Headers.Location!.OriginalString);

        Assert.Contains("2 işlem bağlandı; 0 işlem zaten bağlıydı.", linked);
        Assert.Contains($"href=\"/ledger/{history.FirstJanuaryId:D}\"", linked);
        Assert.Contains("zaten bağlı", linked);
        Assert.Empty(CandidatePattern().Matches(linked));
        Assert.Equal(ledgerBefore, await M010ApiTestData.ReadLedgerCountsAsync(factory));

        using var empty = await M010UiTestData.PostFormAsync(
            client,
            location + "?handler=Link",
            [new("__RequestVerificationToken", M010UiTestData.TokenFrom(linked))]);
        var emptyHtml = WebUtility.HtmlDecode(await empty.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        Assert.Contains("Bağlamak için en az bir işlem seçin.", emptyHtml);

        using var foreign = await M010UiTestData.PostFormAsync(
            client,
            location + "?handler=Link",
            [
                new("__RequestVerificationToken", M010UiTestData.TokenFrom(linked)),
                new("transactionIds", Guid.NewGuid().ToString("D"))
            ]);
        var foreignHtml = WebUtility.HtmlDecode(await foreign.Content.ReadAsStringAsync());

        Assert.False(foreign.IsSuccessStatusCode);
        Assert.Contains("kalıcı olarak kaydedilmiş bir işlem değil", foreignHtml);

        using var withoutToken = await M010UiTestData.PostFormAsync(
            client,
            location + "?handler=Link",
            [new("transactionIds", history.FundPurchaseId.ToString("D"))]);

        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
    }

    [Fact]
    public async Task Evidence_SupersessionKeepsTheOriginalReadableAndClosesItToNewLinks()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var original = await RecordStatementAsync(client, history);
        var originalId = original[(original.LastIndexOf('/') + 1)..];

        var form = await GetEvidenceFormAsync(client, $"?supersedes={originalId}");

        Assert.Contains($"name=\"Input.SupersedesEvidenceId\" value=\"{originalId}\"", form.Html);
        Assert.Contains("Mevcut bir kaydı değiştiriyorsunuz", form.Html);

        var corrected = Statement(history);
        corrected["Input.Label"] = "Synthetic corrected statement";
        corrected["Input.SupersedesEvidenceId"] = originalId;

        using var post = await PostEvidenceAsync(client, form, corrected);
        var replacement = Assert.IsType<Uri>(post.Headers.Location).OriginalString;

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        var (originalStatus, originalHtml) = await M010UiTestData.GetPageAsync(client, original);
        var (_, replacementHtml) = await M010UiTestData.GetPageAsync(client, replacement);

        Assert.Equal(HttpStatusCode.OK, originalStatus);
        Assert.Contains(LabelText, originalHtml);
        Assert.Contains("Bu kayıt yenisiyle değiştirildi", originalHtml);
        Assert.Contains($"href=\"{replacement}\"", originalHtml);
        Assert.DoesNotContain("handler=Link", originalHtml);
        Assert.DoesNotContain("supersedes=", originalHtml);
        Assert.Contains($"href=\"{original}\"", replacementHtml);

        var (_, index) = await M010UiTestData.GetPageAsync(client, "/reconcile");
        Assert.Contains("Yenisiyle değiştirildi", index);

        using var link = await M010UiTestData.PostFormAsync(
            client,
            original + "?handler=Link",
            [
                new("__RequestVerificationToken", M010UiTestData.TokenFrom(replacementHtml)),
                new("transactionIds", history.FirstJanuaryId.ToString("D"))
            ]);
        var linkHtml = WebUtility.HtmlDecode(await link.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, link.StatusCode);
        Assert.Contains("yenisiyle değiştirildi", linkHtml);
    }

    [Fact]
    public async Task Evidence_UnknownRecordAndMissingFileAreSanitized()
    {
        using var factory = new WealthLedgerApiFactory();
        using var client = M010UiTestData.CreateClient(factory);
        var history = await M010UiTestData.SeedAsync(factory, client);
        var metadataOnly = await RecordStatementAsync(client, history);

        var (unknownStatus, unknown) = await M010UiTestData.GetPageAsync(
            client,
            $"/reconcile/evidence/{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.NotFound, unknownStatus);
        Assert.Contains("Kanıt kaydı bu hanede bulunamadı.", unknown);
        M010UiTestData.AssertNoInternals(unknown);

        var (missingStatus, missing) = await M010UiTestData.GetPageAsync(
            client,
            metadataOnly + "?handler=Attachment");

        Assert.Equal(HttpStatusCode.NotFound, missingStatus);
        Assert.Contains("Bu kanıt kaydında saklanmış dosya yok.", missing);

        var (badCursorStatus, badCursor) = await M010UiTestData.GetPageAsync(
            client,
            "/reconcile?evidenceCursor=PRIVATE-EVIDENCE-CURSOR");

        Assert.Equal(HttpStatusCode.BadRequest, badCursorStatus);
        Assert.Contains("Bu mutabakat sayfası açılamadı", badCursor);
        Assert.DoesNotContain("PRIVATE-EVIDENCE-CURSOR", badCursor);
    }

    internal static Dictionary<string, string> Statement(M010UiHistory history)
        => new()
        {
            ["Input.SourceKind"] = "STATEMENT",
            ["Input.EffectiveDate"] = "2026-01-31",
            ["Input.Label"] = LabelText,
            ["Input.InstitutionId"] = history.Setup.InstitutionId.ToString("D"),
            ["Input.AccountId"] = history.Setup.AccountId.ToString("D"),
            ["Input.Reference"] = ReferenceText,
            ["Input.Note"] = NoteText
        };

    /// <summary>
    /// Records the metadata-only statement through the page and returns the
    /// evidence page path.
    /// </summary>
    internal static async Task<string> RecordStatementAsync(
        HttpClient client,
        M010UiHistory history,
        Dictionary<string, string>? fields = null)
    {
        var form = await GetEvidenceFormAsync(client);
        using var post = await PostEvidenceAsync(client, form, fields ?? Statement(history));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        return Assert.IsType<Uri>(post.Headers.Location).OriginalString;
    }

    internal static async Task<EvidenceFormPage> GetEvidenceFormAsync(
        HttpClient client,
        string query = "")
    {
        var (status, html) = await M010UiTestData.GetPageAsync(
            client,
            "/reconcile/evidence/new" + query);

        Assert.Equal(HttpStatusCode.OK, status);
        return new EvidenceFormPage(
            html,
            M010UiTestData.TokenFrom(html),
            M010UiTestData.HiddenValue(IdempotencyKeyPattern(), html, "idempotency key"));
    }

    internal static async Task<HttpResponseMessage> PostEvidenceAsync(
        HttpClient client,
        EvidenceFormPage form,
        Dictionary<string, string> fields,
        (byte[] Bytes, string FileName, string ContentType)? file = null)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(form.Token), "__RequestVerificationToken" },
            { new StringContent(form.IdempotencyKey), "Input.IdempotencyKey" }
        };

        foreach (var (name, value) in fields)
        {
            content.Add(new StringContent(value), name);
        }

        if (file is { } attachment)
        {
            var part = new ByteArrayContent(attachment.Bytes);
            part.Headers.ContentType = MediaTypeHeaderValue.Parse(attachment.ContentType);
            content.Add(part, "Attachment", attachment.FileName);
        }

        return await client.PostAsync("/reconcile/evidence/new", content);
    }

    internal sealed record EvidenceFormPage(
        string Html,
        string Token,
        string IdempotencyKey);

    [GeneratedRegex(
        "name=\"Input.IdempotencyKey\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyPattern();

    [GeneratedRegex(
        "name=\"transactionIds\"[^>]*value=\"([0-9a-f-]{36})\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex CandidatePattern();
}
