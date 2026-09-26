using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Collections;
using SharedKernel.AI.Qdrant.Diagnostics;
using SharedKernel.AI.Qdrant.Logging;
using SharedKernel.AI.Qdrant.Options;
using SharedKernel.AI.Qdrant.Provisioning;
using SharedKernel.AI.Qdrant.Quantization;
using SharedKernel.AI.Qdrant.Raw;
using SharedKernel.AI.Qdrant.Sparse;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Health;

namespace SharedKernel.AI.Qdrant.Extensions;

/// <summary>
/// The fluent builder returned by <c>AddSharedKernelQdrant</c> — registers collections, and opts in to
/// raw client access.
/// </summary>
public sealed class QdrantBuilder
{
    private readonly IServiceCollection _services;
    private readonly Dictionary<string, VectorCollectionDefinition> _collectionDefinitions = new(StringComparer.Ordinal);
    private bool _rawClientAccessAllowed;

    internal QdrantBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>Gets the number of collections registered against this builder so far.</summary>
    internal int RegisteredCollectionCount => _collectionDefinitions.Count;

    /// <summary>
    /// Registers a collection for <typeparamref name="TRecord"/> — scoped
    /// <see cref="IVectorCollection{TRecord}"/> and <see cref="IQdrantHybridQueryAccessor{TRecord}"/>.
    /// </summary>
    public QdrantBuilder AddCollection<TRecord>(string collectionName, Action<VectorCollectionDefinitionBuilder> configure)
        where TRecord : class, IVectorRecord
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ArgumentNullException.ThrowIfNull(configure);

        var definitionBuilder = new VectorCollectionDefinitionBuilder(collectionName);
        configure(definitionBuilder);
        var definitionResult = definitionBuilder.Build();
        if (definitionResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to build the VectorCollectionDefinition for Qdrant collection '{collectionName}': {definitionResult.Error.Message}");
        }

        var definition = definitionResult.Value;
        _collectionDefinitions[collectionName] = definition;

        _services.AddScoped<IVectorCollection<TRecord>>(sp => new QdrantVectorCollection<TRecord>(
            sp.GetRequiredService<IQdrantClient>(),
            definition,
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<QdrantVectorCollection<TRecord>>>()));

        _services.AddScoped<IQdrantHybridQueryAccessor<TRecord>>(sp => new QdrantHybridQueryAccessor<TRecord>(
            sp.GetRequiredService<IQdrantClient>(),
            definition,
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<QdrantHybridQueryAccessor<TRecord>>>()));

        return this;
    }

    /// <summary>
    /// Opts in to the last-resort raw client escape hatch (<see cref="IQdrantRawClientAccessor"/>).
    /// Logs a startup warning. <b>The raw client bypasses tenant scoping.</b>
    /// </summary>
    public QdrantBuilder AllowRawClientAccess()
    {
        _rawClientAccessAllowed = true;
        return this;
    }

    /// <summary>Finalizes registration and returns the underlying <see cref="IServiceCollection"/>.</summary>
    public IServiceCollection Build()
    {
        var collections = new Dictionary<string, VectorCollectionDefinition>(_collectionDefinitions, StringComparer.Ordinal);

        // Registered via a factory rather than AddSingleton<TInterface, TImplementation>() because
        // QdrantCollectionProvisioner's constructor does not take a raw QdrantOptions and needs none —
        // QdrantProviderDescriptor's constructor DOES take primitives derived from IOptions<QdrantOptions>,
        // which AddValidatedOptions only ever registers wrapped, never unwrapped.
        _services.AddSingleton(sp => new QdrantCollectionProvisioner(
            sp.GetRequiredService<QdrantClient>(),
            sp.GetRequiredService<ILogger<QdrantCollectionProvisioner>>()));
        _services.AddSingleton<IVectorCollectionProvisioner>(sp => sp.GetRequiredService<QdrantCollectionProvisioner>());

        // One readiness probe per registered collection, each over the one provisioner instance above.
        foreach (var collectionName in collections.Keys)
        {
            _services.AddReadinessProbe(sp =>
            {
                var provisioner = sp.GetRequiredService<QdrantCollectionProvisioner>();
                return new VectorCollectionReadinessProbe(
                    IntelligenceWellKnown.QdrantProviderName, collectionName, provisioner.ProbeAsync);
            });
        }

        _services.AddSingleton<IVectorProviderDescriptor>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
            return new QdrantProviderDescriptor(collections, options.MaxBatchSize, options.MaxVectorDimension, options.MaxFilterDepth);
        });

        _services.AddSingleton<IQdrantQuantizationProfileAccessor>(sp =>
            new QdrantQuantizationProfileAccessor(sp.GetRequiredService<IQdrantClient>()));

        if (_rawClientAccessAllowed)
        {
            _services.AddSingleton<IQdrantRawClientAccessor>(sp =>
            {
                sp.GetRequiredService<ILogger<QdrantRawClientAccessor>>().QdrantRawClientAccessEnabled();
                return new QdrantRawClientAccessor(sp.GetRequiredService<IQdrantClient>());
            });
        }

        return _services;
    }
}
