using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;

namespace CatalogApi.Features.Operations;

/// <summary>One index's deletion outcome.</summary>
public sealed record IndexDeletion(string Provider, string Index, bool Ok, string? Error);

/// <summary>
/// Drops every registered index so the sample can be re-provisioned from scratch — a convenience for exploring it, and
/// the manual stand-in for the staging -> bulk-load -> CutoverAsync rebuild a real service performs when a mapping,
/// synonym or stop-word list has to change. Both providers refuse to change those in place, on purpose.
/// </summary>
public sealed record DeleteIndexes : ICommand<IReadOnlyList<IndexDeletion>>;

public sealed class DeleteIndexesHandler(
    IEnumerable<ISearchIndexProvisioner> provisioners,
    IEnumerable<ISearchProviderDescriptor> descriptors)
    : ICommandHandler<DeleteIndexes, IReadOnlyList<IndexDeletion>>
{
    public async Task<Result<IReadOnlyList<IndexDeletion>>> Handle(DeleteIndexes command, CancellationToken cancellationToken)
    {
        var outcomes = new List<IndexDeletion>();
        foreach (var (provisioner, descriptor) in provisioners.Zip(descriptors))
        {
            foreach (var indexName in descriptor.RegisteredIndexes)
            {
                var result = await provisioner.DeleteIndexAsync(indexName, cancellationToken);
                outcomes.Add(new IndexDeletion(descriptor.ProviderName, indexName, result.IsSuccess, result.IsFailure ? result.Error.Code : null));
            }
        }

        return Result<IReadOnlyList<IndexDeletion>>.Success(outcomes);
    }
}
