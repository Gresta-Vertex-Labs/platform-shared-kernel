using System.Collections.Concurrent;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.Testing.Intelligence;

/// <summary>
/// In-memory test double for <see cref="IVectorProviderDescriptor"/>. Simulates the zero-I/O
/// provider-identity/ceiling surface and pre-flight <see cref="VectorQuery"/> validation, without a
/// real engine behind it.
/// </summary>
/// <remarks>
/// <para>
/// Keeps its own independent <see cref="VectorCollectionDefinition"/> map, populated via
/// <see cref="RegisterCollection"/> -- it is not read through <see cref="InMemoryVectorCollectionProvisioner"/>
/// or any <see cref="InMemoryVectorCollection{TRecord}"/> instance. See
/// <see cref="InMemoryVectorCollectionProvisioner"/>'s remarks for the full deliberate non-coupling
/// rationale shared by all six <c>Intelligence/</c> fakes.
/// </para>
/// <para>
/// <see cref="Validate"/> is a zero-I/O structural pre-flight only -- <see cref="MaxFilterDepth"/>
/// against the query's <see cref="VectorFilter"/> tree depth, and <see cref="MaxVectorDimension"/>
/// against <see cref="VectorQuery.Vector"/>'s length. It never checks model-identity/dimension
/// against a specific collection's definition -- that remains
/// <see cref="InMemoryVectorCollection{TRecord}"/>'s own responsibility at call time.
/// </para>
/// </remarks>
public sealed class InMemoryVectorProviderDescriptor : IVectorProviderDescriptor
{
    private readonly ConcurrentDictionary<string, VectorCollectionDefinition> _registeredCollections = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new <see cref="InMemoryVectorProviderDescriptor"/>.
    /// </summary>
    /// <param name="providerName">
    /// The provider name to report. Defaults to <c>"in-memory-fake"</c> -- deliberately neither
    /// <c>IntelligenceWellKnown.QdrantProviderName</c> nor <c>.MilvusProviderName</c>, so a
    /// consumer's own provider-name branching/logging code cannot mistake this fake for a real
    /// engine.
    /// </param>
    public InMemoryVectorProviderDescriptor(string providerName = "in-memory-fake")
    {
        ArgumentNullException.ThrowIfNull(providerName);
        ProviderName = providerName;
    }

    /// <inheritdoc />
    public string ProviderName { get; set; }

    /// <inheritdoc cref="IVectorProviderDescriptor.MaxBatchSize" />
    public int MaxBatchSize { get; set; } = 1000;

    /// <inheritdoc cref="IVectorProviderDescriptor.MaxVectorDimension" />
    public int MaxVectorDimension { get; set; } = 4096;

    /// <inheritdoc cref="IVectorProviderDescriptor.MaxFilterDepth" />
    public int MaxFilterDepth { get; set; } = 10;

    /// <inheritdoc />
    public IReadOnlyList<string> RegisteredCollections => _registeredCollections.Keys.ToArray();

    /// <summary>
    /// Registers <paramref name="definition"/> under <paramref name="collectionName"/> -- a
    /// test-setup helper populating both <see cref="RegisteredCollections"/> and the definition
    /// knowledge <see cref="Validate"/> needs.
    /// </summary>
    public void RegisterCollection(string collectionName, VectorCollectionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(collectionName);
        ArgumentNullException.ThrowIfNull(definition);
        _registeredCollections[collectionName] = definition;
    }

    /// <inheritdoc />
    public SharedKernel.Primitives.Results.Result Validate(string collectionName, VectorQuery query)
    {
        ArgumentNullException.ThrowIfNull(collectionName);
        ArgumentNullException.ThrowIfNull(query);

        if (!_registeredCollections.ContainsKey(collectionName))
        {
            return SharedKernel.Primitives.Results.Result.Failure(IntelligenceErrors.CollectionNotFound(collectionName));
        }

        if (query.Vector.Length > MaxVectorDimension)
        {
            return SharedKernel.Primitives.Results.Result.Failure(IntelligenceErrors.InvalidQuery(
                $"Query vector dimension {query.Vector.Length} exceeds provider '{ProviderName}''s MaxVectorDimension ceiling of {MaxVectorDimension}."));
        }

        var depth = FilterDepth(query.Filter);
        if (depth > MaxFilterDepth)
        {
            return SharedKernel.Primitives.Results.Result.Failure(IntelligenceErrors.FilterDepthExceeded(depth, MaxFilterDepth));
        }

        return SharedKernel.Primitives.Results.Result.Success();
    }

    /// <summary>Clears every registered collection definition.</summary>
    public void Reset() => _registeredCollections.Clear();

    private static int FilterDepth(VectorFilter? filter) => filter switch
    {
        null => 0,
        EqualFilter => 1,
        NotEqualFilter => 1,
        InFilter => 1,
        RangeFilter => 1,
        ExistsFilter => 1,
        AndFilter f => 1 + (f.Operands.Count == 0 ? 0 : f.Operands.Max(FilterDepth)),
        OrFilter f => 1 + (f.Operands.Count == 0 ? 0 : f.Operands.Max(FilterDepth)),
        NotFilter f => 1 + FilterDepth(f.Operand),
    };
}
