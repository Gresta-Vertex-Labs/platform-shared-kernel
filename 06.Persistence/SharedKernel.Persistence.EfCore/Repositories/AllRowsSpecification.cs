using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// An explicit, deliberately-named opt-in for a bulk mutation (<see cref="IBulkMutationRepository{TAggregate,TId}.ExecuteUpdateAsync"/>/
/// <see cref="IBulkMutationRepository{TAggregate,TId}.ExecuteDeleteAsync"/>) that must match every
/// row of <typeparamref name="T"/> — i.e. one with no <see cref="ISpecification{T}.Criteria"/> at all.
/// </summary>
/// <typeparam name="T">The aggregate type the bulk mutation targets.</typeparam>
/// <remarks>
/// <see cref="BulkSpecificationGuard.Validate{T}"/> rejects any bulk mutation
/// specification with a <see langword="null"/> <see cref="ISpecification{T}.Criteria"/> UNLESS it is
/// (or derives from) this type — a criteria-less bulk statement is almost always an accidental
/// "forgot the WHERE clause" bug, so it must be an unmistakable, deliberate choice at the call site
/// rather than the silent default. For a tenanted aggregate the tenant global query filter still
/// narrows the statement to the current tenant regardless — this type only opts out of a criteria
/// requirement, never out of tenant isolation.
/// </remarks>
public sealed class AllRowsSpecification<T> : Specification<T>
{
}
