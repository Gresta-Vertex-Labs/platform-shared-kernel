using FluentAssertions;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Sibling-package purity inside 06.Persistence: the EF Core package is the PostgreSQL provider, and the Dapper and
/// Npgsql packages carry no EF Core dependency.
/// </summary>
/// <remarks>
/// The former <c>PersistenceNeverReferencesApplicationOrSecurity</c> rule and its source-tree scan were deleted in
/// P-574: the tier check covers every edge it guarded that is still forbidden (the Host-tier pipeline and mediator
/// adapter, SKTIER001; MediatR, <c>DependencyGraphRulesTests.MediatR_IsReferencedOnlyByTheMediatorAdapter</c>).
/// </remarks>
public sealed class PersistenceLayeringRulesTests
{
    [Fact]
    public void EfCore_IsThePostgreSqlProvider()
    {
        // P-558: PostgreSQL-only. The former SharedKernel.Persistence.PostgreSQL package merged into EfCore,
        // which now references the Npgsql EF Core provider and the shared SharedKernel.Persistence.Npgsql.
        typeof(SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().Contain(["Npgsql.EntityFrameworkCore.PostgreSQL", "SharedKernel.Persistence.Npgsql"]);
    }

    [Fact]
    public void Dapper_NeverReferencesEfCore()
    {
        typeof(SharedKernel.Persistence.Dapper.Sessions.IDbSessionFactory).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Should().NotContain(name =>
                name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("SharedKernel.Persistence.EfCore", StringComparison.Ordinal),
                because: "Dapper-only services must not pull in EF Core");
    }

    [Fact]
    public void Npgsql_NeverReferencesEfCore()
    {
        typeof(SharedKernel.Persistence.NpgsqlPersistenceExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Should().NotContain(name =>
                name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("SharedKernel.Persistence.EfCore", StringComparison.Ordinal),
                because: "the shared data source and the SQLSTATE classifier serve Dapper as well");
    }

    [Fact]
    public void PersistenceEfCore_ReferencesTheSharedExecutionContracts()
    {
        // P-558 (A): the audit contracts that used the shared enums left .Abstractions, so the EF Core package
        // (EfUnitOfWork implements the shared IUnitOfWork) is now the one that proves the reference.
        typeof(SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().Contain("SharedKernel.Execution");
    }
}
