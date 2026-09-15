using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// An expression visitor that replaces every reference to one lambda parameter with another, so the bodies of
/// two lambdas can be combined over a single parameter.
/// </summary>
/// <remarks>
/// Parameters are matched by reference, not by name; other parameters are left unchanged.
/// </remarks>
internal sealed class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _oldParameter;
    private readonly ParameterExpression _newParameter;

    /// <summary>
    /// Initializes a new visitor that replaces <paramref name="oldParameter"/> with <paramref name="newParameter"/>.
    /// </summary>
    /// <param name="oldParameter">The parameter to replace.</param>
    /// <param name="newParameter">The parameter to substitute.</param>
    internal ParameterReplacer(ParameterExpression oldParameter, ParameterExpression newParameter)
    {
        _oldParameter = oldParameter;
        _newParameter = newParameter;
    }

    /// <summary>
    /// Returns the replacement parameter when <paramref name="node"/> is the parameter being replaced;
    /// otherwise returns <paramref name="node"/> unchanged.
    /// </summary>
    /// <param name="node">The parameter expression being visited.</param>
    /// <returns>The replacement parameter, or <paramref name="node"/>.</returns>
    protected override Expression VisitParameter(ParameterExpression node) =>
        node == _oldParameter ? _newParameter : base.VisitParameter(node);
}
