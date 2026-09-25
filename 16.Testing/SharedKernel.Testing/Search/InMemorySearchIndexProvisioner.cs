using System.Collections.Concurrent;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Testing.Search;

/// <summary>
/// In-memory test double for <see cref="ISearchIndexProvisioner"/>. Simulates behavioral
/// correctness (which indexes were provisioned/cut-over/deleted) -- not provider timing or
/// transport faults.
/// </summary>
/// <remarks>
/// <para>
/// Non-generic; tracks its own <see cref="ConcurrentDictionary{TKey,TValue}"/> of registered
/// <see cref="SearchIndexDefinition"/> keyed by index name, independent of any
/// <see cref="InMemorySearchIndex{TDocument}"/> instance -- see that type's remarks for the full
/// deliberate non-coupling rationale shared by all three <c>Search/</c> fakes.
/// </para>
/// <para>
/// <see cref="EnsureIndexAsync"/> is idempotent and additive-only, matching the real contract's own
/// "never drops a field" rule. <see cref="ProbeAsync"/> reports a deterministic, always-healthy
/// <see cref="SearchIndexHealth"/> for a registered index, with <c>DocumentCount</c> fixed at zero
/// since this fake tracks no live document store of its own.
/// </para>
/// </remarks>
public sealed class InMemorySearchIndexProvisioner : ISearchIndexProvisioner
{
    private readonly ConcurrentDictionary<string, SearchIndexDefinition> _indexes = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets a value indicating whether write-path operations should simulate a provider
    /// rejection. When <see langword="true"/>, <see cref="EnsureIndexAsync"/>,
    /// <see cref="DeleteIndexAsync"/>, and <see cref="CutoverAsync"/> all return a failure instead
    /// of performing the operation. <see cref="IndexExistsAsync"/> and <see cref="ProbeAsync"/> are
    /// unaffected, mirroring <see cref="InMemorySearchIndex{TDocument}.SimulateFailure"/>'s
    /// read-path exclusion.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>Gets the names of every index currently registered against this instance.</summary>
    public IReadOnlyList<string> RegisteredIndexNames => _indexes.Keys.ToArray();

    /// <inheritdoc />
    public Task<Result> EnsureIndexAsync(SearchIndexDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (SimulateFailure)
        {
            return Task.FromResult(Result.Failure(SearchErrors.WriteRejected(definition.Name, "SimulateFailure enabled")));
        }

        if (!_indexes.TryGetValue(definition.Name, out var existing))
        {
            _indexes[definition.Name] = definition;
            return Task.FromResult(Result.Success());
        }

        // Additive-only merge: every field in the incoming definition either matches an existing
        // field's role declaration exactly, or is a brand-new field name -- never a silent
        // conflicting-role rewrite, matching the real contract's own "never drops a field" rule.
        var mergedFields = new List<SearchFieldDefinition>(existing.Fields);
        foreach (var incomingField in definition.Fields)
        {
            var matchIndex = mergedFields.FindIndex(f => string.Equals(f.Name, incomingField.Name, StringComparison.Ordinal));
            if (matchIndex < 0)
            {
                mergedFields.Add(incomingField);
                continue;
            }

            if (!FieldRolesMatch(mergedFields[matchIndex], incomingField))
            {
                return Task.FromResult(Result.Failure(SearchErrors.IndexDefinitionConflict(definition.Name, incomingField.Name)));
            }
        }

        _indexes[definition.Name] = existing with { Fields = mergedFields };
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<bool>> IndexExistsAsync(string indexName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexName);
        return Task.FromResult(SharedKernel.Primitives.Results.Result<bool>.Success(_indexes.ContainsKey(indexName)));
    }

    /// <inheritdoc />
    public Task<Result> DeleteIndexAsync(string indexName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexName);

        if (SimulateFailure)
        {
            return Task.FromResult(Result.Failure(SearchErrors.WriteRejected(indexName, "SimulateFailure enabled")));
        }

        // Idempotent -- an absent name still succeeds, mirroring Storage/InMemoryFileStorage.DeleteAsync.
        _indexes.TryRemove(indexName, out _);
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result> CutoverAsync(IndexCutoverRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (SimulateFailure)
        {
            return Task.FromResult(Result.Failure(SearchErrors.WriteRejected(request.LiveIndexName, "SimulateFailure enabled")));
        }

        if (!_indexes.TryGetValue(request.StagingIndexName, out var stagingDefinition))
        {
            return Task.FromResult(Result.Failure(
                SearchErrors.CutoverFailed(request.StagingIndexName, request.LiveIndexName, "staging index not registered")));
        }

        _indexes[request.LiveIndexName] = stagingDefinition with { Name = request.LiveIndexName };

        if (request.DeleteStagingAfterCutover && !string.Equals(request.StagingIndexName, request.LiveIndexName, StringComparison.Ordinal))
        {
            _indexes.TryRemove(request.StagingIndexName, out _);
        }

        return Task.FromResult(Result.Success());
    }

    /// <summary>
    /// Measures a registered index — always healthy, deterministically. Pass it to a
    /// <see cref="SearchIndexReadinessProbe"/> to exercise readiness handling without an engine.
    /// </summary>
    public Task<SharedKernel.Primitives.Results.Result<SearchIndexHealth>> ProbeAsync(string indexName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(indexName);

        if (!_indexes.TryGetValue(indexName, out var definition))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchIndexHealth>.Failure(SearchErrors.IndexNotFound(indexName)));
        }

        var health = new SearchIndexHealth
        {
            Reachable = true,
            IndexAddressable = true,
            Searchable = true,
            DocumentCount = 0,
            PendingWriteCount = 0,
            EngineVersion = "in-memory-fake",
            SchemaFingerprint = definition.ComputeFingerprint(),
            Latency = TimeSpan.Zero,
        };

        return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchIndexHealth>.Success(health));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always succeeds once every index has been provisioned through <see cref="EnsureIndexAsync"/>,
    /// because this fake stores the definition it was given and therefore cannot drift from it. Its
    /// value in a consuming service's tests is proving that the verification call is wired into that
    /// service's startup path at all — real drift is only observable against a real engine.
    /// </remarks>
    public Task<Result> VerifyRegisteredIndexesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success());

    /// <summary>Clears every registered index definition.</summary>
    public void Reset() => _indexes.Clear();

    private static bool FieldRolesMatch(SearchFieldDefinition existing, SearchFieldDefinition incoming) =>
        existing.Kind == incoming.Kind
        && existing.Searchable == incoming.Searchable
        && existing.Filterable == incoming.Filterable
        && existing.Sortable == incoming.Sortable
        && existing.Facetable == incoming.Facetable;
}
