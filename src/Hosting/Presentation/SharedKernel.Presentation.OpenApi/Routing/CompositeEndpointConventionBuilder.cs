using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace SharedKernel.Presentation.OpenApi.Routing;

/// <summary>
/// Applies each convention to several endpoint builders at once, so one returned builder covers the documents and
/// the API reference. With no builders, conventions are accepted and ignored: there is nothing to apply them to.
/// </summary>
/// <remarks>
/// The conventions are also kept, so what they add to the endpoints — authorization, for the startup check — can be
/// read before the endpoints are built, which the framework does on the first request.
/// </remarks>
internal sealed class CompositeEndpointConventionBuilder : IEndpointConventionBuilder
{
    private readonly IEndpointConventionBuilder[] _builders;
    private readonly List<Action<EndpointBuilder>> _conventions = [];
    private readonly List<Action<EndpointBuilder>> _finallyConventions = [];

    public CompositeEndpointConventionBuilder(params IEndpointConventionBuilder[] builders)
    {
        _builders = builders;
    }

    /// <inheritdoc />
    public void Add(Action<EndpointBuilder> convention)
    {
        ArgumentNullException.ThrowIfNull(convention);

        foreach (var builder in _builders)
        {
            builder.Add(convention);
        }

        _conventions.Add(convention);
    }

    /// <inheritdoc />
    public void Finally(Action<EndpointBuilder> finallyConvention)
    {
        ArgumentNullException.ThrowIfNull(finallyConvention);

        foreach (var builder in _builders)
        {
            builder.Finally(finallyConvention);
        }

        _finallyConventions.Add(finallyConvention);
    }

    /// <summary>
    /// Returns the metadata the conventions added so far put on an endpoint, read from a probe endpoint they are
    /// applied to — the conventions, then the finally conventions, each in the order added.
    /// </summary>
    /// <param name="services">The application services, which a convention may read from the endpoint builder.</param>
    /// <remarks>
    /// The framework applies a convention to every endpoint of a builder, and again whenever the endpoints are
    /// rebuilt, so applying it once more is what it is written for. A convention that fails on the probe, because it
    /// needs something only a real endpoint has, is skipped: what it would add stays unknown.
    /// </remarks>
    public IReadOnlyList<object> GetConventionMetadata(IServiceProvider services)
    {
        var probe = new RouteEndpointBuilder(requestDelegate: null, RoutePatternFactory.Parse("/"), order: 0)
        {
            ApplicationServices = services,
        };

        foreach (var convention in _conventions.Concat(_finallyConventions))
        {
            try
            {
                convention(probe);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Skipped, as documented: the endpoints themselves apply it when they are built.
            }
        }

        return [.. probe.Metadata];
    }
}
