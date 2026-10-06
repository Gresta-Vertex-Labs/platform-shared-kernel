using HotChocolate;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Shop.Catalog.Application.Products;
using Shop.Catalog.Application.Search;

namespace Shop.Catalog.Api;

/// <summary>The storefront's GraphQL queries. Each resolver sends the same use case as the REST endpoint.</summary>
public sealed class CatalogQueries
{
    public async Task<ProductDto> GetProductAsync(
        Guid id,
        [Service] ISender sender,
        CancellationToken cancellationToken
    ) => GraphResult.Unwrap(await sender.Send(new GetProductQuery(id), cancellationToken));

    public async Task<ProductSearchPage> SearchProductsAsync(
        string? text,
        string? category,
        [Service] ISender sender,
        CancellationToken cancellationToken
    ) =>
        GraphResult.Unwrap(
            await sender.Send(new SearchProductsQuery(text, category), cancellationToken)
        );
}

/// <summary>
/// Turns a failed <see cref="Result{T}"/> into a GraphQL error carrying the kernel's error code and the HTTP status the
/// REST boundary would have used (the <c>status</c> extension SharedKernelErrorFilter reads).
/// </summary>
internal static class GraphResult
{
    public static T Unwrap<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new GraphQLException(
                ErrorBuilder
                    .New()
                    .SetMessage(result.Error.Message)
                    .SetCode(result.Error.Code)
                    .SetExtension("status", StatusOf(result.Error.Type))
                    .Build()
            );

    private static int StatusOf(ErrorType type) =>
        type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };
}
