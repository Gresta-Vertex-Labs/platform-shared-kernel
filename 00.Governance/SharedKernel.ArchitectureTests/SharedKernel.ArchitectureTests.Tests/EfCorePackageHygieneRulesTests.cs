using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="EfCorePackageHygieneRules"/> — covering all three predicates:
/// <c>NoSpecificationEvaluatorDowncastInEfCoreAssembly</c>,
/// <c>IUnitOfWorkImplementorsMustHaveExactlyOneConstructor</c>, and
/// <c>ApplicationLayerMustNotReferenceDbContextTransaction</c>.
/// </summary>
/// <remarks>
/// T-67/T-68: Rule 1 — NoSpecificationEvaluatorDowncastInEfCoreAssembly
/// T-69/T-70: Rule 2 — IUnitOfWorkImplementorsMustHaveExactlyOneConstructor
/// T-71/T-72: Rule 3 — ApplicationLayerMustNotReferenceDbContextTransaction
/// </remarks>
public class EfCorePackageHygieneRulesTests
{
    // ---------------------------------------------------------------------------
    // T-67 — Rule 1 fire path: castclass to SpecificationEvaluator fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-67: A type whose method body contains a <c>castclass</c> targeting a type named
    /// <c>SpecificationEvaluator&lt;T&gt;</c> must fail
    /// <see cref="EfCorePackageHygieneRules.NoSpecificationEvaluatorDowncastInEfCoreAssembly"/>.
    /// The failure must identify the offending type and method.
    /// </summary>
    [Fact]
    public void NoSpecificationEvaluatorDowncast_CastclassToSpecificationEvaluator_RuleFails()
    {
        // Arrange: compile a fixture where a repository casts ISpecificationEvaluator<T>
        // to its concrete SpecificationEvaluator<T> form.
        // The cast is explicit in C# via a cast expression: (SpecificationEvaluator<T>)_evaluator;
        // The C# compiler emits a castclass IL instruction for this pattern.
        const string source = """
            namespace Persistence.Evaluators
            {
                public interface ISpecificationEvaluator<T> { }

                // Concrete type that the anti-pattern casts to
                public class SpecificationEvaluator<T> : ISpecificationEvaluator<T> { }
            }

            namespace Persistence.Repositories
            {
                // Violation: performs a concrete downcast of ISpecificationEvaluator<T>
                public class OrderRepository
                {
                    private readonly Persistence.Evaluators.ISpecificationEvaluator<object> _evaluator;

                    public OrderRepository(Persistence.Evaluators.ISpecificationEvaluator<object> evaluator)
                    {
                        _evaluator = evaluator;
                    }

                    // This method performs the forbidden castclass
                    public void GetOrders()
                    {
                        // Explicit cast → castclass IL opcode targeting SpecificationEvaluator<T>
                        var concrete = (Persistence.Evaluators.SpecificationEvaluator<object>)_evaluator;
                        _ = concrete;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SpecEvaluatorDowncastViolation", source);

        var result = EfCorePackageHygieneRules
            .NoSpecificationEvaluatorDowncastInEfCoreAssembly(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderRepository.GetOrders() performs a concrete castclass to SpecificationEvaluator<T>, " +
                     "which was eliminated in P-097 by adding GetProjectedQuery to the interface");
    }

    // ---------------------------------------------------------------------------
    // T-68 — Rule 1 pass path: interface-only usage passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-68: A type that uses <c>ISpecificationEvaluator&lt;T&gt;</c> exclusively via its
    /// interface (no <c>castclass</c> to the concrete type) must pass
    /// <see cref="EfCorePackageHygieneRules.NoSpecificationEvaluatorDowncastInEfCoreAssembly"/>.
    /// </summary>
    [Fact]
    public void NoSpecificationEvaluatorDowncast_InterfaceOnlyUsage_RulePasses()
    {
        const string source = """
            using System.Collections.Generic;

            namespace Persistence.Evaluators
            {
                public interface ISpecificationEvaluator<T>
                {
                    IEnumerable<T> Evaluate(IEnumerable<T> query);
                }
            }

            namespace Persistence.Repositories
            {
                // Compliant: uses the evaluator only through its interface
                public class OrderRepository
                {
                    private readonly Persistence.Evaluators.ISpecificationEvaluator<object> _evaluator;

                    public OrderRepository(Persistence.Evaluators.ISpecificationEvaluator<object> evaluator)
                    {
                        _evaluator = evaluator;
                    }

                    // Uses the evaluator via interface — no castclass emitted
                    public System.Collections.Generic.IEnumerable<object> GetOrders(
                        System.Collections.Generic.List<object> source)
                    {
                        return _evaluator.Evaluate(source);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("SpecEvaluatorInterfaceOnly", source);

        var result = EfCorePackageHygieneRules
            .NoSpecificationEvaluatorDowncastInEfCoreAssembly(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderRepository uses ISpecificationEvaluator<T> exclusively via its interface — " +
                     "no castclass to SpecificationEvaluator<T> is emitted");
    }

    // ---------------------------------------------------------------------------
    // T-69 — Rule 2 fire path: IUnitOfWork implementor with two constructors fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-69: An <c>IUnitOfWork</c> implementor with two public instance constructors must fail
    /// <see cref="EfCorePackageHygieneRules.IUnitOfWorkImplementorsMustHaveExactlyOneConstructor"/>.
    /// The failure must identify the offending type and the actual constructor count.
    /// </summary>
    [Fact]
    public void IUnitOfWorkImplementorsMustHaveExactlyOneConstructor_TwoConstructors_RuleFails()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            namespace Application.Abstractions
            {
                public interface IUnitOfWork
                {
                    Task CommitAsync(CancellationToken ct = default);
                }
            }

            namespace Infrastructure
            {
                public class AppDbContext { }

                // Violation: IUnitOfWork implementor with two public instance constructors
                public class EfUnitOfWork : Application.Abstractions.IUnitOfWork
                {
                    // Constructor 1 — the intended DI constructor
                    public EfUnitOfWork(AppDbContext dbContext) { }

                    // Constructor 2 — regression: DI ambiguity (P-098)
                    public EfUnitOfWork() { }

                    public Task CommitAsync(CancellationToken ct = default)
                        => Task.CompletedTask;
                }
            }
            """;

        var assembly = CompileInMemory("TwoConstructorsViolation", source);

        var result = EfCorePackageHygieneRules
            .IUnitOfWorkImplementorsMustHaveExactlyOneConstructor(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "EfUnitOfWork has two public instance constructors, causing DI ambiguity (P-098 regression)");
    }

    // ---------------------------------------------------------------------------
    // T-70 — Rule 2 pass path: IUnitOfWork implementor with one constructor passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-70: An <c>IUnitOfWork</c> implementor with exactly one public instance constructor
    /// must pass <see cref="EfCorePackageHygieneRules.IUnitOfWorkImplementorsMustHaveExactlyOneConstructor"/>.
    /// </summary>
    [Fact]
    public void IUnitOfWorkImplementorsMustHaveExactlyOneConstructor_SingleConstructor_RulePasses()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            namespace Application.Abstractions
            {
                public interface IUnitOfWork
                {
                    Task CommitAsync(CancellationToken ct = default);
                }
            }

            namespace Infrastructure
            {
                public class AppDbContext { }

                // Compliant: exactly one public instance constructor
                public class EfUnitOfWork : Application.Abstractions.IUnitOfWork
                {
                    public EfUnitOfWork(AppDbContext dbContext) { }

                    public Task CommitAsync(CancellationToken ct = default)
                        => Task.CompletedTask;
                }
            }
            """;

        var assembly = CompileInMemory("SingleConstructorCompliant", source);

        var result = EfCorePackageHygieneRules
            .IUnitOfWorkImplementorsMustHaveExactlyOneConstructor(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "EfUnitOfWork has exactly one public instance constructor — no DI ambiguity");
    }

    // ---------------------------------------------------------------------------
    // T-71 — Rule 3 fire path: IDbContextTransaction constructor parameter fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-71: An application class with <c>IDbContextTransaction</c> as a constructor parameter
    /// type must fail
    /// <see cref="EfCorePackageHygieneRules.ApplicationLayerMustNotReferenceDbContextTransaction"/>.
    /// The failure must identify the offending type and the declaration site.
    /// </summary>
    [Fact]
    public void ApplicationLayerMustNotReferenceDbContextTransaction_CtorParameterViolation_RuleFails()
    {
        // Arrange: stub out IDbContextTransaction with the expected namespace/name so the
        // FullName check ("IDbContextTransaction" substring) fires correctly.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            namespace Microsoft.EntityFrameworkCore.Storage
            {
                // Stub simulating EF Core's IDbContextTransaction
                public interface IDbContextTransaction : System.IDisposable
                {
                    Task CommitAsync(CancellationToken ct = default);
                }
            }

            namespace Application.Commands
            {
                // Violation: application handler directly injects IDbContextTransaction
                public class CreateOrderHandler
                {
                    private readonly Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction _tx;

                    // The forbidden constructor parameter — application layer must not use this
                    public CreateOrderHandler(
                        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx)
                    {
                        _tx = tx;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("DbContextTransactionViolation", source);

        var result = EfCorePackageHygieneRules
            .ApplicationLayerMustNotReferenceDbContextTransaction(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "CreateOrderHandler accepts IDbContextTransaction as a constructor parameter, " +
                     "which couples the application layer directly to EF Core's transaction implementation");
    }

    // ---------------------------------------------------------------------------
    // T-72 — Rule 3 pass path: ITransactionalUnitOfWork usage, no IDbContextTransaction
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-72: An application class with <c>ITransactionalUnitOfWork</c> as its only
    /// transaction-related constructor parameter, and no references to
    /// <c>IDbContextTransaction</c>, must pass
    /// <see cref="EfCorePackageHygieneRules.ApplicationLayerMustNotReferenceDbContextTransaction"/>.
    /// </summary>
    [Fact]
    public void ApplicationLayerMustNotReferenceDbContextTransaction_ITransactionalUnitOfWork_RulePasses()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            namespace Application.Abstractions
            {
                // The compliant transaction abstraction (P-099)
                public interface ITransactionalUnitOfWork
                {
                    Task CommitAsync(CancellationToken ct = default);
                }
            }

            namespace Application.Commands
            {
                // Compliant: uses the platform abstraction — no IDbContextTransaction reference
                public class CreateOrderHandler
                {
                    private readonly Application.Abstractions.ITransactionalUnitOfWork _unitOfWork;

                    public CreateOrderHandler(
                        Application.Abstractions.ITransactionalUnitOfWork unitOfWork)
                    {
                        _unitOfWork = unitOfWork;
                    }

                    public Task HandleAsync(CancellationToken ct = default)
                    {
                        return _unitOfWork.CommitAsync(ct);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("TransactionalUnitOfWorkCompliant", source);

        var result = EfCorePackageHygieneRules
            .ApplicationLayerMustNotReferenceDbContextTransaction(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "CreateOrderHandler uses ITransactionalUnitOfWork (P-099) and contains " +
                     "no reference to IDbContextTransaction");
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
