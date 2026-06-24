using SharedKernel.Primitives.Errors;
using EnvelopeNs = SharedKernel.Contracts.Envelope;

namespace SharedKernel.Testing.Contracts;

/// <summary>
/// Plain-exception assertion helpers over <see cref="EnvelopeNs.Envelope"/> /
/// <see cref="EnvelopeNs.Envelope{T}"/>.
/// </summary>
/// <remarks>Zero dependency on any test-framework assertion library.</remarks>
public static class EnvelopeAssertions
{
    /// <summary>Asserts that <paramref name="envelope"/> represents success.</summary>
    /// <param name="envelope">The envelope to evaluate.</param>
    /// <exception cref="InvalidOperationException"><paramref name="envelope"/> is a failure.</exception>
    public static void ShouldBeSuccess(this EnvelopeNs.Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!envelope.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Expected envelope to be successful, but it failed with error " +
                $"'{envelope.Error?.Code}': \"{envelope.Error?.Message}\".");
        }
    }

    /// <summary>Asserts that <paramref name="envelope"/> represents failure.</summary>
    /// <param name="envelope">The envelope to evaluate.</param>
    /// <exception cref="InvalidOperationException"><paramref name="envelope"/> is a success.</exception>
    public static void ShouldBeFailure(this EnvelopeNs.Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.IsSuccess)
            throw new InvalidOperationException("Expected envelope to be a failure, but it was successful.");
    }

    /// <summary>
    /// Asserts that <paramref name="envelope"/> represents success and returns its value.
    /// </summary>
    /// <typeparam name="T">The envelope's value type.</typeparam>
    /// <param name="envelope">The envelope to evaluate.</param>
    /// <returns><paramref name="envelope"/>'s value.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="envelope"/> is a failure.</exception>
    public static T ShouldBeSuccess<T>(this EnvelopeNs.Envelope<T> envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!envelope.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Expected envelope to be successful, but it failed with error " +
                $"'{envelope.Error?.Code}': \"{envelope.Error?.Message}\".");
        }

        return envelope.Value!;
    }

    /// <summary>
    /// Asserts that <paramref name="envelope"/> represents failure, optionally asserting the
    /// failure's <see cref="ErrorType"/>.
    /// </summary>
    /// <typeparam name="T">The envelope's value type.</typeparam>
    /// <param name="envelope">The envelope to evaluate.</param>
    /// <param name="expectedType">When supplied, the expected <see cref="Error.Type"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="envelope"/> is a success, or <paramref name="expectedType"/> was supplied
    /// and does not match the actual error type.
    /// </exception>
    public static void ShouldBeFailure<T>(this EnvelopeNs.Envelope<T> envelope, ErrorType? expectedType = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.IsSuccess)
            throw new InvalidOperationException("Expected envelope to be a failure, but it was successful.");

        if (expectedType is { } type && envelope.Error?.Type != type)
        {
            throw new InvalidOperationException(
                $"Expected error type '{type}' but found '{envelope.Error?.Type}'.");
        }
    }

    /// <summary>
    /// Asserts that <paramref name="envelope"/> represents failure with the specified error code.
    /// </summary>
    /// <typeparam name="T">The envelope's value type.</typeparam>
    /// <param name="envelope">The envelope to evaluate.</param>
    /// <param name="expectedCode">The expected <see cref="Error.Code"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="envelope"/> is a success, or its error code does not match
    /// <paramref name="expectedCode"/>.
    /// </exception>
    public static void ShouldHaveError<T>(this EnvelopeNs.Envelope<T> envelope, string expectedCode)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCode);

        if (envelope.IsSuccess)
            throw new InvalidOperationException("Expected envelope to be a failure, but it was successful.");

        if (envelope.Error?.Code != expectedCode)
        {
            throw new InvalidOperationException(
                $"Expected error code '{expectedCode}' but found '{envelope.Error?.Code}'.");
        }
    }
}
