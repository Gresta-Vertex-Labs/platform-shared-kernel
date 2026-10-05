using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Encryption.Interception;
using SharedKernel.Persistence.EfCore.Encryption.Metadata;

namespace SharedKernel.Persistence.EfCore;

/// <summary>Equality lookups on encrypted properties through their blind index.</summary>
/// <remarks>
/// <para>
/// Reads everything it needs from the model and the context: which shadow column holds the index, the property's
/// purpose and normalization, and the tenant (the caller's, from <c>SharedKernelDbContext.RequestContext</c>, for a
/// tenanted entity). The index is sent as a query parameter, never as SQL text. It matches every blind-index key
/// version still configured, so rows keep being found while a blind-index key rotation is under way.
/// </para>
/// <para>
/// A blind index is tenant-bound: it finds rows of one tenant at a time. For a tenanted entity without a caller
/// tenant (a cross-tenant job), pass <c>tenantId</c> explicitly.
/// </para>
/// <para>
/// <strong>Not a specification criterion.</strong> A specification is built without a context, but the blind index
/// can only be computed with the context's keys, the property's model annotations and the caller's tenant, so there
/// is no <c>Spec.For&lt;T&gt;().Where(...)</c> form. Filter the query here (the <see cref="IQueryable{T}"/> overload
/// composes with anything else), or resolve the id with this method first and pass it to a specification.
/// </para>
/// </remarks>
public static class EncryptedQueryExtensions
{
    /// <summary>Filters to rows whose encrypted, blind-indexed property equals <paramref name="value"/>.</summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="set">The entity set.</param>
    /// <param name="property">The property, e.g. <c>x =&gt; x.Email</c> or <c>x =&gt; x.Billing.Iban</c>.</param>
    /// <param name="value">The plaintext value to find. Normalized exactly like stored values.</param>
    /// <returns>The filtered query.</returns>
    public static IQueryable<T> WhereEncryptedEquals<T>(this DbSet<T> set, Expression<Func<T, string?>> property, string value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(set);
        return set.AsQueryable().WhereEncryptedEquals(set.GetService<ICurrentDbContext>().Context, property, value);
    }

    /// <summary>Filters <paramref name="query"/> to rows whose encrypted, blind-indexed property equals <paramref name="value"/>.</summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">A query over <typeparamref name="T"/> rooted in <paramref name="context"/>.</param>
    /// <param name="context">The context the query runs on.</param>
    /// <param name="property">The property, e.g. <c>x =&gt; x.Email</c>.</param>
    /// <param name="value">The plaintext value to find.</param>
    /// <param name="tenantId">The tenant whose rows to search, overriding the caller's tenant for a tenanted entity.</param>
    /// <returns>The filtered query.</returns>
    /// <exception cref="InvalidOperationException">The property is not encrypted with a blind index, or a tenanted entity has no tenant.</exception>
    public static IQueryable<T> WhereEncryptedEquals<T>(
        this IQueryable<T> query,
        DbContext context,
        Expression<Func<T, string?>> property,
        string value,
        TenantId? tenantId = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);

        var entityType = context.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"'{typeof(T).Name}' is not an entity type of '{context.GetType().Name}'.");
        var path = GetPath(property);
        var member = EncryptionModelMetadata.For(context.Model).FindMember(entityType, path)
            ?? throw new InvalidOperationException($"'{entityType.ShortName()}.{path}' is not an encrypted property.");
        var blindIndex = member.BlindIndexProperty
            ?? throw new InvalidOperationException(
                $"'{member.DisplayName}' has no blind index. Add '.WithBlindIndex()' after '.Encrypt(...)' and run the " +
                "maintenance job with 'EncryptionMaintenanceMode.RecomputeBlindIndexes' to index existing rows.");

        Guid? tenant = null;
        if (typeof(IHasTenant).IsAssignableFrom(typeof(T)))
        {
            tenant = (tenantId ?? (context as SharedKernelDbContext)?.RequestContext.TenantId)?.Value
                ?? throw new InvalidOperationException(
                    $"'{member.DisplayName}' belongs to a tenanted entity, and its blind index is tenant-bound, but no " +
                    "tenant is known. Search within a tenant, or pass 'tenantId' explicitly.");
        }

        var indexes = FieldEncryptionRuntime.For(context).BlindIndexer.ComputeAllVersions(member, value, tenant);

        var parameter = Expression.Parameter(typeof(T), "x");
        var column = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(string)], parameter, Expression.Constant(blindIndex.Name));
        Expression body = indexes.Length == 1
            ? Expression.Equal(column, Expression.Call(typeof(EF), nameof(EF.Parameter), [typeof(string)], Expression.Constant(indexes[0])))
            : Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Contains),
                [typeof(string)],
                Expression.Call(typeof(EF), nameof(EF.Parameter), [typeof(string[])], Expression.Constant(indexes)),
                column);

        return query.Where(Expression.Lambda<Func<T, bool>>(body, parameter));
    }

    private static string GetPath<T>(Expression<Func<T, string?>> property)
    {
        var names = new Stack<string>();
        var current = property.Body;
        while (current is MemberExpression member)
        {
            names.Push(member.Member.Name);
            current = member.Expression;
        }

        if (current != property.Parameters[0] || names.Count == 0)
        {
            throw new ArgumentException(
                "'property' must be a property access on the entity, e.g. 'x => x.Email' or 'x => x.Billing.Iban'.", nameof(property));
        }

        return string.Join('.', names);
    }
}
