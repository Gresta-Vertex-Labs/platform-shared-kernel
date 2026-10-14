using SharedKernel.Core.Exceptions;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Messaging.MassTransit.Serialization;

/// <summary>
/// Thrown when a consumer fails to reverse the payload transform (decrypt and/or decompress)
/// applied by the publisher, or fails to deserialize the resulting bytes.
/// </summary>
/// <remarks>
/// <para>
/// This almost always indicates a mismatch between the publisher's and this consumer's
/// <c>PayloadTransformOptions</c> — e.g. the publisher enabled encryption but this consumer did not,
/// or vice versa, or the two disagree on <c>EnableCompression</c>.
/// </para>
/// <para>
/// Wraps the underlying decryption, decompression, or JSON-deserialization failure as
/// <see cref="Exception.InnerException"/> so a mismatched-configuration scenario fails loudly with a
/// clear, actionable message instead of a confusing downstream
/// <see cref="System.Text.Json.JsonException"/> (or similar) produced by attempting to parse
/// ciphertext or compressed bytes as plain JSON. The inner text never reaches
/// <see cref="Exception.Message"/> or <see cref="SharedKernelException.Error"/>.
/// </para>
/// <para>
/// A <see cref="SharedKernelException"/>: <see cref="SharedKernelException.Error"/> carries
/// <see cref="MessagingErrorCodes.PayloadTransformMismatch"/> with <see cref="ErrorType.Unexpected"/>
/// (HTTP 500, gRPC <c>Internal</c> through the edge maps) — a deployment defect, never a caller's bad input.
/// </para>
/// </remarks>
public sealed class PayloadTransformMismatchException : SharedKernelException
{
    /// <summary>Creates the exception from an <see cref="Primitives.Errors.Error"/>.</summary>
    /// <param name="error">The error describing the failure.</param>
    public PayloadTransformMismatchException(Error error)
        : base(error)
    {
    }

    /// <summary>Creates the exception from an <see cref="Primitives.Errors.Error"/> and its cause.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <param name="innerException">
    /// The underlying decryption, decompression, or deserialization failure that triggered this
    /// exception.
    /// </param>
    public PayloadTransformMismatchException(Error error, Exception innerException)
        : base(error, innerException)
    {
    }
}
