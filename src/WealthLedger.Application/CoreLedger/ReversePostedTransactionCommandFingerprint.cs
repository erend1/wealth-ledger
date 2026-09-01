using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace WealthLedger.Application.CoreLedger
{
    internal static class
        ReversePostedTransactionCommandFingerprint
    {
        internal const string CurrentAlgorithmCode =
            "SHA256";

        internal const int CurrentVersion = 1;

        internal static CommandFingerprint ComputeCurrent(
            ReversePostedTransactionCommand command)
        {
            return Compute(
                command,
                CurrentAlgorithmCode,
                CurrentVersion);
        }

        internal static CommandFingerprint Compute(
            ReversePostedTransactionCommand command,
            string algorithmCode,
            int version)
        {
            ArgumentNullException.ThrowIfNull(command);

            return (algorithmCode, version) switch
            {
                (CurrentAlgorithmCode, 1) =>
                    ComputeV1(command),

                _ =>
                    throw new NotSupportedException(
                        $"Reversal fingerprint '{algorithmCode}' version '{version}' is not supported.")
            };
        }

        private static CommandFingerprint ComputeV1(
            ReversePostedTransactionCommand command)
        {
            var normalized =
                ReversePostedTransactionCommandCanonicalizer
                    .Normalize(command);

            var buffer =
                new ArrayBufferWriter<byte>();

            using (var writer =
                   new Utf8JsonWriter(
                       buffer,
                       new JsonWriterOptions
                       {
                           Indented = false,
                           SkipValidation = false
                       }))
            {
                writer.WriteStartObject();

                writer.WriteNumber(
                    "version",
                    CurrentVersion);

                writer.WriteString(
                    "operation",
                    LedgerOperationCodes
                        .ReversePostedTransaction);

                writer.WriteString(
                    "originalTransactionId",
                    normalized
                        .OriginalTransactionId
                        .ToString("D"));

                writer.WriteString(
                    "reason",
                    normalized.Reason);

                writer.WriteEndObject();
                writer.Flush();
            }

            var hash =
                SHA256.HashData(
                    buffer.WrittenSpan);

            return new CommandFingerprint(
                CurrentAlgorithmCode,
                CurrentVersion,
                Convert
                    .ToHexString(hash)
                    .ToLowerInvariant());
        }
    }
}
