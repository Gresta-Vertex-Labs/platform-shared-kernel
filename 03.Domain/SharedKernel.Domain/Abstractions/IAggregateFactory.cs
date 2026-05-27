namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Zero-member marker interface that tags a class as a factory for a specific aggregate root type.
/// </summary>
/// <typeparam name="TAggregateRoot">The type of the aggregate root this factory produces.</typeparam>
/// <typeparam name="TId">The type of the aggregate's identity key.</typeparam>
/// <remarks>
/// <para>
/// Apply this interface to static factory classes or dedicated factory services that produce
/// instances of <typeparamref name="TAggregateRoot"/>. By convention, expose a
/// <c>static Result&lt;TAggregateRoot&gt; Create(...)</c> factory method that uses
/// <c>AggregateRoot&lt;TId&gt;.TryCreate&lt;TAggregateRoot&gt;(...)</c> to convert
/// construction exceptions into railway-friendly <c>Result</c> values.
/// </para>
/// <para>
/// Example:
/// <code>
/// public sealed class OrderFactory : IAggregateFactory&lt;Order, OrderId&gt;
/// {
///     public static Result&lt;Order&gt; Create(string customerName, IClock clock)
///         => Order.TryCreate(customerName, clock);
/// }
/// </code>
/// </para>
/// </remarks>
public interface IAggregateFactory<TAggregateRoot, TId>
    where TAggregateRoot : IAggregateRoot<TId>
    where TId : notnull
{
}
