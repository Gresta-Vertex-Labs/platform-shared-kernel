using Qdrant.Client;

namespace SharedKernel.AI.Qdrant.Raw;

/// <summary>
/// The last-resort escape hatch onto the underlying <see cref="IQdrantClient"/> — registered only when
/// the composition root explicitly opts in via <c>AllowRawClientAccess()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE RAW CLIENT BYPASSES TENANT SCOPING.</b> Tenant-scope injection happens inside
/// <c>QdrantVectorCollection{TRecord}</c>'s own translation path; a call made directly against
/// <see cref="Client"/> receives none of it. A multi-tenant service using this accessor must apply its
/// own tenant predicate.
/// </para>
/// <para>
/// This is a Qdrant-exclusive contract declared only in this package — referencing it takes a
/// compile-time dependency on <c>SharedKernel.AI.Qdrant</c>, never <c>SharedKernel.AI.Abstractions</c>.
/// </para>
/// </remarks>
public interface IQdrantRawClientAccessor
{
    /// <summary>Gets the raw <see cref="IQdrantClient"/> instance. THE RAW CLIENT BYPASSES TENANT SCOPING.</summary>
    IQdrantClient Client { get; }
}
