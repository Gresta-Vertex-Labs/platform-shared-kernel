namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Zero-member marker interface for domain services — stateless domain logic that does not
/// naturally belong to a single aggregate or value object.
/// </summary>
/// <remarks>
/// Domain services should be pure in the domain sense: no I/O, no infrastructure concerns.
/// They coordinate between aggregates or enforce cross-aggregate invariants.
/// </remarks>
public interface IDomainService
{
}
