namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marks a class as the factory for one aggregate root type.
/// </summary>
/// <typeparam name="TAggregateRoot">The aggregate root type the factory produces.</typeparam>
/// <typeparam name="TId">The aggregate's identity key type.</typeparam>
/// <remarks>
/// <para>
/// A marker, deliberately without members: creation signatures differ for every aggregate, so no
/// shared method could describe them. The marker lets architecture rules find every factory and
/// hold them to one convention: a factory lives in the domain assembly, and creates its aggregate
/// through a <c>Create</c> method that returns a <c>ValidationResult</c> instead of throwing.
/// </para>
/// <para>
/// Use a dedicated factory when creation needs collaborators the aggregate should not know about,
/// such as a uniqueness check or an identifier generator. When the aggregate can build itself,
/// a static <c>Create</c> on the aggregate using <c>TryCreate</c> is enough and needs no factory.
/// </para>
/// <example>
/// <code>
/// public sealed class OrderFactory(IIdGenerator ids, IClock clock) : IAggregateFactory&lt;Order, OrderId&gt;
/// {
///     public ValidationResult&lt;Order&gt; Create(string customerName) =&gt;
///         Order.Create(new OrderId(ids.NewId()), customerName, clock);
/// }
/// </code>
/// </example>
/// </remarks>
public interface IAggregateFactory<TAggregateRoot, TId>
    where TAggregateRoot : IAggregateRoot<TId>
    where TId : notnull
{
}
