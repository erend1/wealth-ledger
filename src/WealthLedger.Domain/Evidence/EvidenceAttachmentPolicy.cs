using System.Globalization;
using System.Text;

namespace WealthLedger.Domain.Evidence
{
    public enum EvidenceAttachmentRejection
    {
        Empty,
        TooLarge,
        FileNameInvalid,
        FileNameExtensionMismatch,
        ContentNotAccepted
    }

    public sealed class EvidenceAttachmentRejectedException : Exception
    {
        public EvidenceAttachmentRejectedException(
            EvidenceAttachmentRejection reason)
            : base(MessageFor(reason))
        {
            Reason = reason;
        }

        public EvidenceAttachmentRejection Reason { get; }

        // Messages name the rule only, never the file name or content.
        private static string MessageFor(
            EvidenceAttachmentRejection reason)
            => reason switch
            {
                EvidenceAttachmentRejection.Empty =>
                    "An evidence attachment cannot be empty.",

                EvidenceAttachmentRejection.TooLarge =>
                    "An evidence attachment cannot exceed 10 MiB.",

                EvidenceAttachmentRejection.FileNameInvalid =>
                    "The evidence attachment file name is not usable.",

                EvidenceAttachmentRejection.FileNameExtensionMismatch =>
                    "The evidence attachment file extension does not match its media type.",

                EvidenceAttachmentRejection.ContentNotAccepted =>
                    "The evidence attachment content is not an accepted format.",

                _ => throw new ArgumentOutOfRangeException(
                    nameof(reason),
                    reason,
                    "Unsupported attachment rejection.")
            };
    }

    /// <summary>
    /// Decides whether an attachment may be retained, before any persistence.
    /// </summary>
    /// <remarks>
    /// The declared media type, the file extension and the content signature
    /// must all agree. Binary formats are recognized by their leading
    /// signature. Text formats must be strict UTF-8 without NUL or other
    /// control characters, and must not open with markup, so HTML, SVG or XML
    /// renamed to .txt or .csv is refused. Executables, Office containers and
    /// arbitrary binary content therefore fail every accepted rule.
    /// </remarks>
    public static class EvidenceAttachmentPolicy
    {
        public const int MaximumByteLength = 10 * 1024 * 1024;

        public const int MaximumFileNameLength = 255;

        // Bounds the work done on a caller-supplied path before sanitizing.
        private const int MaximumOriginalFileNameLength = 4_096;

        private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

        private static readonly byte[] PngSignature =
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

        private static readonly byte[] Utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];

        private static readonly string[] ReservedDeviceNames =
        [
            "CON", "PRN", "AUX", "NUL",
            "COM0", "COM1", "COM2", "COM3", "COM4",
            "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4",
            "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ];

        public static EvidenceAttachmentRejection? Inspect(
            string? originalFileName,
            EvidenceMediaType mediaType,
            ReadOnlySpan<byte> content,
            out string? sanitizedFileName)
        {
            EnsureDefined(mediaType);
            sanitizedFileName = null;

            if (content.IsEmpty)
            {
                return EvidenceAttachmentRejection.Empty;
            }

            if (content.Length > MaximumByteLength)
            {
                return EvidenceAttachmentRejection.TooLarge;
            }

            if (!TrySanitizeFileName(
                    originalFileName,
                    out var fileName))
            {
                return EvidenceAttachmentRejection.FileNameInvalid;
            }

            if (!HasAcceptedExtension(fileName, mediaType))
            {
                return EvidenceAttachmentRejection.FileNameExtensionMismatch;
            }

            if (!HasAcceptedContent(mediaType, content))
            {
                return EvidenceAttachmentRejection.ContentNotAccepted;
            }

            sanitizedFileName = fileName;
            return null;
        }

        public static IReadOnlyList<string> AcceptedExtensions(
            EvidenceMediaType mediaType)
        {
            EnsureDefined(mediaType);

            return mediaType switch
            {
                EvidenceMediaType.Pdf => [".pdf"],
                EvidenceMediaType.Png => [".png"],
                EvidenceMediaType.Jpeg => [".jpg", ".jpeg"],
                EvidenceMediaType.PlainText => [".txt"],
                EvidenceMediaType.Csv => [".csv"],
                _ => throw new ArgumentOutOfRangeException(nameof(mediaType))
            };
        }

        /// <summary>
        /// Keeps only the last path segment and replaces characters that can
        /// spoof, break or hide a name with an underscore.
        /// </summary>
        public static bool TrySanitizeFileName(
            string? originalFileName,
            out string sanitizedFileName)
        {
            sanitizedFileName = string.Empty;

            if (string.IsNullOrWhiteSpace(originalFileName)
                || originalFileName.Length > MaximumOriginalFileNameLength)
            {
                return false;
            }

            var lastSeparator = originalFileName.LastIndexOfAny(['/', '\\']);
            var name = originalFileName[(lastSeparator + 1)..];

            if (HasUnpairedSurrogate(name))
            {
                return false;
            }

            try
            {
                name = name.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                return false;
            }

            var builder = new StringBuilder(name.Length);

            foreach (var rune in name.EnumerateRunes())
            {
                if (IsUnsafeFileNameRune(rune))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(rune.ToString());
                }
            }

            name = builder
                .ToString()
                .Trim()
                .TrimEnd('.', ' ');

            var extensionStart = name.LastIndexOf('.');
            var stem = extensionStart < 0
                ? name
                : name[..extensionStart];

            if (stem.Trim('.', ' ').Length == 0)
            {
                return false;
            }

            var deviceStem = stem.Split('.')[0].TrimEnd(' ');

            if (ReservedDeviceNames.Contains(
                    deviceStem,
                    StringComparer.OrdinalIgnoreCase))
            {
                name = "_" + name;
            }

            if (name.Length > MaximumFileNameLength)
            {
                return false;
            }

            sanitizedFileName = name;
            return true;
        }

        internal static bool HasAcceptedExtension(
            string fileName,
            EvidenceMediaType mediaType)
        {
            var extensionStart = fileName.LastIndexOf('.');

            if (extensionStart < 0)
            {
                return false;
            }

            var extension = fileName[extensionStart..];

            return AcceptedExtensions(mediaType).Contains(
                extension,
                StringComparer.OrdinalIgnoreCase);
        }

        internal static bool HasAcceptedContent(
            EvidenceMediaType mediaType,
            ReadOnlySpan<byte> content)
            => mediaType switch
            {
                EvidenceMediaType.Pdf => content.StartsWith(PdfSignature),
                EvidenceMediaType.Png => content.StartsWith(PngSignature),
                EvidenceMediaType.Jpeg => content.StartsWith(JpegSignature),
                EvidenceMediaType.PlainText => IsAcceptedText(content),
                EvidenceMediaType.Csv => IsAcceptedText(content),
                _ => false
            };

        private static bool IsAcceptedText(ReadOnlySpan<byte> content)
        {
            if (content.StartsWith(Utf8ByteOrderMark))
            {
                content = content[Utf8ByteOrderMark.Length..];
            }

            if (content.IsEmpty
                || !System.Text.Unicode.Utf8.IsValid(content))
            {
                return false;
            }

            var leading = true;

            while (!content.IsEmpty)
            {
                Rune.DecodeFromUtf8(
                    content,
                    out var rune,
                    out var consumed);
                content = content[consumed..];

                if (rune.Value is < 0x20 and not ('\t' or '\n' or '\r')
                    || rune.Value is >= 0x7F and <= 0x9F)
                {
                    return false;
                }

                // Invisible format characters (a second BOM, zero-width
                // spaces) cannot hide leading markup.
                if (!leading
                    || Rune.IsWhiteSpace(rune)
                    || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
                {
                    continue;
                }

                if (rune.Value == '<')
                {
                    return false;
                }

                leading = false;
            }

            return true;
        }

        private static bool HasUnpairedSurrogate(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (!char.IsSurrogate(value[index]))
                {
                    continue;
                }

                if (!char.IsSurrogatePair(value, index))
                {
                    return true;
                }

                index++;
            }

            return false;
        }

        private static bool IsUnsafeFileNameRune(Rune rune)
        {
            if (rune.Value is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            {
                return true;
            }

            return Rune.GetUnicodeCategory(rune)
                is UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.PrivateUse
                or UnicodeCategory.Surrogate
                or UnicodeCategory.OtherNotAssigned;
        }

        private static void EnsureDefined(EvidenceMediaType mediaType)
        {
            if (!Enum.IsDefined(mediaType))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mediaType),
                    mediaType,
                    "Unsupported evidence media type.");
            }
        }
    }
}
