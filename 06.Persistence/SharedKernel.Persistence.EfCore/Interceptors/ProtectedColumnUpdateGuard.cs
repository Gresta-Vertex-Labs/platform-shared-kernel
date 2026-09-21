using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Repositories;

namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// Rejects, before an <c>ExecuteUpdate</c> is compiled, a setter that targets a column only the platform may write:
/// the tenant column, the creation audit columns or a concurrency token (the <c>xmin</c> row version included).
/// </summary>
/// <remarks>
/// <para>
/// The bulk repository validates its setters itself; this guard closes the direct path —
/// <c>context.Orders.Where(...).ExecuteUpdateAsync(s =&gt; s.SetProperty(o =&gt; o.TenantId, other))</c> — which would
/// otherwise move rows to another tenant, rewrite provenance or forge a row version without any check.
/// </para>
/// <para>
/// It inspects the LINQ expression tree (EF Core passes the setters as <c>(property lambda, value)</c> tuples), so a
/// target named through <c>EF.Property</c> is caught as well. SQL written by hand is not checked.
/// </para>
/// </remarks>
internal sealed class ProtectedColumnUpdateGuard : IQueryExpressionInterceptor
{
    private static readonly MethodInfo PropertyMethod = typeof(EF).GetMethod(nameof(EF.Property))!;

    private ProtectedColumnUpdateGuard()
    {
    }

    /// <summary>The shared instance every context registers.</summary>
    public static ProtectedColumnUpdateGuard Instance { get; } = new();

    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        if (queryExpression is MethodCallExpression { Method.Name: "ExecuteUpdate" } call
            && eventData.Context is { } context
            && call.Arguments.Count > 1
            && call.Arguments[0].Type.IsGenericType
            && context.Model.FindEntityType(call.Arguments[0].Type.GetGenericArguments()[0]) is { } entityType)
        {
            foreach (var target in Targets(call.Arguments[1]))
                Check(entityType, target);
        }

        return queryExpression;
    }

    // The first argument of each (property, value) tuple of the setter array.
    private static IEnumerable<LambdaExpression> Targets(Expression setters)
    {
        if (setters is not NewArrayExpression array)
            yield break;

        foreach (var element in array.Expressions)
        {
            if (element is NewExpression { Arguments.Count: > 0 } tuple && Unquote(tuple.Arguments[0]) is LambdaExpression target)
                yield return target;
        }
    }

    private static void Check(IEntityType entityType, LambdaExpression target)
    {
        if (TargetProperty(entityType, target) is not { } property)
            return;

        var isRootMember = property.DeclaringType is IEntityType;
        var clrType = entityType.ClrType;
        string? reason = null;

        if (property.IsConcurrencyToken)
            reason = "it is a concurrency token; setting it would forge the row version";
        else if (isRootMember && typeof(IHasTenant).IsAssignableFrom(clrType) && property.Name == nameof(IHasTenant.TenantId))
            reason = "it would move rows to another tenant without the tenant write guard";
        else if (isRootMember && typeof(IHasCreatedAudit).IsAssignableFrom(clrType)
            && property.Name is nameof(IHasCreatedAudit.CreatedBy) or nameof(IHasCreatedAudit.CreatedOn))
            reason = "creation provenance is immutable";

        if (reason is not null)
        {
            throw new InvalidOperationException(
                $"ExecuteUpdate on '{entityType.DisplayName()}' may not set '{property.Name}': {reason}. Change the entity "
                + "through its aggregate and SaveChanges instead.");
        }
    }

    private static IProperty? TargetProperty(IEntityType entityType, LambdaExpression target)
    {
        if (MemberPath.TryGetSegments(target) is { } segments)
        {
            ITypeBase? current = entityType;
            for (var i = 0; current is not null && i < segments.Count - 1; i++)
                current = current.FindComplexProperty(segments[i])?.ComplexType;

            return current?.FindProperty(segments[^1]);
        }

        // EF.Property<T>(e, "Name")
        if (StripConvert(target.Body) is MethodCallExpression { Method.IsGenericMethod: true } call
            && call.Method.GetGenericMethodDefinition() == PropertyMethod
            && call.Arguments[1] is ConstantExpression { Value: string name })
        {
            return entityType.FindProperty(name);
        }

        return null;
    }

    private static Expression Unquote(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            expression = unary.Operand;

        return expression;
    }
}
