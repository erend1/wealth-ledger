using System.Text.Json;
using System.Text.Json.Serialization;

namespace WealthLedger.Api.Endpoints;

/// <summary>
/// Reads one bounded, strictly shaped JSON command body.
/// </summary>
/// <remarks>
/// The body is read through a fixed byte bound whatever the declared length,
/// so a chunked body cannot grow past it. Unknown members, missing required
/// members, nulls in non-nullable members and numbers sent as strings are
/// refused as malformed input; the problem never echoes the body.
/// </remarks>
internal static class BoundedJsonBody
{
    internal const int MaximumCommandBytes = 64 * 1024;

    internal static readonly JsonSerializerOptions StrictOptions =
        new(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true
        };

    internal static async Task<BoundedJsonResult<T>> ReadAsync<T>(
        HttpRequest request,
        BoundedRequestProblems problems,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(problems);

        if (!request.HasJsonContentType())
        {
            return new(
                null,
                BoundedProblems.Problem(
                    StatusCodes.Status415UnsupportedMediaType,
                    problems.Title,
                    problems.RequestInvalidCode,
                    "The request body must be JSON."));
        }

        var bytes = await BoundedRequestStream.ReadAllAsync(
            request,
            MaximumCommandBytes,
            cancellationToken);

        if (bytes is null)
        {
            return new(null, TooLarge(problems));
        }

        var value = Deserialize<T>(bytes);

        return value is null
            ? new(null, Malformed(problems))
            : new(value, null);
    }

    /// <summary>
    /// Strictly deserializes UTF-8 JSON, or returns null when it is malformed.
    /// </summary>
    /// <remarks>
    /// A leading UTF-8 byte-order mark is skipped, as the stream-based
    /// binding of the other routes does; span deserialization alone would
    /// refuse it.
    /// </remarks>
    internal static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json)
        where T : class
    {
        if (utf8Json.StartsWith(Utf8ByteOrderMark))
        {
            utf8Json = utf8Json[Utf8ByteOrderMark.Length..];
        }

        try
        {
            return JsonSerializer.Deserialize<T>(utf8Json, StrictOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    internal static IResult Malformed(BoundedRequestProblems problems)
        => BoundedProblems.BadRequest(
            problems,
            problems.RequestInvalidCode,
            "The request body is malformed or has an unsupported shape.");

    internal static IResult TooLarge(BoundedRequestProblems problems)
        => BoundedProblems.Problem(
            StatusCodes.Status413PayloadTooLarge,
            problems.Title,
            problems.RequestInvalidCode,
            "The request body exceeds its fixed size bound.");
}

internal readonly record struct BoundedJsonResult<T>(T? Value, IResult? Error)
    where T : class;

internal static class BoundedRequestStream
{
    /// <summary>
    /// Reads the whole body when it fits the bound, or returns null as soon
    /// as it cannot, without buffering past the bound.
    /// </summary>
    internal static async Task<byte[]?> ReadAllAsync(
        HttpRequest request,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximumBytes)
        {
            return null;
        }

        // A declared length sizes the buffer once; the bound still applies
        // to what is actually read.
        using var buffer = new MemoryStream(
            (int)(request.ContentLength ?? 0) + 1);
        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await request.Body.ReadAsync(chunk, cancellationToken);

            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > maximumBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }
    }
}
