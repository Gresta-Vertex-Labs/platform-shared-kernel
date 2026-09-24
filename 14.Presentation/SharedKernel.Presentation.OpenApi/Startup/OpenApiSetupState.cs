using System.Collections.Concurrent;
using SharedKernel.Presentation.OpenApi.Routing;

namespace SharedKernel.Presentation.OpenApi.Startup;

/// <summary>
/// Registered once by <c>AddSharedKernelOpenApi()</c>, which it also marks as done; records the documents
/// <c>MapSharedKernelOpenApi()</c> serves outside the Development environment, for <see cref="OpenApiStartupDiagnostics"/>.
/// </summary>
internal sealed class OpenApiSetupState
{
    private readonly ConcurrentQueue<CompositeEndpointConventionBuilder> _exposedDocuments = new();

    /// <summary>
    /// Gets the builder <c>MapSharedKernelOpenApi()</c> returned for each mapping of the documents outside the
    /// Development environment.
    /// </summary>
    public IEnumerable<CompositeEndpointConventionBuilder> ExposedDocuments => _exposedDocuments;

    /// <summary>Records documents mapped outside the Development environment, with the builder returned for them.</summary>
    public void AddExposedDocuments(CompositeEndpointConventionBuilder documents) => _exposedDocuments.Enqueue(documents);
}
