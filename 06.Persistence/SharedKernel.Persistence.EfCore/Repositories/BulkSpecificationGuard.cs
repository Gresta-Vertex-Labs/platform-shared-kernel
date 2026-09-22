using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.Abstractions.Repositories;

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
            throw new UnsupportedSpecificationException("Includes are not supported for bulk mutation operations.");

        if (spec.StringIncludes.Count > 0)
            throw new UnsupportedSpecificationException("StringIncludes are not supported for bulk mutation operations.");

        if (spec.OrderBy is not null || spec.OrderByDescending is not null || spec.ThenBys.Count > 0)
            throw new UnsupportedSpecificationException("Ordering is not supported for bulk mutation operations.");

        if (spec.Skip.HasValue || spec.Take.HasValue)
            throw new UnsupportedSpecificationException("Paging (Skip/Take) is not supported for bulk mutation operations.");

        if (spec.Criteria is null && spec is not AllRowsSpecification<T>)
        {
            throw new UnsupportedSpecificationException(
                "A bulk mutation specification must declare Criteria (a WHERE clause), or use "
                + $"'AllRowsSpecification<{typeof(T).Name}>' to explicitly opt in to matching every row.");
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
            throw new UnsupportedSpecificationException("A bulk update must set at least one property.");

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

    // Walks the member chain from the entity through complex properties to the final scalar property. Anything
    // else (a navigation, an owned type, EF.Property, a method call) fails closed: an unresolved target would
    // otherwise skip every check below, which is how x => x.Contact.Ssn once wrote plaintext into an encrypted
    // column (A8).
    private static IProperty Resolve(IEntityType entityType, LambdaExpression target)
    {
        var segments = MemberPath.TryGetSegments(target)
            ?? throw new UnsupportedSpecificationException(
                $"A bulk update target must be a plain member path such as x => x.Status or x => x.Contact.Email; "
                + $"'{target}' is not. EF.Property and computed targets are rejected because they can name a "
                + "protected column without this check seeing it.");

        ITypeBase current = entityType;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var complex = current.FindComplexProperty(segments[i])
                ?? throw new UnsupportedSpecificationException(
                    $"'{string.Join('.', segments)}': '{segments[i]}' is not a complex property of '{current.DisplayName()}'. "
                    + "Bulk updates can only set the aggregate's own columns, including complex-type members.");

            current = complex.ComplexType;
        }

        return current.FindProperty(segments[^1])
            ?? throw new UnsupportedSpecificationException(
                $"'{string.Join('.', segments)}' is not a mapped property of '{current.DisplayName()}'.");
    }

    private static void EnsureWritable<T>(IProperty property, LambdaExpression target)
    {
        var path = MemberPath.TryGet(target);
        var isRootMember = property.DeclaringType is IEntityType;

        if (property.IsPrimaryKey())
            throw new UnsupportedSpecificationException($"'{path}' is part of the primary key and cannot be set by a bulk update.");

        if (property.IsConcurrencyToken)
        {
            throw new UnsupportedSpecificationException(
                $"'{path}' is a concurrency token; setting it would forge the row version.");
        }

        if (isRootMember && typeof(IHasTenant).IsAssignableFrom(typeof(T)) && property.Name == nameof(IHasTenant.TenantId))
        {
            throw new UnsupportedSpecificationException(
                $"'{nameof(IHasTenant.TenantId)}' cannot be set by a bulk update; it would move rows to another tenant "
                + "without the tenant write guard.");
        }

        if (isRootMember
            && typeof(IHasCreatedAudit).IsAssignableFrom(typeof(T))
            && property.Name is nameof(IHasCreatedAudit.CreatedBy) or nameof(IHasCreatedAudit.CreatedOn))
        {
            throw new UnsupportedSpecificationException($"'{property.Name}' cannot be set by a bulk update; creation provenance is immutable.");
        }

        if (property.FindAnnotation(PersistenceModelAnnotationNames.Encrypt) is not null)
        {
            throw new UnsupportedSpecificationException(
                $"'{path}' is an encrypted column. A bulk update bypasses the encryption interceptor and would store "
                + "the value as plaintext.");
        }

    }
}
