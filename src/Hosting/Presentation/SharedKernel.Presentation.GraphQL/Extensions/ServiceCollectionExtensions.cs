using HotChocolate.Execution.Configuration;
using HotChocolate.Types.Pagination;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.GraphQL.Conventions;
using SharedKernel.Presentation.GraphQL.Errors;
using SharedKernel.Presentation.GraphQL.Options;

namespace SharedKernel.Presentation.GraphQL.Extensions;

/// <summary>
/// DI registration entry point for <c>SharedKernel.Presentation.GraphQL</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    // Sentinel service descriptor used for idempotency guard.
    private sealed class SharedKernelGraphQLRegistrationMarker;

    // Stateless — validates the just-constructed options instance directly, at the point of
    // consumption, since GraphQLOptions is never resolved via IOptions<GraphQLOptions>.Value here
    // (P-358/WO-056), mirroring RestClientOptionsValidator's/GrpcClientOptionsValidator's identical
    // validate-at-point-of-consumption pattern (see RestCommunicationBuilder.AddRestClient<TClient> /
    // GrpcCommunicationBuilder.AddGrpcClient<TClient>).
    private static readonly GraphQLOptionsValidator OptionsValidator = new();

    /// <summary>
    /// Registers platform-standard HotChocolate GraphQL conventions:
    /// snake_case operation naming, <c>SharedKernelFilterConvention</c>,
    /// offset and cursor pagination with <c>MaxPageSize</c> cap,
    /// <c>SharedKernelErrorFilter</c>, and the <c>AllowIntrospection</c> gate.
    /// </summary>
    /// <remarks>
    /// Must be called <b>before</b> any service-specific <c>AddGraphQLServer()</c> / <c>AddTypes()</c>
    /// calls. Idempotent — a second call returns the same builder and is otherwise a no-op.
    /// Registers the executor through HotChocolate's <c>AddGraphQLServer()</c>, so the ASP.NET Core
    /// server services <c>app.MapGraphQL()</c> resolves per request are present; <c>AddGraphQL()</c>
    /// alone registers only the executor and every HTTP request then fails.
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
            return services.AddGraphQLServer();
        }

        services.AddSingleton<SharedKernelGraphQLRegistrationMarker>();

        // Bind and validate GraphQLOptions.
        var options = new GraphQLOptions();
        configure?.Invoke(options);

        // Validate the just-constructed instance immediately after configure?.Invoke(options) and
        // before it is ever applied to services.Configure<GraphQLOptions>/ModifyPagingOptions/
        // DisableIntrospection — the registered IValidateOptions<GraphQLOptions> can never
        // structurally fire on its own, since this type is never resolved via
        // IOptions<GraphQLOptions>.Value (P-358/WO-056).
        var validationResult = OptionsValidator.Validate(name: null, options);
        if (validationResult.Failed)
        {
            // OptionsValidationException's optionsName parameter is non-nullable — GraphQLOptions has
            // no per-client name concept (unlike RestClientOptions/GrpcClientOptions), so string.Empty
            // is the faithful "no name" sentinel for this single, unnamed options instance.
            throw new OptionsValidationException(
                string.Empty,
                typeof(GraphQLOptions),
                validationResult.Failures);
        }

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
            .AddGraphQLServer()
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

        // Introspection gate, in both directions: AddGraphQLServer's default security policy disables introspection
        // outside the Development environment, so AllowIntrospection = true must re-enable it explicitly.
        builder = builder.DisableIntrospection(!options.AllowIntrospection);

        return builder;
    }
}
