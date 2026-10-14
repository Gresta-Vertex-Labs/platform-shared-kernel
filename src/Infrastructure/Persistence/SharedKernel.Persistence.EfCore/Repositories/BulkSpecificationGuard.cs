using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Exceptions;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Validates bulk-mutation specifications and setters before any SQL runs, failing closed on anything it
/// cannot prove safe.
/// </summary>
internal static class BulkSpecificationGuard
{
    /// <summary>Rejects includes, ordering, paging and (unless <see cref="AllRowsSpecification{T}"/>) missing criteria.</summary>
    /// <exception cref="UnsupportedSpecificationException">The specification has an unsupported shape.</exception>
    public static void Validate<T>(ISpecification<T> spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Includes.Count > 0)
            throw Unsupported("Includes are not supported; remove them from the specification.");

        if (spec.StringIncludes.Count > 0)
            throw Unsupported("StringIncludes are not supported; remove them from the specification.");

        if (spec.OrderBy is not null || spec.OrderByDescending is not null || spec.ThenBys.Count > 0)
            throw Unsupported("Ordering is not supported; remove it from the specification.");

        if (spec.Skip.HasValue || spec.Take.HasValue)
            throw Unsupported("Paging (Skip/Take) is not supported; remove it from the specification.");

        if (spec.Criteria is null && spec is not AllRowsSpecification<T>)
        {
            throw Unsupported(
                "A bulk mutation specification must declare Criteria (a WHERE clause), or use "
                + $"'AllRowsSpecification<{typeof(T).Name}>' to match every row on purpose.");
        }
    }

    /// <summary>
    /// Resolves every setter target to a mapped property (following complex-type members) and rejects protected
    /// columns.
    /// </summary>
    /// <returns>The resolved properties, in setter order.</returns>
    /// <exception cref="UnsupportedSpecificationException">
    /// A target is not a plain member path to a mapped property, or is the key, a concurrency token,
    /// <c>TenantId</c>, a creation audit column or an encrypted column.
    /// </exception>
    public static IReadOnlyList<IProperty> ValidateSetters<T>(BulkUpdateSetters<T> setters, IEntityType entityType)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entityType);

        if (setters.Targets.Count == 0)
            throw Unsupported("A bulk update must set at least one property.");

        var targets = setters.Targets;
        var resolved = new List<IProperty>(targets.Count);
        foreach (var target in targets)
        {
            var property = Resolve(entityType, target);
            EnsureWritable<T>(property, target);
            resolved.Add(property);
        }

        return resolved;
    }

    private static UnsupportedSpecificationException Unsupported(string reason) =>
        new(EfCoreErrors.BulkSpecificationNotSupported(reason));

    // Walks the member chain from the entity through complex properties to the final scalar property. Anything
    // else (a navigation, an owned type, EF.Property, a method call) fails closed: an unresolved target would
    // otherwise skip every check below, which is how x => x.Contact.Ssn once wrote plaintext into an encrypted
    // column (A8).
    private static IProperty Resolve(IEntityType entityType, LambdaExpression target)
    {
        var segments = MemberPath.TryGetSegments(target)
            ?? throw Unsupported(
                "A bulk update target must be a plain member path such as x => x.Status or x => x.Contact.Email, "
                + $"and '{target}' is not; EF.Property and computed targets are rejected because they can name a "
                + "protected column without this check seeing it.");

        ITypeBase current = entityType;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var complex = current.FindComplexProperty(segments[i])
                ?? throw Unsupported(
                    $"'{string.Join('.', segments)}': '{segments[i]}' is not a complex property of '{current.DisplayName()}'; "
                    + "bulk updates set only the aggregate's own columns, including complex-type members.");

            current = complex.ComplexType;
        }

        return current.FindProperty(segments[^1])
            ?? throw Unsupported(
                $"'{string.Join('.', segments)}' is not a mapped property of '{current.DisplayName()}'.");
    }

    private static void EnsureWritable<T>(IProperty property, LambdaExpression target)
    {
        var path = MemberPath.TryGet(target);
        var isRootMember = property.DeclaringType is IEntityType;

        if (property.IsPrimaryKey())
            throw Unsupported($"'{path}' is part of the primary key and must not be set by a bulk update.");

        if (property.IsConcurrencyToken)
        {
            throw Unsupported(
                $"'{path}' is a concurrency token and must not be set by a bulk update, because that would forge the row version.");
        }

        if (isRootMember && typeof(IHasTenant).IsAssignableFrom(typeof(T)) && property.Name == nameof(IHasTenant.TenantId))
        {
            throw Unsupported(
                $"'{nameof(IHasTenant.TenantId)}' must not be set by a bulk update, because that would move rows to another "
                + "tenant without the tenant write guard.");
        }

        if (isRootMember
            && typeof(IHasCreatedAudit).IsAssignableFrom(typeof(T))
            && property.Name is nameof(IHasCreatedAudit.CreatedBy) or nameof(IHasCreatedAudit.CreatedOn))
        {
            throw Unsupported($"'{property.Name}' must not be set by a bulk update, because creation provenance is immutable.");
        }

        if (property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt) is not null)
        {
            throw Unsupported(
                $"'{path}' is an encrypted column and must not be set by a bulk update, because that bypasses the "
                + "encryption interceptor and stores the value as plaintext.");
        }
    }
}
