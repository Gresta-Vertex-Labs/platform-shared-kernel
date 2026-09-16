using SharedKernel.Cryptography;
using SharedKernel.Security.ApiKey.Validation;

namespace SharedKernel.Security.ApiKey.Samples;

/// <summary>
/// A worked, NON-PRODUCTION example of an <see cref="IApiKeyValidator"/> that accepts more than one
/// simultaneously-active key per client during a rotation window, using
/// <see cref="FixedTimeComparison.AreEqualToAny(string, IEnumerable{string})"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS TYPE IS A DOCUMENTATION RECIPE, NOT A SHIPPED PRODUCTION IMPLEMENTATION.</b> It exists to back
/// this package's README rotation-window recipe with real, compiled, exercised code rather than
/// hand-typed prose. <c>SharedKernel.Security.ApiKey</c>'s deliberate philosophy is that it never
/// dictates key storage — a real <see cref="IApiKeyValidator"/> implementation is always the consuming
/// service's own, backed by whatever storage it already uses (database, secret store, configuration).
/// This sample's in-memory <see cref="_clientKeys"/> map is a stand-in for that storage, not a suggestion
/// to store API keys in process memory.
/// </para>
/// <para>
/// <b>The pattern this sample demonstrates:</b> during a key-rotation grace window, a client may hold
/// EITHER its current key or a not-yet-retired previous key. A single comparison against "the" valid
/// key cannot express this — there are multiple simultaneously valid candidates.
/// <see cref="FixedTimeComparison.AreEqualToAny(string, IEnumerable{string})"/> compares the presented key
/// against every candidate, whichever one matches, so elapsed comparison time never reveals which
/// candidate (or how many) matched.
/// </para>
/// </remarks>
internal sealed class RotationWindowApiKeyValidatorSample : IApiKeyValidator
{
    // Stand-in for a real store (database table, secret manager, ...) mapping a client identifier to
    // every currently-active key for that client — typically the current key plus, during a bounded
    // rotation grace window, one not-yet-retired previous key. A real implementation should store a
    // hash of each key (e.g. via 01.Core/SharedKernel.Cryptography's IOneWayHasher) rather than the
    // plaintext key value.
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _clientKeys;

    /// <summary>
    /// Initializes a new instance of <see cref="RotationWindowApiKeyValidatorSample"/> for demonstration
    /// purposes.
    /// </summary>
    /// <param name="clientKeys">
    /// A map of client identifier to every currently-active key for that client. In a real
    /// implementation this is loaded from the consuming service's own storage, never hardcoded.
    /// </param>
    internal RotationWindowApiKeyValidatorSample(IReadOnlyDictionary<string, IReadOnlyList<string>> clientKeys)
    {
        ArgumentNullException.ThrowIfNull(clientKeys);
        _clientKeys = clientKeys;
    }

    /// <inheritdoc/>
    public Task<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presentedKey);

        // A real implementation looks up candidates by the client identifier encoded in (or otherwise
        // derivable from) the presented key — e.g. a "{clientId}.{secret}" key shape, or a lookup table
        // keyed by a non-secret prefix. This sample simply evaluates every client's candidate set, since
        // it has no such structure to exploit; a production implementation should narrow the candidate
        // set to the specific client being authenticated wherever possible.
        foreach (var (clientId, candidates) in _clientKeys)
        {
            if (FixedTimeComparison.AreEqualToAny(presentedKey, candidates))
            {
                return Task.FromResult(ApiKeyValidationResult.Valid(clientId: clientId));
            }
        }

        return Task.FromResult(ApiKeyValidationResult.Invalid);
    }
}
