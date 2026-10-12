using SharedKernel.Primitives.Errors;

namespace SharedKernel.Compression.Internal;

/// <summary>Failure results shared by both compressors.</summary>
internal static class CompressionErrors
{
    internal static Error Corrupt() =>
        Error.Validation(
            CompressionErrorCodes.DecompressionFailed,
            "Decompression failed: the compressed payload is corrupt, or was produced by a different algorithm.");

    internal static Error Malformed() =>
        Error.Validation(
            CompressionErrorCodes.MalformedPayload,
            "The payload is not a SharedKernel compression frame. A payload written by a raw compressor must be "
                + "read by a raw compressor.");

    internal static Error UnsupportedVersion(byte version) =>
        Error.Validation(
            CompressionErrorCodes.MalformedPayload,
            $"The payload declares compression frame version {version}, which this package does not understand.");

    internal static Error AlgorithmMismatch(CompressionAlgorithm actual, CompressionAlgorithm expected) =>
        Error.Validation(
            CompressionErrorCodes.AlgorithmMismatch,
            $"The payload was compressed with {actual} but read with {expected}.");

    internal static Error Truncated(long declared, long actual) =>
        Error.Validation(
            CompressionErrorCodes.TruncatedPayload,
            actual < declared
                ? $"The payload decompressed to {actual} bytes but its frame records {declared}, so it was cut short."
                : $"The payload decompressed to {actual} bytes but its frame records {declared}, so data was appended to it.");

    internal static Error TooLarge(long limit) =>
        Error.Validation(
            CompressionErrorCodes.PayloadTooLarge,
            $"Decompression stopped: the payload expands beyond the configured maximum of {limit} bytes.");
}
