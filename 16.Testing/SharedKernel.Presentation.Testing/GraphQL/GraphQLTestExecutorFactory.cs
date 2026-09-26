using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Testing.Communication;

/// <summary>
/// Static factory wiring an <see cref="IRequestExecutorBuilder"/> with test-safe HotChocolate
/// defaults (introspection enabled, a generous max page size) for isolated GraphQL schema tests.
/// </summary>
/// <remarks>
/// This package takes no project reference to <c>SharedKernel.Presentation.GraphQL</c> (scope
/// lock) — it wires the equivalent test-safe defaults directly against the raw HotChocolate API
/// (<c>AddGraphQLServer()</c>) rather than calling <c>AddSharedKernelGraphQL()</c>. Tests that need
/// to verify the platform's actual conventions (snake_case filtering, <c>SharedKernelErrorFilter</c>,
/// etc.) belong in <c>SharedKernel.Presentation.GraphQL</c>'s own test suite, not here.
/// </remarks>
public static class GraphQLTestExecutorFactory
{
    /// <summary>
    /// Registers a HotChocolate GraphQL server on <paramref name="services"/> with introspection
    /// left enabled (HotChocolate's own default) and <c>MaxPageSize = 10</c> — test-safe defaults
    /// for isolated schema tests.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The <see cref="IRequestExecutorBuilder"/> for further schema configuration (query/mutation types, etc.).</returns>
    public static IRequestExecutorBuilder Create(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddGraphQLServer()
            .AddQueryableCursorPagingProvider()
            .ModifyPagingOptions(o =>
            {
                o.MaxPageSize = 10;
                o.DefaultPageSize = 10;
                o.IncludeTotalCount = true;
            });
    }
}
