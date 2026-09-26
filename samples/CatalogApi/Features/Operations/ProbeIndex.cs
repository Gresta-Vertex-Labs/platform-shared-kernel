using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;

namespace CatalogApi.Features.Operations;

/// <summary>
/// Readiness for one index on one named provider: the same <see cref="IReadinessProbe"/> <c>/health/ready</c> runs.
/// </summary>
/// <remarks>
/// The provider is addressed by name rather than guessed at. Each provider registered one probe per index, named
/// <c>search-{provider}-{index}</c> (<see cref="SearchIndexReadinessProbe.ProbeNameFor"/>), so asking by that name can
/// never report an index healthy because some <em>other</em> engine happens to have one by the same name.
/// </remarks>
public sealed record ProbeIndex(string ProviderKey, string IndexName) : IQuery<ReadinessReport>;

public sealed class ProbeIndexHandler(IEnumerable<IReadinessProbe> probes) : IQueryHandler<ProbeIndex, ReadinessReport>
{
    public async Task<Result<ReadinessReport>> Handle(ProbeIndex query, CancellationToken cancellationToken)
    {
        var name = SearchIndexReadinessProbe.ProbeNameFor(query.ProviderKey, query.IndexName);
        var probe = probes.FirstOrDefault(p => p.Name == name);
        if (probe is null)
        {
            return Error.NotFound(
                "catalog.probe_not_registered",
                $"No readiness probe is registered for index '{query.IndexName}' on provider '{query.ProviderKey}'.");
        }

        return await probe.ProbeAsync(cancellationToken);
    }
}
