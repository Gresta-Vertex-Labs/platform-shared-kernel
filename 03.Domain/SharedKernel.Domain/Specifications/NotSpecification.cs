using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A composite specification that negates another specification.
/// An entity satisfies this specification only when it does NOT satisfy the inner specification.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
public sealed class NotSpecification<T> : Specification<T>
{
    /// <summary>
    /// Initialises a new <see cref="NotSpecification{T}"/> that negates <paramref name="spec"/>.
    /// </summary>
    public NotSpecification(Specification<T> spec)
    {
        if (spec.Criteria is not null)
        {
            var param = spec.Criteria.Parameters[0];
            AddCriteria(Expression.Lambda<Func<T, bool>>(
                Expression.Not(spec.Criteria.Body), param));
        }
    }
}
