using System.Text.Json.Serialization.Metadata;

namespace SharedKernel.Contracts.Serialization;

/// <summary>
/// Provides the pre-configured STJ type-info resolver for all types in <c>SharedKernel.Contracts</c>.
/// </summary>
/// <remarks>
/// <para>
/// Consuming services add <see cref="TypeInfoResolver"/> to their
/// <c>JsonSerializerOptions.TypeInfoResolverChain</c> to enable AOT-safe deserialization of all
/// contracts types (<see cref="Pagination.PagedList{T}"/>, <see cref="Pagination.CursorPagedList{T}"/>,
/// <see cref="Envelopes.Envelope"/>, <see cref="Envelopes.Envelope{T}"/>,
/// <see cref="Events.EventEnvelope{TEvent}"/>).
/// </para>
/// <para>
/// Example:
/// <code>
/// var options = new JsonSerializerOptions();
/// options.TypeInfoResolverChain.Add(MyServiceJsonContext.Default);
/// options.TypeInfoResolverChain.Add(ContractsSerializerDefaults.TypeInfoResolver);
/// </code>
/// </para>
/// <para>
/// The concrete <see cref="ContractsJsonContext"/> remains <c>internal</c>. Never reference it
/// directly from consuming services. Use this class instead.
/// </para>
/// </remarks>
public static class ContractsSerializerDefaults
{
    /// <summary>
    /// Gets the <see cref="IJsonTypeInfoResolver"/> covering all types in <c>SharedKernel.Contracts</c>.
    /// Add this to <c>JsonSerializerOptions.TypeInfoResolverChain</c> in your service composition root.
    /// </summary>
    public static IJsonTypeInfoResolver TypeInfoResolver => ContractsJsonContext.Default;
}
