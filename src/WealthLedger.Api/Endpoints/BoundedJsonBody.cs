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

        try
        {
            var value = JsonSerializer.Deserialize<T>(bytes, StrictOptions);

            return value is null
                ? new(null, Malformed(problems))
                : new(value, null);
        }
        catch (JsonException)
        {
            return new(null, Malformed(problems));
        }
    }

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

        using var buffer = new MemoryStream();
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
