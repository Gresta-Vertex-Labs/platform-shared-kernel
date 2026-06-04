using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A composite specification that combines two specifications with logical AND.
/// An entity satisfies this specification only when it satisfies both the left and right
/// constituent specifications.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
public sealed class AndSpecification<T> : Specification<T>
{
    /// <summary>
    /// Initialises a new <see cref="AndSpecification{T}"/> by composing <paramref name="left"/>
    /// and <paramref name="right"/> via logical AND.
    /// </summary>
    public AndSpecification(Specification<T> left, Specification<T> right)
    {
        if (left.Criteria is not null && right.Criteria is not null)
        {
            var param = left.Criteria.Parameters[0];
            var rightBody = new ParameterReplacer(right.Criteria.Parameters[0], param)
                .Visit(right.Criteria.Body);
            AddCriteria(Expression.Lambda<Func<T, bool>>(
                Expression.AndAlso(left.Criteria.Body, rightBody), param));
        }
        else if (left.Criteria is not null)
        {
            AddCriteria(left.Criteria);
        }
        else if (right.Criteria is not null)
        {
            AddCriteria(right.Criteria);
        }

        if (left.AsNoTracking || right.AsNoTracking)
            ApplyNoTracking();

        if (left.IncludeDeleted || right.IncludeDeleted)
            IncludeSoftDeleted();

        foreach (var path in left.StringIncludes)
            AddStringInclude(path);

        foreach (var path in right.StringIncludes)
            if (!StringIncludes.Contains(path))
                AddStringInclude(path);
    }
}
