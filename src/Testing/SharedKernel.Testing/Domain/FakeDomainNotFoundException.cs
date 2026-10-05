using SharedKernel.Domain.Exceptions;

namespace SharedKernel.Testing.Domain;

/// <summary>
/// Static factory producing valid <see cref="DomainNotFoundException"/> instances for
/// repository-fake "not found" setups.
/// </summary>
public static class FakeDomainNotFoundException
{
    /// <summary>
    /// Produces a <see cref="DomainNotFoundException"/> for the aggregate type
    /// <typeparamref name="TAggregate"/> and the given <paramref name="id"/>.
    /// </summary>
    /// <typeparam name="TAggregate">The CLR type of the aggregate that was not found.</typeparam>
    /// <param name="id">The identifier that was searched for.</param>
    /// <returns>A new <see cref="DomainNotFoundException"/>.</returns>
    public static DomainNotFoundException For<TAggregate>(object id) => new(typeof(TAggregate), id);
}
