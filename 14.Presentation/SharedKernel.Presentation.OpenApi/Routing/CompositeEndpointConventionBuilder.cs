using Microsoft.AspNetCore.Builder;

namespace SharedKernel.Presentation.OpenApi.Routing;

/// <summary>
/// Applies each convention to several endpoint builders at once, so one returned builder covers the documents and
/// the API reference. With no builders, conventions are accepted and ignored: there is nothing to apply them to.
/// </summary>
internal sealed class CompositeEndpointConventionBuilder : IEndpointConventionBuilder
{
    private readonly IEndpointConventionBuilder[] _builders;

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
    }

    /// <inheritdoc />
    public void Finally(Action<EndpointBuilder> finallyConvention)
    {
        ArgumentNullException.ThrowIfNull(finallyConvention);

        foreach (var builder in _builders)
        {
            builder.Finally(finallyConvention);
        }
    }
}
