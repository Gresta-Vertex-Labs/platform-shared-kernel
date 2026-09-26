using SharedKernel.Application.Idempotency;
using System.Security.Cryptography;
using System.Text.Json;

namespace SharedKernel.Application.Pipeline.Idempotency;

/// <summary>
/// Computes a stable fingerprint of a request payload for <see cref="IdempotencyBehavior{TRequest,TResponse}"/>'s
/// duplicate-vs-reused-key detection.
/// </summary>
/// <remarks>
/// Only ever the fallback path — used when the request does not supply its own
/// <see cref="IIdempotentRequest.Fingerprint"/>.
/// </remarks>
internal static class RequestFingerprint
{
    /// <summary>
    /// Computes the lowercase-hex SHA-256 fingerprint of <paramref name="request"/>'s default JSON
    /// serialization.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="request"/> could not be serialized (an unserializable member, or a reference
    /// cycle) — never a raw <see cref="JsonException"/>.
    /// </exception>
    internal static string Compute<TRequest>(TRequest request)
    {
        byte[] bytes;
        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(request, typeof(TRequest));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Could not compute an automatic idempotency fingerprint for '{typeof(TRequest).FullName}' " +
                "via JSON serialization — it likely has an unserializable member or a reference cycle. " +
                "Implement IIdempotentRequest.Fingerprint on the command to supply an explicit fingerprint " +
                "instead of relying on automatic serialization.",
                ex);
        }

        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
