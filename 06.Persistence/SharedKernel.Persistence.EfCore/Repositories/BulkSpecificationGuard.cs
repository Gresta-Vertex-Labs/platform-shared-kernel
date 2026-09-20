using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Validates that an <see cref="ISpecification{T}"/> and, for updates, its setters delegate, only
/// use shapes that translate safely to a single server-side <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>
/// statement.
/// </summary>
internal static class BulkSpecificationGuard
{
    /// <summary>
    /// Throws <see cref="UnsupportedSpecificationException"/> if <paramref name="spec"/> declares
    /// any include, ordering, or paging shape — only <see cref="ISpecification{T}.Criteria"/> and
    /// <see cref="ISpecification{T}.IncludeDeleted"/> are applied to bulk mutation queries.
    /// <see cref="ISpecification{T}.IsDistinct"/> and <see cref="ISpecification{T}.AsNoTracking"/>
    /// are tolerated as no-ops.
    /// </summary>
    /// <remarks>
    /// <strong>Criteria required:</strong> a <see langword="null"/>
    /// <see cref="ISpecification{T}.Criteria"/> is rejected unless <paramref name="spec"/> is an
    /// <see cref="AllRowsSpecification{T}"/> — an unqualified bulk statement over every row is almost
    /// always a missing-WHERE-clause bug, not a deliberate choice, so the deliberate case must name
    /// itself explicitly. See <see cref="AllRowsSpecification{T}"/>'s remarks.
    /// </remarks>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="spec">The specification to validate.</param>
    public static void Validate<T>(ISpecification<T> spec)
    {
        // Specific shape violations are reported before the general "must declare Criteria"
        // guidance below, so a spec that is BOTH criteria-less AND, say, carries an Include gets
        // the more actionable "Includes are not supported" message.
        if (spec.Includes.Count > 0)
            throw new UnsupportedSpecificationException("Includes are not supported for bulk mutation operations.");

        if (spec.StringIncludes.Count > 0)
            throw new UnsupportedSpecificationException("StringIncludes are not supported for bulk mutation operations.");

        if (spec.OrderBy is not null || spec.OrderByDescending is not null)
            throw new UnsupportedSpecificationException("Ordering is not supported for bulk mutation operations.");

        if (spec.ThenBys.Count > 0)
            throw new UnsupportedSpecificationException("ThenBys are not supported for bulk mutation operations.");

        if (spec.Skip.HasValue || spec.Take.HasValue)
            throw new UnsupportedSpecificationException("Paging (Skip/Take) is not supported for bulk mutation operations.");

        if (spec.Criteria is null && spec is not AllRowsSpecification<T>)
        {
            throw new UnsupportedSpecificationException(
                "A bulk mutation specification must declare Criteria (a WHERE clause), or use " +
                $"'{nameof(AllRowsSpecification<T>)}<{typeof(T).Name}>' to explicitly opt in to " +
                "matching every row.");
        }
    }

    /// <summary>
    /// Throws <see cref="UnsupportedSpecificationException"/> if <paramref name="setPropertyCalls"/>
    /// targets a protected column that a bulk statement must never set directly, because doing so
    /// would bypass an invariant the normal EF Core save pipeline (interceptors, the tenant write
    /// guard) otherwise enforces on every write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Protected:</strong> <c>TenantId</c>, when <typeparamref name="T"/>
    /// implements <see cref="IHasTenant"/> — bulk mutations bypass <c>TenantWriteGuardInterceptor</c>
    /// entirely, so allowing a caller to overwrite this column would let a row be silently
    /// relabelled into another tenant with no guard in the path at all. <c>RowVersion</c>, when
    /// <typeparamref name="T"/> implements <see cref="IHasConcurrency"/> — forging the concurrency
    /// token would let a caller manufacture a value that defeats every future optimistic-concurrency
    /// check for that row. <c>CreatedBy</c>/<c>CreatedOn</c>, when <typeparamref name="T"/>
    /// implements <see cref="IHasCreatedAudit"/> — immutable creation provenance.
    /// </para>
    /// <para>
    /// <strong>Deliberately NOT protected:</strong> <c>IsDeleted</c>/<c>DeletedOn</c>/<c>DeletedBy</c>
    /// — <see cref="IBulkMutationRepository{TAggregate,TId}"/>'s own documentation already names
    /// setting these three columns via this exact method as the sanctioned way to bulk soft-delete,
    /// since <see cref="IBulkMutationRepository{TAggregate,TId}.ExecuteDeleteAsync"/> always issues a
    /// hard physical <c>DELETE</c> with no soft-delete translation.
    /// </para>
    /// <para>
    /// <strong>Encrypted columns:</strong> a property the model annotates with
    /// <see cref="PersistenceModelAnnotationNames.Encrypt"/> can never be targeted either, regardless
    /// of <typeparamref name="T"/>'s implemented interfaces — <c>ExecuteUpdateAsync</c> issues a raw
    /// server-side <c>UPDATE</c> that never passes through the encryption save-changes interceptor, so
    /// a caller-supplied value would be written as PLAINTEXT into a column every other write path, and
    /// every compliance artefact, treats as encrypted (and would leave any blind-index shadow column
    /// stale, silently breaking future equality lookups on the row). The annotation is read directly
    /// off <paramref name="entityType"/> — a bare, cross-package string constant this package already
    /// shares with the opt-in encryption package for exactly this kind of check (see
    /// <see cref="PersistenceModelAnnotationNames"/>'s own remarks) — never a project reference to that
    /// package, which this one must not take.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="setPropertyCalls">The caller-supplied setters delegate.</param>
    /// <param name="entityType">
    /// <typeparamref name="T"/>'s entity type in the executing <c>DbContext</c>'s model, used only to
    /// check each targeted property for the encryption annotation. <see langword="null"/> skips that
    /// one check (every other protected-column check above is interface-based and needs no model).
    /// </param>
    public static void ValidateSetters<T>(Action<UpdateSettersBuilder<T>> setPropertyCalls, IEntityType? entityType = null)
    {
        var targeted = UpdateSettersInspector.ExtractPropertyNames(setPropertyCalls);

        foreach (var propertyName in targeted)
        {
            // FAIL CLOSED on a selector the inspector could not resolve to a property name.
            // UpdateSettersInspector yields string.Empty for anything that is not a plain member
            // access - including EF.Property<T>(x, "TenantId"), which names a protected column
            // through a string none of the checks below would ever see. Letting an unresolved
            // shape through makes every check that follows advisory rather than enforced.
            if (string.IsNullOrEmpty(propertyName))
            {
                throw new UnsupportedSpecificationException(
                    "A bulk mutation setter must target a property directly (x => x.Property). The " +
                    "supplied selector could not be resolved to a property name - shapes such as " +
                    "EF.Property<T>(x, \"Name\") are rejected here because they can name a protected " +
                    "column (TenantId, RowVersion, CreatedBy/CreatedOn, an encrypted column) without " +
                    "this guard seeing it.");
            }

            if (typeof(IHasTenant).IsAssignableFrom(typeof(T))
                && propertyName == nameof(IHasTenant.TenantId))
            {
                throw new UnsupportedSpecificationException(
                    $"'{nameof(IHasTenant.TenantId)}' cannot be set via a bulk mutation — it would " +
                    "bypass tenant-write-guard enforcement entirely.");
            }

            if (typeof(IHasConcurrency).IsAssignableFrom(typeof(T))
                && propertyName == nameof(IHasConcurrency.RowVersion))
            {
                throw new UnsupportedSpecificationException(
                    $"'{nameof(IHasConcurrency.RowVersion)}' cannot be set via a bulk mutation — it " +
                    "would let a caller forge the optimistic-concurrency token.");
            }

            if (typeof(IHasCreatedAudit).IsAssignableFrom(typeof(T))
                && (propertyName == nameof(IHasCreatedAudit.CreatedBy) || propertyName == nameof(IHasCreatedAudit.CreatedOn)))
            {
                throw new UnsupportedSpecificationException(
                    $"'{propertyName}' cannot be set via a bulk mutation — creation provenance is immutable.");
            }

            if (entityType?.FindProperty(propertyName)?.FindAnnotation(PersistenceModelAnnotationNames.Encrypt) is not null)
            {
                throw new UnsupportedSpecificationException(
                    $"'{propertyName}' cannot be set via a bulk mutation — it is an encrypted column. " +
                    "A bulk statement writes the supplied value directly, bypassing the encryption " +
                    "save-changes interceptor entirely, which would store it as plaintext.");
            }
        }
    }
}
