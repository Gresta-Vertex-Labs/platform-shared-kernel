using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Validates that an <see cref="ISpecification{T}"/> only uses shapes that translate to a single
/// server-side <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> statement.
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
    /// <typeparam name="T">The aggregate type.</typeparam>
    /// <param name="spec">The specification to validate.</param>
    public static void Validate<T>(ISpecification<T> spec)
    {
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
    }
}
