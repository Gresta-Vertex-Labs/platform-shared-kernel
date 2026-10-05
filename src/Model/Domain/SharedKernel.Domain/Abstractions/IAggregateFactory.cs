namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marker for a class that creates one aggregate root type, used when creation needs collaborators the
/// aggregate itself should not depend on.
/// </summary>
/// <typeparam name="TAggregateRoot">The aggregate root type the factory creates.</typeparam>
/// <typeparam name="TId">The identity key type of that aggregate. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <b>Usage.</b> Write a factory when creation needs something outside the aggregate, such as an
/// identifier generator or a uniqueness check. When the aggregate can build itself, give it a static
/// <c>Create</c> method that calls <c>TryCreate</c> instead; it needs no factory.
/// </para>
/// <para>
/// <b>Convention.</b> The interface has no members, because every aggregate has a different creation
/// signature. Architecture rules find factories through it and require a public <c>Create</c> method
/// that returns <see cref="SharedKernel.Primitives.Results.ValidationResult{T}"/> of
/// <typeparamref name="TAggregateRoot"/>. Report invalid input as a failed result, never by throwing.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class OrderFactory(IIdGenerator ids, IClock clock) : IAggregateFactory&lt;Order, OrderId&gt;
/// {
///     public ValidationResult&lt;Order&gt; Create(string customerName) =&gt;
///         Order.Create(new OrderId(ids.NewId()), customerName, clock);
/// }
/// </code>
/// </example>
public interface IAggregateFactory<TAggregateRoot, TId>
    where TAggregateRoot : IAggregateRoot<TId>
    where TId : notnull
{
}
