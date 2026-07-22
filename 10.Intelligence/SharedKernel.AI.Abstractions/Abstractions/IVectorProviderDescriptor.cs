using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>
/// A singleton, zero-I/O descriptor of the active vector provider's identity and ceilings, plus a
/// zero-I/O pre-flight validator for <see cref="VectorQuery"/> shapes.
/// </summary>
/// <remarks>
/// <para>
/// <b>No capability-flags enum, deliberately:</b> an <c>if (caps.HasFlag(...))</c> branch at an
/// application call site is a platform violation here specifically because degradation in similarity
/// search is confidently wrong rows, not merely fewer rows. The consumer-facing capability mechanism is
/// the compile error a provider swap produces against provider-package-declared exclusive contracts,
/// never a runtime flag.
/// </para>
/// <para>
/// <b><see cref="Validate"/> is the zero-I/O, zero-container pre-flight:</b> checks
/// <see cref="MaxFilterDepth"/> against the query's <see cref="VectorFilter"/> tree depth,
/// <see cref="MaxVectorDimension"/> against <see cref="VectorQuery.Vector"/>'s length, and structural
/// request invariants — without touching the network. It does not check model-identity/dimension
/// against a specific collection's definition (that check needs the definition, which the descriptor
/// does not hold) — that remains <see cref="IVectorCollection{TRecord}"/>'s own responsibility at call
/// time.
/// </para>
/// </remarks>
public interface IVectorProviderDescriptor
{
    /// <summary>Gets the active provider's name.</summary>
    string ProviderName { get; }

    /// <summary>Gets the active provider's maximum batch size for a single bulk write.</summary>
    int MaxBatchSize { get; }

    /// <summary>Gets the active provider's maximum vector dimension.</summary>
    int MaxVectorDimension { get; }

    /// <summary>Gets the active provider's maximum <see cref="VectorFilter"/> tree depth.</summary>
    int MaxFilterDepth { get; }

    /// <summary>Gets the names of every collection registered against this provider.</summary>
    IReadOnlyList<string> RegisteredCollections { get; }

    /// <summary>
    /// Validates <paramref name="query"/> against the collection named <paramref name="collectionName"/>
    /// — a zero-I/O structural pre-flight, not a model-identity/dimension check.
    /// </summary>
    Result Validate(string collectionName, VectorQuery query);
}
