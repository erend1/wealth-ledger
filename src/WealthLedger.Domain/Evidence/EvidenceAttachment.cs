using System.Security.Cryptography;

namespace WealthLedger.Domain.Evidence
{
    /// <summary>
    /// The optional retained source file of one evidence record.
    /// </summary>
    /// <remarks>
    /// The attachment owns a private copy of the accepted bytes. Its SHA-256
    /// digest and byte length are computed here from that copy, never taken
    /// from a caller, so they always describe the retained content. The
    /// digest proves byte identity, not financial truth.
    /// </remarks>
    public sealed class EvidenceAttachment
    {
        public const int Sha256HexLength = 64;

        private readonly byte[] _content;

        private EvidenceAttachment(
            string fileName,
            EvidenceMediaType mediaType,
            byte[] content)
        {
            FileName = fileName;
            MediaType = mediaType;
            _content = content;
            Sha256 = ComputeSha256(content);
        }

        public string FileName { get; }

        public EvidenceMediaType MediaType { get; }

        public int ByteLength => _content.Length;

        public string Sha256 { get; }

        public ReadOnlyMemory<byte> Content => _content;

        public static EvidenceAttachment Create(
            string? originalFileName,
            EvidenceMediaType mediaType,
            ReadOnlySpan<byte> content)
        {
            var rejection = EvidenceAttachmentPolicy.Inspect(
                originalFileName,
                mediaType,
                content,
                out var fileName);

            if (rejection is EvidenceAttachmentRejection reason)
            {
                throw new EvidenceAttachmentRejectedException(reason);
            }

            return new EvidenceAttachment(
                fileName!,
                mediaType,
                content.ToArray());
        }

        public static string ComputeSha256(ReadOnlySpan<byte> content)
            => Convert
                .ToHexString(SHA256.HashData(content))
                .ToLowerInvariant();
    }
}
