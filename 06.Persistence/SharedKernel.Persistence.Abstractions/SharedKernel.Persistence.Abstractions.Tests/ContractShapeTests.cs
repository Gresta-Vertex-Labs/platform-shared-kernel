using System.Data;
using System.Linq.Expressions;
using FluentAssertions;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.Abstractions.UnitOfWork;

namespace SharedKernel.Persistence.Abstractions.Tests;

/// <summary>
/// Compile-time and runtime contract shape tests for SharedKernel.Persistence.Abstractions.
/// Ensures all expected interface members exist and no forbidden types (outbox) are present.
/// </summary>
public sealed class ContractShapeTests
{
    // ---------------------------------------------------------------------------
    // IRepository<TAggregate, TId> contract
    // ---------------------------------------------------------------------------

    [Fact]
    public void IRepository_Has_GetByIdAsync()
    {
        var method = typeof(IRepository<,>).GetMethod("GetByIdAsync");
        method.Should().NotBeNull("IRepository must expose GetByIdAsync (write side retains this method)");
    }

    [Fact]
    public void IRepository_Has_ExistsAsync()
    {
        var method = typeof(IRepository<,>).GetMethod("ExistsAsync");
        method.Should().NotBeNull("IRepository must expose ExistsAsync");
    }

    [Fact]
    public void IRepository_Has_AddAsync()
    {
        var method = typeof(IRepository<,>).GetMethod("AddAsync");
        method.Should().NotBeNull("IRepository must expose AddAsync");
    }

    [Fact]
    public void IRepository_Has_AddRangeAsync()
    {
        var method = typeof(IRepository<,>).GetMethod("AddRangeAsync");
        method.Should().NotBeNull("IRepository must expose AddRangeAsync");
    }

    [Fact]
    public void IRepository_Has_UpdateAsync()
    {
        var method = typeof(IRepository<,>).GetMethod("UpdateAsync");
        method.Should().NotBeNull("IRepository must expose UpdateAsync");
    }

    [Fact]
    public void IRepository_Has_UpdateRangeAsync()
    {
        var method = typeof(IRepository<,>).GetMethod("UpdateRangeAsync");
        method.Should().NotBeNull("IRepository must expose UpdateRangeAsync");
    }

    [Fact]
    public void IRepository_Has_DeleteAsync()
    {
        var method = typeof(IRepository<,>).GetMethod("DeleteAsync");
        method.Should().NotBeNull("IRepository must expose DeleteAsync");
    }

    [Fact]
    public void IRepository_Has_DeleteRangeAsync()
    {
        var method = typeof(IRepository<,>).GetMethod("DeleteRangeAsync");
        method.Should().NotBeNull("IRepository must expose DeleteRangeAsync");
    }

    [Fact]
    public void IRepository_DoesNot_Expose_IQueryable()
    {
        var methods = typeof(IRepository<,>).GetMethods();
        var queryableMethod = methods.Any(m =>
            m.ReturnType.IsGenericType &&
            m.ReturnType.GetGenericTypeDefinition() == typeof(IQueryable<>));
        queryableMethod.Should().BeFalse("IRepository must not expose IQueryable");
    }

    // ---------------------------------------------------------------------------
    // IReadRepository<TAggregate, TId> contract
    // ---------------------------------------------------------------------------

    [Fact]
    public void IReadRepository_Does_Not_Have_GetByIdAsync()
    {
        // P-080 breaking change: GetByIdAsync was removed from IReadRepository.
        // Use GetBySpecAsync(new ByIdSpecification<TAggregate, TId>(id), ct) instead.
        var method = typeof(IReadRepository<,>).GetMethod("GetByIdAsync");
        method.Should().BeNull(
            "IReadRepository.GetByIdAsync was removed (P-080 breaking change). " +
            "Use GetBySpecAsync(new ByIdSpecification<TAggregate, TId>(id), ct) instead.");
    }

    [Fact]
    public void IReadRepository_Has_GetBySpecAsync()
    {
        var method = typeof(IReadRepository<,>).GetMethod("GetBySpecAsync");
        method.Should().NotBeNull("IReadRepository must expose GetBySpecAsync");
    }

    [Fact]
    public void IReadRepository_Has_ListAsync()
    {
        var method = typeof(IReadRepository<,>).GetMethod("ListAsync");
        method.Should().NotBeNull("IReadRepository must expose ListAsync");
    }

    [Fact]
    public void IReadRepository_Has_CountAsync()
    {
        var method = typeof(IReadRepository<,>).GetMethod("CountAsync");
        method.Should().NotBeNull("IReadRepository must expose CountAsync");
    }

    [Fact]
    public void IReadRepository_Has_AnyAsync()
    {
        var method = typeof(IReadRepository<,>).GetMethod("AnyAsync");
        method.Should().NotBeNull("IReadRepository must expose AnyAsync");
    }

    [Fact]
    public void IReadRepository_Has_GetByIdsAsync()
    {
        var method = typeof(IReadRepository<,>).GetMethod("GetByIdsAsync");
        method.Should().NotBeNull("IReadRepository must expose GetByIdsAsync");
    }

    [Fact]
    public void IReadRepository_Has_ListPagedAsync()
    {
        var method = typeof(IReadRepository<,>).GetMethod("ListPagedAsync");
        method.Should().NotBeNull("IReadRepository must expose ListPagedAsync returning PagedList<T>");
    }

    [Fact]
    public void IReadRepository_Has_ListProjectedAsync()
    {
        var method = typeof(IReadRepository<,>).GetMethod("ListProjectedAsync");
        method.Should().NotBeNull("IReadRepository must expose ListProjectedAsync<TResult>");
    }

    [Fact]
    public void IReadRepository_Has_GetBySpecProjectedAsync()
    {
        var method = typeof(IReadRepository<,>).GetMethod("GetBySpecProjectedAsync");
        method.Should().NotBeNull("IReadRepository must expose GetBySpecProjectedAsync<TResult>");
    }

    [Fact]
    public void IReadRepository_DoesNot_Expose_IQueryable()
    {
        var methods = typeof(IReadRepository<,>).GetMethods();
        var queryableMethod = methods.Any(m =>
            m.ReturnType.IsGenericType &&
            m.ReturnType.GetGenericTypeDefinition() == typeof(IQueryable<>));
        queryableMethod.Should().BeFalse("IReadRepository must not expose IQueryable");
    }

    // ---------------------------------------------------------------------------
    // IUnitOfWork contract
    // ---------------------------------------------------------------------------

    [Fact]
    public void IUnitOfWork_Has_SaveChangesAsync()
    {
        var method = typeof(IUnitOfWork).GetMethod("SaveChangesAsync");
        method.Should().NotBeNull("IUnitOfWork must expose SaveChangesAsync");
        method!.ReturnType.Should().Be(typeof(Task<int>), "SaveChangesAsync must return Task<int>");
    }

    [Fact]
    public void IUnitOfWork_Has_Exactly_OneMethod()
    {
        var methods = typeof(IUnitOfWork).GetMethods();
        methods.Should().HaveCount(1, "IUnitOfWork must expose exactly one method: SaveChangesAsync");
    }

    // ---------------------------------------------------------------------------
    // IDbConnectionFactory contract
    // ---------------------------------------------------------------------------

    [Fact]
    public void IDbConnectionFactory_Has_CreateConnectionAsync()
    {
        var method = typeof(IDbConnectionFactory).GetMethod("CreateConnectionAsync");
        method.Should().NotBeNull("IDbConnectionFactory must expose CreateConnectionAsync");
    }

    [Fact]
    public void IDbConnectionFactory_CreateConnectionAsync_ReturnsTaskOfIDbConnection()
    {
        var method = typeof(IDbConnectionFactory).GetMethod("CreateConnectionAsync")!;
        method.ReturnType.Should().Be(typeof(Task<IDbConnection>));
    }

    // ---------------------------------------------------------------------------
    // ISpecificationEvaluator<T> contract
    // ---------------------------------------------------------------------------

    [Fact]
    public void ISpecificationEvaluator_Has_GetQuery()
    {
        var method = typeof(ISpecificationEvaluator<>).GetMethod("GetQuery");
        method.Should().NotBeNull("ISpecificationEvaluator must expose GetQuery");
    }

    // ---------------------------------------------------------------------------
    // IProjectionSpecification<TAggregate, TResult> contract
    // ---------------------------------------------------------------------------

    [Fact]
    public void IProjectionSpecification_Exists_In_Specifications_Namespace()
    {
        var type = typeof(IProjectionSpecification<,>);
        type.Should().NotBeNull("IProjectionSpecification<TAggregate, TResult> must exist in Abstractions");
        type.Namespace.Should().Be("SharedKernel.Persistence.Abstractions.Specifications");
    }

    [Fact]
    public void IProjectionSpecification_Extends_ISpecification()
    {
        var type = typeof(IProjectionSpecification<,>);
        var interfaces = type.GetInterfaces();
        var extendsSpec = interfaces.Any(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition() == typeof(SharedKernel.Domain.Specifications.ISpecification<>));
        extendsSpec.Should().BeTrue("IProjectionSpecification must extend ISpecification<TAggregate>");
    }

    [Fact]
    public void IProjectionSpecification_Has_Selector_Property_Of_ExpressionType()
    {
        var type = typeof(IProjectionSpecification<,>);
        var prop = type.GetProperty("Selector");
        prop.Should().NotBeNull("IProjectionSpecification must expose Selector property");
        // Return type should be Expression<Func<TAggregate, TResult>>
        prop!.PropertyType.IsGenericType.Should().BeTrue();
        prop.PropertyType.GetGenericTypeDefinition().Should().Be(typeof(Expression<>));
    }

    // ---------------------------------------------------------------------------
    // ByIdSpecification<TAggregate, TId> contract
    // ---------------------------------------------------------------------------

    [Fact]
    public void ByIdSpecification_Exists_In_Specifications_Namespace()
    {
        var type = typeof(ByIdSpecification<,>);
        type.Should().NotBeNull("ByIdSpecification<TAggregate, TId> must exist in Abstractions");
        type.Namespace.Should().Be("SharedKernel.Persistence.Abstractions.Specifications");
    }

    // ---------------------------------------------------------------------------
    // No outbox types allowed in this assembly
    // ---------------------------------------------------------------------------

    [Fact]
    public void Abstractions_Assembly_DoesNotContain_OutboxMessage()
    {
        var assembly = typeof(IRepository<,>).Assembly;
        var outboxTypes = assembly.GetTypes()
            .Where(t => t.Name.Contains("Outbox"))
            .ToList();

        outboxTypes.Should().BeEmpty(
            "Abstractions package must not contain any outbox types — outbox belongs to 07.Messaging");
    }

    [Fact]
    public void Abstractions_Assembly_DoesNotContain_IOutboxWriter()
    {
        var assembly = typeof(IRepository<,>).Assembly;
        var type = assembly.GetTypes().FirstOrDefault(t => t.Name == "IOutboxWriter");
        type.Should().BeNull("IOutboxWriter must not exist in Abstractions — it belongs to 07.Messaging");
    }

    // ---------------------------------------------------------------------------
    // No ORM NuGet dependency in Abstractions assembly
    // ---------------------------------------------------------------------------

    [Fact]
    public void Abstractions_Assembly_DoesNotReference_EntityFramework()
    {
        var assembly = typeof(IRepository<,>).Assembly;
        var efReference = assembly.GetReferencedAssemblies()
            .Any(r => r.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        efReference.Should().BeFalse(
            "Abstractions must have zero ORM NuGet dependencies — EF Core must not be referenced");
    }
}
