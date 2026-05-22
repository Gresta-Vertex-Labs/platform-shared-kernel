using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A composite specification that combines two specifications with logical OR.
/// An entity satisfies this specification when it satisfies either the left or right
/// constituent specification.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
public sealed class OrSpecification<T> : Specification<T>
{
    /// <summary>
    /// Initialises a new <see cref="OrSpecification{T}"/> by composing <paramref name="left"/>
    /// and <paramref name="right"/> via logical OR.
    /// </summary>
    public OrSpecification(Specification<T> left, Specification<T> right)
    {
        if (left.Criteria is not null && right.Criteria is not null)
        {
            var param = left.Criteria.Parameters[0];
            var rightBody = new ParameterReplacer(right.Criteria.Parameters[0], param)
                .Visit(right.Criteria.Body);
            AddCriteria(Expression.Lambda<Func<T, bool>>(
                Expression.OrElse(left.Criteria.Body, rightBody), param));
        }
        else if (left.Criteria is not null)
        {
            AddCriteria(left.Criteria);
        }
        else if (right.Criteria is not null)
        {
            AddCriteria(right.Criteria);
        }
    }
}
