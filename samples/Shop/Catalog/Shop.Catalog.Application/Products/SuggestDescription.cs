using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Shop.Catalog.Domain;

namespace Shop.Catalog.Application.Products;

/// <summary>A marketing description written by the chat model, and what it cost.</summary>
public sealed record DescriptionSuggestion(string Text, int PromptTokens, int CompletionTokens);

/// <summary>Asks the chat model for a one-sentence marketing description of a product.</summary>
[RequirePermission(CatalogPermissions.Manage)]
public sealed record SuggestDescriptionQuery(Guid ProductId) : IQuery<DescriptionSuggestion>;

public sealed class SuggestDescriptionHandler(
    IReadRepository<Product, ProductId> products,
    ISemanticKernel kernel
) : IQueryHandler<SuggestDescriptionQuery, DescriptionSuggestion>
{
    public async Task<Result<DescriptionSuggestion>> Handle(
        SuggestDescriptionQuery query,
        CancellationToken cancellationToken
    )
    {
        var product = await products.GetByIdAsync(
            new ProductId(query.ProductId),
            cancellationToken
        );
        if (product is null)
        {
            return Result<DescriptionSuggestion>.Failure(
                CatalogMessages.ProductNotFound.ToError(ErrorType.NotFound, query.ProductId)
            );
        }

        var completion = await kernel.CompleteAsync(
            new CompletionRequest
            {
                Messages =
                [
                    new ChatMessage
                    {
                        Role = ChatRole.System,
                        Content =
                            "You write one short, factual marketing sentence for an online shop.",
                    },
                    new ChatMessage
                    {
                        Role = ChatRole.User,
                        Content =
                            $"Product: {product.Name} by {product.Brand}, category {product.Category}.",
                    },
                ],
                MaxOutputTokens = 60,
            },
            cancellationToken
        );
        if (completion.IsFailure)
        {
            return Result<DescriptionSuggestion>.Failure(completion.Error);
        }

        return Result<DescriptionSuggestion>.Success(
            new DescriptionSuggestion(
                completion.Value.Message.Content,
                completion.Value.TokenUsage.PromptTokens,
                completion.Value.TokenUsage.CompletionTokens
            )
        );
    }
}
