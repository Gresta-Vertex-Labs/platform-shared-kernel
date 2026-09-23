using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace SharedKernel.Presentation.OpenApi.Documents;

/// <summary>Finds the metadata of the endpoint an MVC action is served by.</summary>
/// <remarks>
/// <para>
/// The API Explorer describes a minimal-API endpoint with a copy of the endpoint's metadata, but an MVC action with
/// the action's own attributes only: conventions applied to the endpoints — <c>MapControllers().RequirePermission(…)</c>
/// — are missing from <see cref="ActionDescriptor.EndpointMetadata"/>. The endpoint of an MVC action carries the
/// <see cref="ActionDescriptor"/> itself, which is how it is found here.
/// </para>
/// <para>
/// The map is built on first use and rebuilt when the endpoints change. Thread-safe: a concurrent rebuild builds the
/// same map twice.
/// </para>
/// </remarks>
internal sealed class EndpointMetadataLookup
{
    private readonly EndpointDataSource _endpoints;
    private Snapshot? _snapshot;

    public EndpointMetadataLookup(EndpointDataSource endpoints)
    {
        _endpoints = endpoints;
    }

    /// <summary>
    /// Returns the metadata of the endpoint that serves <paramref name="action"/>, or <see langword="null"/> when no
    /// endpoint carries it (a minimal-API endpoint, whose description already holds its metadata).
    /// </summary>
    public IReadOnlyList<object>? Find(ActionDescriptor action)
    {
        var snapshot = Volatile.Read(ref _snapshot);

        if (snapshot is null || snapshot.ChangeToken.HasChanged)
        {
            snapshot = new Snapshot(_endpoints);
            Volatile.Write(ref _snapshot, snapshot);
        }

        return snapshot.Metadata.GetValueOrDefault(action);
    }

    private sealed class Snapshot
    {
        public Snapshot(EndpointDataSource endpoints)
        {
            // The token is taken before the endpoints are read, so a change during the read is not missed.
            ChangeToken = endpoints.GetChangeToken();

            foreach (var endpoint in endpoints.Endpoints)
            {
                if (endpoint.Metadata.GetMetadata<ActionDescriptor>() is { } action)
                {
                    Metadata.TryAdd(action, endpoint.Metadata);
                }
            }
        }

        public IChangeToken ChangeToken { get; }

        public Dictionary<ActionDescriptor, IReadOnlyList<object>> Metadata { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
