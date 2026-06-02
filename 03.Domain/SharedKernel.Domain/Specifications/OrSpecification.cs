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
    /// <remarks>
    /// When either operand has a <see langword="null"/> <c>Criteria</c> (meaning "match all"),
    /// the combined <c>Criteria</c> is also <see langword="null"/> — a logical OR with an
    /// unconditional-match operand always matches everything.
    /// </remarks>
    public OrSpecification(Specification<T> left, Specification<T> right)
    {
        // If either operand has null criteria it matches everything.
        // OR of (match-all, anything) = match-all → combined criteria is null.
        if (left.Criteria is not null && right.Criteria is not null)
        {
            var param = left.Criteria.Parameters[0];
            var rightBody = new ParameterReplacer(right.Criteria.Parameters[0], param)
                .Visit(right.Criteria.Body);
            AddCriteria(Expression.Lambda<Func<T, bool>>(
                Expression.OrElse(left.Criteria.Body, rightBody), param));
        }
        // else: at least one operand has null criteria → combined Criteria stays null (match all)

        if (left.AsNoTracking || right.AsNoTracking)
            ApplyNoTracking();

        if (left.IncludeDeleted || right.IncludeDeleted)
            IncludeSoftDeleted();
    }
}
