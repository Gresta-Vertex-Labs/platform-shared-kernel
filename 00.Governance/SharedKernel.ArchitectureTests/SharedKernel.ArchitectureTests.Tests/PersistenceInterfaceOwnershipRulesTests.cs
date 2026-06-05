using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="PersistenceInterfaceOwnershipRules"/> — covering all four predicates:
/// <c>IUserContextDeclaredOnlyInSecurityAbstractions</c>,
/// <c>TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions</c>,
/// <c>IReadRepositoryMustNotExposeIQueryable</c>, and
/// <c>NoGetByIdAsyncOnReadRepository</c>.
/// </summary>
/// <remarks>
/// T-52/T-53: Rule 1 — IUserContextDeclaredOnlyInSecurityAbstractions
/// T-54/T-55: Rule 2 — TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions
/// T-56/T-57: Rule 3 — IReadRepositoryMustNotExposeIQueryable
/// T-58/T-59: Rule 4 — NoGetByIdAsyncOnReadRepository
/// </remarks>
public class PersistenceInterfaceOwnershipRulesTests
{
    // ---------------------------------------------------------------------------
    // T-52 — Rule 1 fire path: IUserContext declared outside Security.Abstractions fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-52: An assembly that declares a type named <c>IUserContext</c> (outside
    /// <c>SharedKernel.Security.Abstractions</c>) must fail
    /// <see cref="PersistenceInterfaceOwnershipRules.IUserContextDeclaredOnlyInSecurityAbstractions"/>.
    /// </summary>
    [Fact]
    public void IUserContextDeclaredOnlyInSecurityAbstractions_InterfaceDeclaredOutsideOwner_RuleFails()
    {
        // Arrange: compile a fixture where IUserContext is declared in the persistence namespace
        // rather than in SharedKernel.Security.Abstractions.
        const string source = """
            using System;

            namespace SharedKernel.Persistence.EfCore
            {
                // Violation: IUserContext re-declared outside its owner assembly
                public interface IUserContext
                {
                    Guid UserId { get; }
                    string UserName { get; }
                }
            }
            """;

        var assembly = CompileInMemory("IUserContextViolation", source);

        var result = PersistenceInterfaceOwnershipRules
            .IUserContextDeclaredOnlyInSecurityAbstractions(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "IUserContext is declared in SharedKernel.Persistence.EfCore, " +
                     "not in SharedKernel.Security.Abstractions where it belongs after P-078");
    }

    // ---------------------------------------------------------------------------
    // T-53 — Rule 1 pass path: assembly with no IUserContext declaration passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-53: An assembly that does not declare any type named <c>IUserContext</c> must pass
    /// <see cref="PersistenceInterfaceOwnershipRules.IUserContextDeclaredOnlyInSecurityAbstractions"/>.
    /// </summary>
    [Fact]
    public void IUserContextDeclaredOnlyInSecurityAbstractions_NoIUserContextDeclaration_RulePasses()
    {
        const string source = """
            using System;

            namespace Application.Services
            {
                // Compliant: no IUserContext declaration here; references come from Security.Abstractions
                public interface IOrderService
                {
                    void PlaceOrder(Guid orderId);
                }
            }
            """;

        var assembly = CompileInMemory("NoIUserContextClean", source);

        var result = PersistenceInterfaceOwnershipRules
            .IUserContextDeclaredOnlyInSecurityAbstractions(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "The assembly declares no IUserContext type — the rule should pass");
    }

    // ---------------------------------------------------------------------------
    // T-54 — Rule 2 fire path: ITenantProvider declared outside Security.Abstractions fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-54: An assembly that declares a type named <c>ITenantProvider</c> outside
    /// <c>SharedKernel.Security.Abstractions</c> must fail
    /// <see cref="PersistenceInterfaceOwnershipRules.TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions"/>.
    /// </summary>
    [Fact]
    public void TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions_ITenantProviderDeclaredOutsideOwner_RuleFails()
    {
        const string source = """
            using System;

            namespace SharedKernel.Persistence.EfCore
            {
                // Violation: ITenantProvider re-declared outside SharedKernel.Security.Abstractions
                public interface ITenantProvider
                {
                    Guid TenantId { get; }
                }
            }
            """;

        var assembly = CompileInMemory("ITenantProviderViolation", source);

        var result = PersistenceInterfaceOwnershipRules
            .TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "ITenantProvider is declared outside SharedKernel.Security.Abstractions, " +
                     "violating the single-source-of-truth principle for tenant identity contracts");
    }

    // ---------------------------------------------------------------------------
    // T-55 — Rule 2 pass path: assemblies with no tenant-identity declarations pass
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-55: An assembly that declares neither <c>ITenantProvider</c> nor
    /// <c>ICurrentTenantService</c> must pass
    /// <see cref="PersistenceInterfaceOwnershipRules.TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions"/>.
    /// </summary>
    [Fact]
    public void TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions_NoTenantIdentityDeclarations_RulePasses()
    {
        const string source = """
            using System;

            namespace Application.MultiTenancy
            {
                // Compliant: uses the tenant service via its owner assembly reference,
                // does not re-declare the interface locally
                public class TenantAwareOrderService
                {
                    public Guid GetCurrentTenantId() => Guid.Empty;
                }
            }
            """;

        var assembly = CompileInMemory("NoTenantIdentityClean", source);

        var result = PersistenceInterfaceOwnershipRules
            .TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "The assembly declares neither ITenantProvider nor ICurrentTenantService");
    }

    // ---------------------------------------------------------------------------
    // T-56 — Rule 3 fire path: IReadRepository implementor with IQueryable method fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-56: A type implementing <c>IReadRepository&lt;Order, Guid&gt;</c> with a method
    /// returning <c>IQueryable&lt;Order&gt;</c> must fail
    /// <see cref="PersistenceInterfaceOwnershipRules.IReadRepositoryMustNotExposeIQueryable"/>.
    /// </summary>
    [Fact]
    public void IReadRepositoryMustNotExposeIQueryable_IQueryableReturningMethod_RuleFails()
    {
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
                public interface IReadRepository<TEntity, TId> { }
            }

            namespace Persistence.Repositories
            {
                // Violation: IReadRepository implementor exposes IQueryable<Order>
                public class OrderReadRepository
                    : Persistence.Abstractions.IReadRepository<Domain.Order, Guid>
                {
                    // Offending method — leaks EF Core expression-tree semantics into application layer
                    public System.Linq.IQueryable<Domain.Order> GetAll()
                    {
                        throw new NotImplementedException();
                    }

                    public Task<Domain.Order?> FindByIdAsync(Guid id)
                    {
                        return Task.FromResult<Domain.Order?>(null);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("ReadRepositoryIQueryableViolation", source);

        var result = PersistenceInterfaceOwnershipRules
            .IReadRepositoryMustNotExposeIQueryable(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderReadRepository.GetAll() returns IQueryable<Order>, " +
                     "violating the IReadRepository contract — query surface must use Specification<T>");
    }

    // ---------------------------------------------------------------------------
    // T-57 — Rule 3 pass path: clean IReadRepository implementor passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-57: A type implementing <c>IReadRepository&lt;Order, Guid&gt;</c> with no
    /// <c>IQueryable</c>-returning methods must pass
    /// <see cref="PersistenceInterfaceOwnershipRules.IReadRepositoryMustNotExposeIQueryable"/>.
    /// </summary>
    [Fact]
    public void IReadRepositoryMustNotExposeIQueryable_NoIQueryableMethods_RulePasses()
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
                public interface IReadRepository<TEntity, TId> { }
            }

            namespace Persistence.Repositories
            {
                // Compliant: no IQueryable returns — uses Task-based async methods
                public class OrderReadRepository
                    : Persistence.Abstractions.IReadRepository<Domain.Order, Guid>
                {
                    public Task<Domain.Order?> FindByIdAsync(Guid id)
                    {
                        return Task.FromResult<Domain.Order?>(null);
                    }

                    public Task<IReadOnlyList<Domain.Order>> FindAsync()
                    {
                        return Task.FromResult<IReadOnlyList<Domain.Order>>(new List<Domain.Order>());
                    }
                }
            }
            """;

        var assembly = CompileInMemory("ReadRepositoryNoIQueryable", source);

        var result = PersistenceInterfaceOwnershipRules
            .IReadRepositoryMustNotExposeIQueryable(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderReadRepository exposes no IQueryable-returning methods");
    }

    // ---------------------------------------------------------------------------
    // T-58 — Rule 4 fire path: IReadRepository implementor with GetByIdAsync fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-58: A class implementing <c>IReadRepository&lt;Order, Guid&gt;</c> that declares
    /// <c>GetByIdAsync</c> must fail
    /// <see cref="PersistenceInterfaceOwnershipRules.NoGetByIdAsyncOnReadRepository"/>.
    /// </summary>
    [Fact]
    public void NoGetByIdAsyncOnReadRepository_GetByIdAsyncDeclared_RuleFails()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Domain
            {
                public class Order { public Guid Id { get; set; } }
            }

            namespace Persistence.Abstractions
            {
                public interface IReadRepository<TEntity, TId> { }
            }

            namespace Persistence.Repositories
            {
                // Violation: GetByIdAsync was removed from IReadRepository in P-080
                public class OrderReadRepository
                    : Persistence.Abstractions.IReadRepository<Domain.Order, Guid>
                {
                    // Offending method — re-introduces the anti-pattern removed in P-080
                    public Task<Domain.Order?> GetByIdAsync(
                        Guid id, CancellationToken ct = default)
                    {
                        return Task.FromResult<Domain.Order?>(null);
                    }

                    // Correct alternative names
                    public Task<Domain.Order?> GetAsync(
                        Guid id, CancellationToken ct = default)
                    {
                        return Task.FromResult<Domain.Order?>(null);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("GetByIdAsyncViolation", source);

        var result = PersistenceInterfaceOwnershipRules
            .NoGetByIdAsyncOnReadRepository(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderReadRepository declares GetByIdAsync, which was removed from " +
                     "IReadRepository in P-080 — use FindByIdAsync or GetAsync instead");
    }

    // ---------------------------------------------------------------------------
    // T-59 — Rule 4 pass path: IReadRepository implementor without GetByIdAsync passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-59: A class implementing <c>IReadRepository&lt;Order, Guid&gt;</c> that does not
    /// declare <c>GetByIdAsync</c> must pass
    /// <see cref="PersistenceInterfaceOwnershipRules.NoGetByIdAsyncOnReadRepository"/>.
    /// </summary>
    [Fact]
    public void NoGetByIdAsyncOnReadRepository_NoGetByIdAsyncMethod_RulePasses()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Domain
            {
                public class Order { public Guid Id { get; set; } }
            }

            namespace Persistence.Abstractions
            {
                public interface IReadRepository<TEntity, TId> { }
            }

            namespace Persistence.Repositories
            {
                // Compliant: uses the correct P-080 method names — no GetByIdAsync
                public class OrderReadRepository
                    : Persistence.Abstractions.IReadRepository<Domain.Order, Guid>
                {
                    // Correct: GetAsync returns T?
                    public Task<Domain.Order?> GetAsync(
                        Guid id, CancellationToken ct = default)
                    {
                        return Task.FromResult<Domain.Order?>(null);
                    }

                    // Correct: FindByIdAsync returns Result<T> equivalent
                    public Task<IReadOnlyList<Domain.Order>> FindAsync(
                        CancellationToken ct = default)
                    {
                        return Task.FromResult<IReadOnlyList<Domain.Order>>(new List<Domain.Order>());
                    }
                }
            }
            """;

        var assembly = CompileInMemory("NoGetByIdAsyncClean", source);

        var result = PersistenceInterfaceOwnershipRules
            .NoGetByIdAsyncOnReadRepository(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderReadRepository uses GetAsync / FindAsync — no GetByIdAsync declared");
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
