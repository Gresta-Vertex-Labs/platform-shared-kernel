namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Merges the query shape of composed specifications: includes and flags are combined, ordering is carried
/// over from the one operand that declares it, and anything that cannot be merged without changing the
/// query's meaning throws instead of being dropped.
/// </summary>
internal static class SpecificationComposition
{
    /// <summary>
    /// Copies the includes, flags and ordering of <paramref name="operands"/> into <paramref name="target"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// An operand declares <see cref="ISpecification{T}.Skip"/> or <see cref="ISpecification{T}.Take"/>, or
    /// more than one operand declares a primary sort.
    /// </exception>
    internal static void MergeShape<T>(Specification<T> target, string operation, params ISpecification<T>[] operands)
    {
        ISpecification<T>? ordered = null;

        foreach (var operand in operands)
        {
            if (operand.Skip.HasValue || operand.Take.HasValue)
            {
                throw new InvalidOperationException(
                    $"Cannot combine specifications with {operation}: '{operand.GetType().Name}' declares Skip/Take. "
                    + "Paging a combined filter is ambiguous; combine the filters first and page the result at the "
                    + "call site (a page request passed to the repository).");
            }

            if (operand.OrderBy is not null || operand.OrderByDescending is not null)
            {
                if (ordered is not null)
                {
                    throw new InvalidOperationException(
                        $"Cannot combine specifications with {operation}: both '{ordered.GetType().Name}' and "
                        + $"'{operand.GetType().Name}' declare a primary sort, and no merge of two orderings is "
                        + "correct in general. Remove the ordering from one operand.");
                }

                ordered = operand;
            }

            foreach (var include in operand.Includes)
                target.AddIncludeExpression(include);

            foreach (var path in operand.StringIncludes)
                target.AddStringIncludeCore(path);

            if (operand.IsDistinct)
                target.SetDistinctCore();
            if (operand.AsSplitQuery)
                target.SetSplitQueryCore();
            if (operand.IncludeDeleted)
                target.SetIncludeDeletedCore();
        }

        if (ordered is null)
            return;

        if (ordered.OrderBy is { } ascending)
            target.SetOrderCore(ascending, descending: false);
        else
            target.SetOrderCore(ordered.OrderByDescending!, descending: true);

        foreach (var (keySelector, descending) in ordered.ThenBys)
            target.AddThenByCore(keySelector, descending);
    }
}
