using SharedKernel.Application;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;

namespace CatalogApi.Features.Operations;

/// <summary>One index's provisioning outcome; <paramref name="Error"/> is <see langword="null"/> when it succeeded.</summary>
public sealed record ProvisionOutcome(string Provider, string Index, Error? Error);

/// <summary>
/// Creates what is missing — idempotent and additive-only. Run it on every deploy; it reports a conflict rather than
/// silently rewriting an incompatible mapping or analysis chain. The command succeeds with an outcome per index; an
/// index that failed is one outcome of the report, not a failure of the command.
/// </summary>
public sealed record ProvisionIndexes : ICommand<IReadOnlyList<ProvisionOutcome>>;

public sealed class ProvisionIndexesHandler(
    IEnumerable<ISearchIndexProvisioner> provisioners,
    IEnumerable<ISearchProviderDescriptor> descriptors)
    : ICommandHandler<ProvisionIndexes, IReadOnlyList<ProvisionOutcome>>
{
    public async Task<Result<IReadOnlyList<ProvisionOutcome>>> Handle(ProvisionIndexes command, CancellationToken cancellationToken)
    {
        // Two providers are registered, so ISearchIndexProvisioner resolves twice. A real service
        // registers one; this sample deliberately runs both engines side by side, which is legal
        // precisely because they serve different document types.
        //
        // Each provisioner is given only ITS OWN index. Handing every definition to every
        // provisioner would create a "products" index on ElasticSearch and an "order-lines-read"
        // index on Meilisearch that nothing ever reads — and would make a conflict on one engine
        // look like a conflict on both. ISearchProviderDescriptor.RegisteredIndexes is what says
        // which indexes a given provider was actually configured for.
        var outcomes = new List<ProvisionOutcome>();
        foreach (var (provisioner, descriptor) in provisioners.Zip(descriptors))
        {
            foreach (var definition in Definitions.All.Where(d => descriptor.RegisteredIndexes.Contains(d.Name)))
            {
                var result = await provisioner.EnsureIndexAsync(definition, cancellationToken);
                outcomes.Add(new ProvisionOutcome(descriptor.ProviderName, definition.Name, result.IsFailure ? result.Error : null));
            }
        }

        return Result<IReadOnlyList<ProvisionOutcome>>.Success(outcomes);
    }
}
