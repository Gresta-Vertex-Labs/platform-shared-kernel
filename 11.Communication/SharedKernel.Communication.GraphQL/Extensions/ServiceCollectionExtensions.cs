using HotChocolate.Execution.Configuration;
using HotChocolate.Types.Pagination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.GraphQL.Conventions;
using SharedKernel.Communication.GraphQL.Errors;
using SharedKernel.Communication.GraphQL.Options;

namespace SharedKernel.Communication.GraphQL.Extensions;

/// <summary>
/// DI registration entry point for <c>SharedKernel.Communication.GraphQL</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    // Sentinel service descriptor used for idempotency guard.
    private sealed class SharedKernelGraphQLRegistrationMarker;

    /// <summary>
    /// Registers platform-standard HotChocolate GraphQL conventions:
    /// snake_case operation naming, <c>SharedKernelFilterConvention</c>,
    /// offset and cursor pagination with <c>MaxPageSize</c> cap,
    /// <c>SharedKernelErrorFilter</c>, and the <c>AllowIntrospection</c> gate.
    /// </summary>
    /// <remarks>
    /// Must be called <b>before</b> any service-specific <c>AddGraphQL()</c> / <c>AddTypes()</c>
    /// calls. Idempotent — a second call returns the same builder and is otherwise a no-op.
    /// HotChocolate v16 is NOT AOT-safe — do not add
    /// <c>&lt;IsAotCompatible&gt;true&lt;/IsAotCompatible&gt;</c> to any project that references
    /// this package.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Optional delegate to override <see cref="GraphQLOptions"/> defaults.</param>
    /// <returns>
    /// <see cref="IRequestExecutorBuilder"/> for further HotChocolate configuration chaining.
    /// </returns>
    public static IRequestExecutorBuilder AddSharedKernelGraphQL(
        this IServiceCollection services,
        Action<GraphQLOptions>? configure = null)
    {
        // Idempotency guard: return early if already registered.
        if (services.Any(d => d.ServiceType == typeof(SharedKernelGraphQLRegistrationMarker)))
        {
            // Return the existing builder (already registered).
            return services.AddGraphQL();
        }

        services.AddSingleton<SharedKernelGraphQLRegistrationMarker>();

        // Bind and validate GraphQLOptions.
        var options = new GraphQLOptions();
        configure?.Invoke(options);

        services.Configure<GraphQLOptions>(o =>
        {
            o.EnableFiltering = options.EnableFiltering;
            o.EnableSorting = options.EnableSorting;
            o.EnablePaging = options.EnablePaging;
            o.MaxPageSize = options.MaxPageSize;
            o.AllowIntrospection = options.AllowIntrospection;
        });
        services.AddSingleton<IValidateOptions<GraphQLOptions>, GraphQLOptionsValidator>();

        // Build the IRequestExecutorBuilder with platform conventions.
        var builder = services
            .AddGraphQL()
            .AddErrorFilter<SharedKernelErrorFilter>();

        // Register SharedKernelFilterConvention as IFilterConvention.
        if (options.EnableFiltering)
        {
            builder = builder.AddFiltering<SharedKernelFilterConvention>();
        }

        // Register default sorting convention.
        if (options.EnableSorting)
        {
            builder = builder.AddSorting();
        }

        // Configure paging with MaxPageSize cap.
        if (options.EnablePaging)
        {
            builder = builder
                .AddQueryableCursorPagingProvider()
                .ModifyPagingOptions(o =>
                {
                    o.MaxPageSize = options.MaxPageSize;
                    o.DefaultPageSize = Math.Min(10, options.MaxPageSize);
                    o.IncludeTotalCount = true;
                });
        }

        // Introspection gate: disable when AllowIntrospection = false.
        if (!options.AllowIntrospection)
        {
            builder = builder.DisableIntrospection();
        }

        return builder;
    }
}
