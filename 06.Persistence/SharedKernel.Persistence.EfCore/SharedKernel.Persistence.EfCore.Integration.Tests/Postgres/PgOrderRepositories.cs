using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

internal sealed class PgOrderRepository(PgTestDbContext ctx, ICrossTenantScope crossTenantScope)
    : TenantedRepository<PgOrderAggregate, PgOrderId>(ctx, crossTenantScope);

internal sealed class PgOrderReadRepository(PgTestDbContext ctx)
    : EfReadRepository<PgOrderAggregate, PgOrderId>(ctx, new SpecificationEvaluator<PgOrderAggregate>());

/// <summary>Matches every order (for a bulk mutation with no additional filter beyond tenant/soft-delete).</summary>
internal sealed class AllPgOrdersSpecification : Specification<PgOrderAggregate>
{
    public AllPgOrdersSpecification(bool includeDeleted = false)
    {
        if (includeDeleted)
            IncludeSoftDeleted();
    }
}

/// <summary>Matches orders by <see cref="PgOrderAggregate.Code"/> prefix — used for scoping bulk ops per test run.</summary>
internal sealed class PgOrdersByCodePrefixSpecification : Specification<PgOrderAggregate>
{
    public PgOrdersByCodePrefixSpecification(string prefix, bool includeDeleted = false)
    {
        AddCriteria(o => o.Code.StartsWith(prefix));
        if (includeDeleted)
            IncludeSoftDeleted();
    }
}

