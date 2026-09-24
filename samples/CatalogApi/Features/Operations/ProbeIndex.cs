using SharedKernel.Application;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace CatalogApi.Features.Operations;

/// <summary>
/// Readiness for one index on one named provider, as 13.ServiceDefaults' health check consumes it.
/// </summary>
/// <remarks>
/// The provider is addressed by key rather than guessed at. Iterating every registered provisioner and returning the
/// first that answers would "work" here and be wrong in principle: it would report an index as healthy because some
/// *other* engine happens to have one by the same name.
/// </remarks>
public sealed record ProbeIndex(string ProviderKey, string IndexName) : IQuery<SearchIndexHealth>;

public sealed class ProbeIndexHandler(IServiceProvider services) : IQueryHandler<ProbeIndex, SearchIndexHealth>
{
    public Task<Result<SearchIndexHealth>> Handle(ProbeIndex query, CancellationToken cancellationToken) =>
        ProvisionerFor(query.ProviderKey)
            .Bind(provisioner => provisioner.ProbeAsync(query.IndexName, cancellationToken));

    /// <summary>The provisioner registered under <paramref name="providerKey"/>, or a 404 naming the key.</summary>
    private Result<ISearchIndexProvisioner> ProvisionerFor(string providerKey) =>
        services.GetKeyedService<ISearchIndexProvisioner>(providerKey) is { } provisioner
            ? Result<ISearchIndexProvisioner>.Success(provisioner)
            : Error.NotFound("catalog.provider_not_registered", $"No search provider is registered under '{providerKey}'.");
}
