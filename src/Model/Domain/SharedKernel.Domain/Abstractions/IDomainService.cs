namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marker for a domain service: stateless domain logic that spans several aggregates or value objects
/// and belongs to none of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Extend <see cref="SharedKernel.Domain.DomainServices.DomainService"/> rather than
/// implementing this interface directly; an architecture rule checks it.
/// </para>
/// <para>
/// <b>Purity.</b> A domain service performs no I/O. It receives the domain objects it works on, and the
/// application layer loads and saves them.
/// </para>
/// </remarks>
public interface IDomainService
{
}
