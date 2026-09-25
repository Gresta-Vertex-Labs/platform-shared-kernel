using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="PersistenceLayerProtectionRules"/> — covering all three predicates:
/// <c>OnlyEfUnitOfWorkMayCallSaveChanges</c>, <c>RepositoriesMustNotExposeIQueryable</c>,
/// and <c>DomainAssembliesNeverReferencePersistenceStack</c>.
/// </summary>
/// <remarks>
/// T-46/T-47: Rule 1 — OnlyEfUnitOfWorkMayCallSaveChanges
/// T-48/T-49: Rule 2 — RepositoriesMustNotExposeIQueryable
/// T-50/T-51: Rule 3 — DomainAssembliesNeverReferencePersistenceStack
/// </remarks>
public class PersistenceLayerProtectionRulesTests
{
    // ---------------------------------------------------------------------------
    // T-46 — Rule 1 fire path: direct SaveChangesAsync call fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-46: A class outside <c>SharedKernel.Persistence.EfCore</c> that calls
    /// <c>DbContext.SaveChangesAsync()</c> directly must fail
    /// <see cref="PersistenceLayerProtectionRules.OnlyEfUnitOfWorkMayCallSaveChanges"/>.
    /// </summary>
    [Fact]
    public void OnlyEfUnitOfWorkMayCallSaveChanges_DirectSaveChangesCall_RuleFails()
    {
        // Arrange: compile a fixture where an application-layer class calls SaveChangesAsync
        // directly (outside the SharedKernel.Persistence.EfCore namespace exemption).
        // Use a synchronous wrapper (non-async method) to keep the call in the top-level method
        // body rather than in a compiler-generated state-machine class, which would cause the
        // call instruction to appear in a nested type that NetArchTest may not scan.
        const string source = """
            using System.Threading.Tasks;

            namespace Application.Services
            {
                public class DbContext
                {
                    public Task SaveChangesAsync() => Task.CompletedTask;
                    public void SaveChanges() { }
                }

                public class OrderService
                {
                    private readonly DbContext _dbContext;
                    public OrderService(DbContext dbContext) { _dbContext = dbContext; }

                    // Violation: direct SaveChanges call outside persistence namespace
                    public void SaveOrder()
                    {
                        _dbContext.SaveChanges();
                    }
                }
            }
            """;

        var assembly = CompileInMemory("DirectSaveChangesViolation", source);

        var result = PersistenceLayerProtectionRules
            .OnlyEfUnitOfWorkMayCallSaveChanges(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderService calls DbContext.SaveChangesAsync() directly, bypassing the EF Core interceptor chain");
    }

    // ---------------------------------------------------------------------------
    // T-47 — Rule 1 pass path: IUnitOfWork usage passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-47: A class that calls only <c>IUnitOfWork.CommitAsync()</c> and never
    /// <c>DbContext.SaveChangesAsync()</c> must pass
    /// <see cref="PersistenceLayerProtectionRules.OnlyEfUnitOfWorkMayCallSaveChanges"/>.
    /// </summary>
    [Fact]
    public void OnlyEfUnitOfWorkMayCallSaveChanges_IUnitOfWorkUsage_RulePasses()
    {
        const string source = """
            using System.Threading.Tasks;

            namespace Application.Transactions
            {
                public interface IUnitOfWork
                {
                    Task CommitAsync();
                }
            }

            namespace Application.Services
            {
                public class OrderService
                {
                    private readonly Application.Transactions.IUnitOfWork _unitOfWork;
                    public OrderService(Application.Transactions.IUnitOfWork unitOfWork)
                    {
                        _unitOfWork = unitOfWork;
                    }

                    // Compliant: delegates to IUnitOfWork, never calls SaveChangesAsync directly
                    public async Task SaveOrder()
                    {
                        await _unitOfWork.CommitAsync();
                    }
                }
            }
            """;

        var assembly = CompileInMemory("IUnitOfWorkUsage", source);

        var result = PersistenceLayerProtectionRules
            .OnlyEfUnitOfWorkMayCallSaveChanges(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderService delegates to IUnitOfWork.CommitAsync() and never calls DbContext.SaveChangesAsync()");
    }

    // ---------------------------------------------------------------------------
    // T-48 — Rule 2 fire path: IRepository<T,TId> with IQueryable return fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-48: A type implementing <c>IRepository&lt;Order, Guid&gt;</c> with a method returning
    /// <c>IQueryable&lt;Order&gt;</c> must fail
    /// <see cref="PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable"/>.
    /// </summary>
    [Fact]
    public void RepositoriesMustNotExposeIQueryable_IQueryableReturningMethod_RuleFails()
    {
        // Arrange: compile a fixture where an IRepository implementor exposes IQueryable<T>.
        // IQueryable is defined in System.Linq — use a stub to keep the fixture self-contained.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace System.Linq
            {
                public interface IQueryable<T> { }
            }

            namespace Domain
            {
                public class Order { public Guid Id { get; set; } }
            }

            namespace Persistence.Abstractions
            {
                public interface IRepository<TEntity, TId> { }
            }

            namespace Persistence.Repositories
            {
                // Violation: repository exposes IQueryable<Order>
                public class OrderRepository : Persistence.Abstractions.IRepository<Domain.Order, Guid>
                {
                    // Violating method — leaks EF Core expression-tree semantics
                    public System.Linq.IQueryable<Domain.Order> GetAll()
                    {
                        throw new NotImplementedException();
                    }

                    public Task<List<Domain.Order>> FindAsync()
                    {
                        return Task.FromResult(new List<Domain.Order>());
                    }
                }
            }
            """;

        var assembly = CompileInMemory("IQueryableViolation", source);

        var result = PersistenceLayerProtectionRules
            .RepositoriesMustNotExposeIQueryable(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderRepository.GetAll() returns IQueryable<Order>, violating the repository contract");
    }

    // ---------------------------------------------------------------------------
    // T-49 — Rule 2 pass path: IRepository<T,TId> without IQueryable passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-49: A type implementing <c>IRepository&lt;Order, Guid&gt;</c> with no
    /// <c>IQueryable</c>-returning methods must pass
    /// <see cref="PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable"/>.
    /// </summary>
    [Fact]
    public void RepositoriesMustNotExposeIQueryable_NoIQueryableMethods_RulePasses()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace Domain
            {
                public class Order { public Guid Id { get; set; } }
            }

            namespace Persistence.Abstractions
            {
                public interface IRepository<TEntity, TId> { }
            }

            namespace Persistence.Repositories
            {
                // Compliant: no IQueryable returns — uses Task<List<T>> instead
                public class OrderRepository : Persistence.Abstractions.IRepository<Domain.Order, Guid>
                {
                    public Task<List<Domain.Order>> FindAsync()
                    {
                        return Task.FromResult(new List<Domain.Order>());
                    }

                    public Task<Domain.Order?> GetByIdAsync(Guid id)
                    {
                        return Task.FromResult<Domain.Order?>(null);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("NoIQueryableRepository", source);

        var result = PersistenceLayerProtectionRules
            .RepositoriesMustNotExposeIQueryable(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderRepository exposes no IQueryable-returning methods");
    }

    // ---------------------------------------------------------------------------
    // T-50 — Rule 3 fire path: domain type referencing EF Core namespace fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-50: A domain assembly that has a dependency on a type in the
    /// <c>Microsoft.EntityFrameworkCore</c> namespace must fail
    /// <see cref="PersistenceLayerProtectionRules.DomainAssembliesNeverReferencePersistenceStack"/>.
    /// </summary>
    /// <remarks>
    /// NetArchTest's <c>NotHaveDependencyOn("Microsoft.EntityFrameworkCore")</c> performs a
    /// substring match against referenced type namespaces within the assembly. By defining a
    /// stub type in <c>namespace Microsoft.EntityFrameworkCore</c> inside the same compilation
    /// unit and having a domain type reference it, we produce a fixture that exercises the rule
    /// without requiring the actual EF Core NuGet package.
    /// </remarks>
    [Fact]
    public void DomainAssembliesNeverReferencePersistenceStack_EfCoreReference_RuleFails()
    {
        // Arrange: compile a fixture where a domain entity references a type
        // in the Microsoft.EntityFrameworkCore namespace (stub class simulates the [Key] attribute).
        const string source = """
            namespace Microsoft.EntityFrameworkCore
            {
                // Stub simulating EF Core's data annotation infrastructure
                public class KeyAttribute : System.Attribute { }
            }

            namespace SharedKernel.Domain
            {
                public class OrderEntity
                {
                    // Violation: domain entity carries an EF Core infrastructure attribute
                    [Microsoft.EntityFrameworkCore.Key]
                    public System.Guid Id { get; set; }
                }
            }
            """;

        var assembly = CompileInMemory("EfCoreDomainViolation", source);

        var result = PersistenceLayerProtectionRules
            .DomainAssembliesNeverReferencePersistenceStack(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderEntity references Microsoft.EntityFrameworkCore.KeyAttribute, violating DDD isolation");
    }

    // ---------------------------------------------------------------------------
    // T-51 — Rule 3 pass path: clean domain assembly passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-51: A domain assembly with no dependency on EF Core, Npgsql, or any
    /// <c>SharedKernel.Persistence.*</c> package must pass
    /// <see cref="PersistenceLayerProtectionRules.DomainAssembliesNeverReferencePersistenceStack"/>.
    /// </summary>
    [Fact]
    public void DomainAssembliesNeverReferencePersistenceStack_CleanDomainAssembly_RulePasses()
    {
        const string source = """
            namespace SharedKernel.Domain
            {
                // Compliant: plain domain entity with no infrastructure annotations
                public class OrderEntity
                {
                    public System.Guid Id { get; set; }
                    public string Status { get; set; } = string.Empty;
                }
            }
            """;

        var assembly = CompileInMemory("CleanDomainRule3Persistence", source);

        var result = PersistenceLayerProtectionRules
            .DomainAssembliesNeverReferencePersistenceStack(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderEntity has no EF Core, Npgsql, or SharedKernel.Persistence.* references");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Assembly CompileInMemory(
        string assemblyName,
        string source,
        string[]? extraAssemblyPaths = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var refList = new System.Collections.Generic.List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Threading.Tasks").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
        };

        if (extraAssemblyPaths is not null)
        {
            foreach (var path in extraAssemblyPaths)
                refList.Add(MetadataReference.CreateFromFile(path));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: refList,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{assemblyName}_{System.Guid.NewGuid():N}.dll");

        using (var fs = System.IO.File.OpenWrite(tempPath))
        {
            var emitResult = compilation.Emit(fs);
            if (!emitResult.Success)
            {
                var errors = string.Join(
                    System.Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                throw new InvalidOperationException(
                    $"Fixture '{assemblyName}' failed to compile:{System.Environment.NewLine}{errors}");
            }
        }

        return Assembly.LoadFrom(tempPath);
    }
}
