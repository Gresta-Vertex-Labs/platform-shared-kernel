using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// An <see cref="ExpressionVisitor"/> that rewrites all occurrences of one
/// <see cref="ParameterExpression"/> to another. Used internally when composing two
/// expression trees that use different parameter names.
/// </summary>
internal sealed class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _oldParameter;
    private readonly ParameterExpression _newParameter;

    internal ParameterReplacer(ParameterExpression oldParameter, ParameterExpression newParameter)
    {
        _oldParameter = oldParameter;
        _newParameter = newParameter;
    }

    /// <inheritdoc/>
    protected override Expression VisitParameter(ParameterExpression node) =>
        node == _oldParameter ? _newParameter : base.VisitParameter(node);
}
