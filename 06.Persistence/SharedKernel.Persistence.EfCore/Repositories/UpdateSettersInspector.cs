using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Extracts the set of property names a caller-supplied bulk-update setters delegate targets,
/// without executing any SQL, so the target columns can be validated before the statement runs.
/// </summary>
/// <remarks>
/// Runs <c>setPropertyCalls</c> against a standalone
/// <see cref="UpdateSettersBuilder{TSource}"/> instance (a pure in-memory recorder — EF Core only
/// translates it to SQL once handed to <c>IQueryable{T}.ExecuteUpdateAsync</c>), then reads back the
/// accumulated setters via <c>BuildSettersExpression()</c> and inspects each recorded property
/// selector's target member. Both members are `[EntityFrameworkInternal]`
/// (<c>EF1001</c>) — the same internal-API class this package's public
/// <see cref="IBulkMutationRepository{TAggregate,TId}"/> surface already exposes at its own boundary
/// (<c>Action&lt;UpdateSettersBuilder&lt;TAggregate&gt;&gt;</c>), so this adds no new class of
/// version-fragility risk, only another instance of an already-accepted one.
/// </remarks>
internal static class UpdateSettersInspector
{
#pragma warning disable EF1001 // Internal EF Core API usage — see class remarks.
    /// <summary>
    /// Returns the CLR member name targeted by every <c>SetProperty(...)</c> call
    /// <paramref name="setPropertyCalls"/> makes, in call order. A property selector this method
    /// cannot recognize (not a direct <c>x =&gt; x.Member</c> access) is reported as an empty string,
    /// but an empty entry means "this setter targets something I could not name", NOT "this
    /// setter is harmless": EF.Property&lt;T&gt;(x, "TenantId") reports empty while targeting a
    /// protected column. BulkSpecificationGuard.ValidateSetters therefore rejects an empty entry
    /// outright rather than letting it fall through its protected-name checks.
    /// </summary>
    /// <typeparam name="TSource">The entity type the bulk update targets.</typeparam>
    /// <param name="setPropertyCalls">The caller-supplied setters delegate.</param>
    public static IReadOnlyList<string> ExtractPropertyNames<TSource>(
        Action<UpdateSettersBuilder<TSource>> setPropertyCalls)
    {
        var builder = new UpdateSettersBuilder<TSource>();
        setPropertyCalls(builder);

        var settersArray = builder.BuildSettersExpression();
        var names = new List<string>(settersArray.Expressions.Count);

        foreach (var element in settersArray.Expressions)
        {
            if (element is NewExpression { Arguments.Count: 2 } tuple
                && tuple.Arguments[0] is LambdaExpression { Body: MemberExpression member })
            {
                names.Add(member.Member.Name);
            }
            else
            {
                names.Add(string.Empty);
            }
        }

        return names;
    }
#pragma warning restore EF1001
}
