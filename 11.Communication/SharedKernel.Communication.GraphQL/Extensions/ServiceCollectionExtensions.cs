using HotChocolate.Execution.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.GraphQL.Options;

namespace SharedKernel.Communication.GraphQL.Extensions;

/// <summary>
/// DI registration entry point for <c>SharedKernel.Communication.GraphQL</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string RegistrationMarkerKey = "SharedKernel.Communication.GraphQL.Registered";

    /// <summary>
    /// Registers platform-standard HotChocolate GraphQL conventions:
    /// snake_case naming, <c>SharedKernelFilterConvention</c>, offset and cursor pagination,
    /// <c>SharedKernelErrorFilter</c>, <c>MaxPageSize</c> cap, and <c>AllowIntrospection</c> gate.
    /// </summary>
    /// <remarks>
    /// Must be called <b>before</b> any service-specific <c>AddGraphQL()</c> / <c>AddTypes()</c> calls.
    /// Idempotent — a second call is a no-op.
    /// HotChocolate is NOT AOT-safe; do not add <c>&lt;IsAotCompatible&gt;true&lt;/IsAotCompatible&gt;</c>
    /// to any project that references this package.
    /// </remarks>
    /// <returns><see cref="IRequestExecutorBuilder"/> for further HotChocolate configuration.</returns>
    public static IRequestExecutorBuilder AddSharedKernelGraphQL(
        this IServiceCollection services,
        Action<GraphQLOptions>? configure = null)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
