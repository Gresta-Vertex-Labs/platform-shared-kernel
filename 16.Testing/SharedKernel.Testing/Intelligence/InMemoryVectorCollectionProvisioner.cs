using System.Collections.Concurrent;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.Testing.Intelligence;

/// <summary>
/// In-memory test double for <see cref="IVectorCollectionProvisioner"/>. Simulates behavioral
/// correctness (which collections were provisioned/cut-over/deleted) -- not provider timing or
/// transport faults.
/// </summary>
/// <remarks>
/// <para>
/// Non-generic; tracks its own <see cref="ConcurrentDictionary{TKey,TValue}"/> of registered
/// <see cref="VectorCollectionDefinition"/> keyed by collection name, independent of any
/// <see cref="InMemoryVectorCollection{TRecord}"/> instance -- see the non-coupling rationale below.
/// </para>
/// <para>
/// <see cref="EnsureCollectionAsync"/> is idempotent and additive-only (merges new
/// <see cref="VectorFieldDefinition"/> entries into an existing registration by name, never drops
/// one; a same-name field re-declared with a conflicting <see cref="VectorFieldDefinition.Kind"/>/
/// <see cref="VectorFieldDefinition.Filterable"/> returns
/// <c>IntelligenceErrors.CollectionDefinitionConflict</c>). <see cref="DeleteCollectionAsync"/> is
/// idempotent (an absent name still succeeds). <see cref="CutoverAsync"/> requires
/// <see cref="VectorCollectionCutoverRequest.StagingCollectionName"/> to be currently registered.
/// <see cref="ProbeAsync"/> returns a deterministic, always-healthy
/// <see cref="VectorCollectionHealth"/> for a registered collection, with <c>VectorCount</c> fixed
/// at zero since this fake tracks no document store of its own, and
/// <see cref="VectorCollectionHealth.PendingWriteCount"/> fixed at zero (not <see langword="null"/>)
/// -- a documented divergence from the real contract's "permanently nullable" honesty rule, since
/// this fake models no real optimizer backlog to report against at all.
/// </para>
/// <para>
/// <b>DELIBERATE NON-COUPLING BETWEEN ALL SIX <c>Intelligence/</c> FAKES</b> (the second application
/// of this pattern after <c>Search/</c>'s original three-type case): <see cref="InMemoryEmbeddingGenerator"/>,
/// <see cref="InMemoryVectorCollection{TRecord}"/>, <see cref="InMemoryVectorCollectionProvisioner"/>,
/// <see cref="InMemoryVectorProviderDescriptor"/>, <see cref="InMemorySemanticKernel"/>, and
/// <see cref="InMemoryCompletionProviderDescriptor"/> are six fully independent sealed fakes with no
/// constructor or type dependency on each other. A consuming test that wants two of these fakes to
/// agree on one <see cref="VectorCollectionDefinition"/> passes the same definition instance to each
/// explicitly -- never relies on implicit cross-fake state sharing.
/// </para>
/// </remarks>
public sealed class InMemoryVectorCollectionProvisioner : IVectorCollectionProvisioner
{
    private readonly ConcurrentDictionary<string, VectorCollectionDefinition> _collections = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets a value indicating whether write-path operations should simulate a provider
    /// rejection. When <see langword="true"/>, <see cref="EnsureCollectionAsync"/>,
    /// <see cref="DeleteCollectionAsync"/>, and <see cref="CutoverAsync"/> all return a failure
    /// instead of performing the operation. <see cref="CollectionExistsAsync"/> and
    /// <see cref="ProbeAsync"/> are unaffected.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>Gets the names of every collection currently registered against this instance.</summary>
    public IReadOnlyList<string> RegisteredCollectionNames => _collections.Keys.ToArray();

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result> EnsureCollectionAsync(
        VectorCollectionDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result.Failure(
                IntelligenceErrors.WriteRejected(definition.Name, "SimulateFailure enabled")));
        }

        if (!_collections.TryGetValue(definition.Name, out var existing))
        {
            _collections[definition.Name] = definition;
            return Task.FromResult(SharedKernel.Primitives.Results.Result.Success());
        }

        // Additive-only merge: every field in the incoming definition either matches an existing
        // field's role declaration exactly, or is a brand-new field name -- never a silent
        // conflicting-role rewrite.
        var mergedFields = new List<VectorFieldDefinition>(existing.Fields);
        foreach (var incoming in definition.Fields)
        {
            var matchIndex = mergedFields.FindIndex(f => string.Equals(f.Name, incoming.Name, StringComparison.Ordinal));
            if (matchIndex < 0)
            {
                mergedFields.Add(incoming);
                continue;
            }

            var current = mergedFields[matchIndex];
            if (current.Kind != incoming.Kind || current.Filterable != incoming.Filterable)
            {
                return Task.FromResult(SharedKernel.Primitives.Results.Result.Failure(
                    IntelligenceErrors.CollectionDefinitionConflict(definition.Name, incoming.Name)));
            }
        }

        _collections[definition.Name] = existing with { Fields = mergedFields };
        return Task.FromResult(SharedKernel.Primitives.Results.Result.Success());
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<bool>> CollectionExistsAsync(
        string collectionName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectionName);
        return Task.FromResult(SharedKernel.Primitives.Results.Result<bool>.Success(_collections.ContainsKey(collectionName)));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result> DeleteCollectionAsync(
        string collectionName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectionName);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result.Failure(
                IntelligenceErrors.WriteRejected(collectionName, "SimulateFailure enabled")));
        }

        // Idempotent -- an absent name still succeeds.
        _collections.TryRemove(collectionName, out _);
        return Task.FromResult(SharedKernel.Primitives.Results.Result.Success());
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result> CutoverAsync(
        VectorCollectionCutoverRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result.Failure(
                IntelligenceErrors.WriteRejected(request.LiveCollectionName, "SimulateFailure enabled")));
        }

        if (!_collections.TryGetValue(request.StagingCollectionName, out var stagingDefinition))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result.Failure(
                IntelligenceErrors.CutoverFailed(request.StagingCollectionName, request.LiveCollectionName, "staging collection not registered")));
        }

        _collections[request.LiveCollectionName] = stagingDefinition with { Name = request.LiveCollectionName };

        if (request.DeleteStagingAfterCutover
            && !string.Equals(request.StagingCollectionName, request.LiveCollectionName, StringComparison.Ordinal))
        {
            _collections.TryRemove(request.StagingCollectionName, out _);
        }

        return Task.FromResult(SharedKernel.Primitives.Results.Result.Success());
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<VectorCollectionHealth>> ProbeAsync(
        string collectionName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collectionName);

        if (!_collections.TryGetValue(collectionName, out var definition))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorCollectionHealth>.Failure(
                IntelligenceErrors.CollectionNotFound(collectionName)));
        }

        var health = new VectorCollectionHealth
        {
            Reachable = true,
            CollectionAddressable = true,
            Queryable = true,
            VectorCount = 0,
            PendingWriteCount = 0,
            EngineVersion = "in-memory-fake",
            SchemaFingerprint = definition.Fingerprint,
            Latency = TimeSpan.Zero,
        };

        return Task.FromResult(SharedKernel.Primitives.Results.Result<VectorCollectionHealth>.Success(health));
    }

    /// <summary>Clears every registered collection definition.</summary>
    public void Reset() => _collections.Clear();
}
