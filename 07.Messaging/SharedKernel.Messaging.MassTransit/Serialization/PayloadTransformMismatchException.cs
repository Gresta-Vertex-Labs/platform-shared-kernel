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
/// Wraps the underlying decryption, decompression, or JSON-deserialization failure so a
/// mismatched-configuration scenario fails loudly with a clear, actionable message instead of a
/// confusing downstream <see cref="System.Text.Json.JsonException"/> (or similar) produced by
/// attempting to parse ciphertext or compressed bytes as plain JSON.
/// </para>
/// </remarks>
public sealed class PayloadTransformMismatchException : Exception
{
    /// <summary>Creates a new <see cref="PayloadTransformMismatchException"/>.</summary>
    /// <param name="message">A diagnostic message describing the failure.</param>
    /// <param name="innerException">
    /// The underlying decryption, decompression, or deserialization failure that triggered this
    /// exception.
    /// </param>
    public PayloadTransformMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
