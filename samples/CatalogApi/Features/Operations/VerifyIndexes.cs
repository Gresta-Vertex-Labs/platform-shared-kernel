using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;

namespace CatalogApi.Features.Operations;

/// <summary>
/// The deployment check: does every live index still match what this build declares? Catches the quiet failure where
/// code ships declaring a field, synonym or stop word the index was never rebuilt for, so filters silently match
/// nothing while the index looks perfectly healthy. Answers one entry per provider — the mismatch's error, or
/// <see langword="null"/> — rather than one error.
/// </summary>
public sealed record VerifyIndexes : IQuery<IReadOnlyList<Error?>>;

public sealed class VerifyIndexesHandler(IEnumerable<ISearchIndexProvisioner> provisioners)
    : IQueryHandler<VerifyIndexes, IReadOnlyList<Error?>>
{
    public async Task<Result<IReadOnlyList<Error?>>> Handle(VerifyIndexes query, CancellationToken cancellationToken)
    {
        var outcomes = new List<Error?>();
        foreach (var provisioner in provisioners)
        {
            var result = await provisioner.VerifyRegisteredIndexesAsync(cancellationToken);
            outcomes.Add(result.IsFailure ? result.Error : null);
        }

        return Result<IReadOnlyList<Error?>>.Success(outcomes);
    }
}
