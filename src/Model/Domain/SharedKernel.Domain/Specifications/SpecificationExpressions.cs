using System.Linq.Expressions;
using System.Text;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Expression helpers shared by the specification types: boxing a typed selector into the
/// <see cref="object"/>-typed shape <see cref="ISpecification{T}"/> stores, and reading a plain member path.
/// </summary>
internal static class SpecificationExpressions
{
    /// <summary>
    /// Returns <paramref name="selector"/> as an <see cref="object"/>-typed lambda over the same parameter,
    /// boxing a value-typed body with a conversion node (the shape the C# compiler emits for the same lambda).
    /// </summary>
    internal static Expression<Func<T, object>> ToObjectSelector<T, TKey>(Expression<Func<T, TKey>> selector)
    {
        if (selector is Expression<Func<T, object>> alreadyObject)
            return alreadyObject;

        Expression body = typeof(TKey).IsValueType
            ? Expression.Convert(selector.Body, typeof(object))
            : selector.Body;

        return Expression.Lambda<Func<T, object>>(body, selector.Parameters);
    }

    /// <summary>
    /// Returns the dot-separated member path of <paramref name="selector"/> (<c>o =&gt; o.Customer.Address</c>
    /// gives <c>"Customer.Address"</c>), or <see langword="null"/> when its body is anything other than a
    /// chain of member accesses on the lambda parameter, conversions aside.
    /// </summary>
    internal static string? TryGetMemberPath(LambdaExpression selector)
    {
        var names = new List<string>();
        var current = StripConvert(selector.Body);

        while (current is MemberExpression member)
        {
            names.Add(member.Member.Name);

            if (member.Expression is null)
                return null;

            current = StripConvert(member.Expression);
        }

        if (current != selector.Parameters[0] || names.Count == 0)
            return null;

        var path = new StringBuilder();
        for (var i = names.Count - 1; i >= 0; i--)
        {
            if (path.Length > 0)
                path.Append('.');
            path.Append(names[i]);
        }

        return path.ToString();
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } unary)
            expression = unary.Operand;

        return expression;
    }
}
