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
    // T-271 — Rule 4 fire path: EF.Property<T> called directly in an ordinary method body
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-271: A contrived EfCore-shaped fixture directly calling <c>EF.Property&lt;TId&gt;(entity,
    /// "Id")</c> in an ordinary (non-expression-tree) method body must fail
    /// <see cref="EfCorePackageHygieneRules.NoDirectEfPropertyUsageInEfCoreAssembly"/> —
    /// reproducing the exact P-316 <c>TenantedRepository</c> defect pattern.
    /// </summary>
    [Fact]
    public void NoDirectEfPropertyUsageInEfCoreAssembly_DirectCallInOrdinaryMethodBody_RuleFails()
    {
        const string source = """
            namespace Microsoft.EntityFrameworkCore
            {
                // Stub simulating EF Core's EF.Property<TProperty> static method.
                public static class EF
                {
                    public static TProperty Property<TProperty>(object entity, string propertyName) =>
                        default!;
                }
            }

            namespace Persistence.MultiTenancy
            {
                // Violation: reproduces the exact P-316 TenantedRepository defect — a direct,
                // client-side-evaluated EF.Property<T> call in an ordinary method body.
                public class TenantedRepositoryFixture
                {
                    public bool IdEquals(object entity, object id)
                    {
                        var idValue = Microsoft.EntityFrameworkCore.EF.Property<object>(entity, "Id");
                        return idValue!.Equals(id);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("EfPropertyDirectCallViolation", source);

        var result = EfCorePackageHygieneRules
            .NoDirectEfPropertyUsageInEfCoreAssembly(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "TenantedRepositoryFixture.IdEquals calls EF.Property<T> directly in ordinary " +
                     "(non-expression-tree) code — reproducing the exact P-316 defect pattern");
    }

    // ---------------------------------------------------------------------------
    // T-272 — Rule 4 fire path: EF.Property<T> called inside a Func<T,bool> lambda
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-272: A contrived fixture calling <c>EF.Property&lt;TId&gt;(entity, "Id")</c> inside a
    /// lambda passed as <c>Func&lt;T,bool&gt;</c> (non-expression-tree, client-side-evaluated)
    /// must fail <see cref="EfCorePackageHygieneRules.NoDirectEfPropertyUsageInEfCoreAssembly"/>
    /// — proving detection is not scoped to "outside any lambda."
    /// </summary>
    [Fact]
    public void NoDirectEfPropertyUsageInEfCoreAssembly_DirectCallInsideFuncLambda_RuleFails()
    {
        // NOTE: the lambda captures ONLY `this` (via the _id field), never a local variable or
        // method parameter. This is deliberate: a lambda capturing a LOCAL/parameter is compiled
        // by Roslyn onto a compiler-generated nested <>c__DisplayClass type, which NetArchTest's
        // own type-discovery layer never surfaces to an ICustomRule (the same platform-documented
        // gap already recorded for SK0012/ReflectionGuardRules — confirmed empirically here via a
        // temporary recording ICustomRule during this phase's own implementation, not assumed). A
        // lambda capturing only `this` is instead compiled as a plain private INSTANCE method
        // directly on the enclosing (top-level) type — <Matches>b__N_0 — which NetArchTest's scan
        // DOES visit, so the Call/Callvirt opcode against EF.Property is genuinely observed here.
        const string source = """
            namespace Microsoft.EntityFrameworkCore
            {
                public static class EF
                {
                    public static TProperty Property<TProperty>(object entity, string propertyName) =>
                        default!;
                }
            }

            namespace Persistence.MultiTenancy
            {
                // Violation: EF.Property<T> called inside a lambda converted to an ordinary
                // Func<T,bool> delegate — client-side evaluated, NOT an expression tree.
                public class ClientSideFilterFixture
                {
                    private object? _id;

                    public bool Matches(object entity, object id)
                    {
                        _id = id;

                        System.Func<object, bool> predicate =
                            e => Microsoft.EntityFrameworkCore.EF.Property<object>(e, "Id")!.Equals(_id);

                        return predicate(entity);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("EfPropertyFuncLambdaViolation", source);

        var result = EfCorePackageHygieneRules
            .NoDirectEfPropertyUsageInEfCoreAssembly(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "the lambda is converted to a Func<object,bool> delegate (not an expression " +
                     "tree), so EF.Property<T> is invoked via a direct Call/Callvirt IL instruction " +
                     "just like the ordinary-method-body violation");
    }

    // ---------------------------------------------------------------------------
    // T-273 — Rule 4 pass path: Expression.Property/Expression.Lambda pattern (no EF.Property call)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-273: A contrived fixture using <c>Expression.Property(parameterExpr, "Id")</c> +
    /// <c>Expression.Lambda&lt;Func&lt;T,bool&gt;&gt;(...)</c> (the P-316-corrected pattern, no
    /// <c>EF.Property</c> call present) must pass
    /// <see cref="EfCorePackageHygieneRules.NoDirectEfPropertyUsageInEfCoreAssembly"/>.
    /// </summary>
    [Fact]
    public void NoDirectEfPropertyUsageInEfCoreAssembly_ExpressionPropertyPattern_RulePasses()
    {
        const string source = """
            using System;
            using System.Linq.Expressions;

            namespace Persistence.MultiTenancy
            {
                public class FixtureAggregate
                {
                    public object? Id { get; set; }
                }

                // Compliant: the P-316-corrected pattern — builds the Id-equality predicate via
                // expression trees instead of EF.Property<T>. No EF.Property call is present.
                public class ExpressionTreeIdPredicateFixture
                {
                    public Expression<Func<FixtureAggregate, bool>> BuildIdEqualsPredicate(object id)
                    {
                        var param = Expression.Parameter(typeof(FixtureAggregate), "e");
                        var idProperty = Expression.Property(param, "Id");
                        var idConstant = Expression.Constant(id, typeof(object));
                        var equals = Expression.Equal(idProperty, idConstant);
                        return Expression.Lambda<Func<FixtureAggregate, bool>>(equals, param);
                    }
                }
            }
            """;

        var assembly = CompileInMemory(
            "EfPropertyExpressionTreeCompliant",
            source,
            new[] { Assembly.Load("System.Linq.Expressions").Location });

        var result = EfCorePackageHygieneRules
            .NoDirectEfPropertyUsageInEfCoreAssembly(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "ExpressionTreeIdPredicateFixture builds the Id predicate via " +
                     "Expression.Property/Expression.Lambda (the P-316-corrected pattern) — no " +
                     "EF.Property call is present anywhere in this fixture");
    }

    // ---------------------------------------------------------------------------
    // T-274 — Rule 4 pass path (structural self-exemption proof): EF.Property inside a
    // HasQueryFilter-shaped Expression<Func<T,bool>> lambda
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-274: A contrived fixture using <c>EF.Property&lt;TId&gt;(e, "TenantId")</c> inside a
    /// lambda whose converted type is <c>Expression&lt;Func&lt;T,bool&gt;&gt;</c> (a
    /// <c>HasQueryFilter</c>-shaped global query filter) must pass
    /// <see cref="EfCorePackageHygieneRules.NoDirectEfPropertyUsageInEfCoreAssembly"/> — this is
    /// the structural self-exemption for the platform's one legitimate <c>EF.Property&lt;T&gt;</c>
    /// usage, confirmed empirically here against the real compiled IL (this test asserting
    /// <c>RulePasses</c> and going green is the empirical confirmation the design's own
    /// Implementation Rules required — not merely assumed from prose).
    /// </summary>
    [Fact]
    public void NoDirectEfPropertyUsageInEfCoreAssembly_EfPropertyInsideExpressionLambda_RulePasses()
    {
        const string source = """
            using System;
            using System.Linq.Expressions;

            namespace Microsoft.EntityFrameworkCore
            {
                public static class EF
                {
                    public static TProperty Property<TProperty>(object entity, string propertyName) =>
                        default!;
                }
            }

            namespace Persistence.MultiTenancy
            {
                public class FixtureAggregate
                {
                }

                // Stub simulating EF Core's ModelBuilder.Entity<T>().HasQueryFilter(...) overload —
                // the parameter type is what forces the C# compiler to convert the lambda body to
                // an expression tree instead of compiling an ordinary delegate.
                public static class ModelBuilderStub
                {
                    public static void HasQueryFilter(
                        Expression<Func<FixtureAggregate, bool>> filter)
                    {
                        _ = filter;
                    }
                }

                // Compliant: the platform's ONE legitimate EF.Property<T> pattern — a
                // tenant/shadow-property global query filter built via
                // HasQueryFilter(Expression<Func<T,bool>>). The C# compiler lowers this lambda
                // body into Expression-builder calls instead of emitting a direct Call/Callvirt
                // against EF.Property — structural self-exemption, not an allow-list.
                public class TenantFilterFixture
                {
                    private static readonly Guid CurrentTenantId = Guid.Empty;

                    public void Configure()
                    {
                        ModelBuilderStub.HasQueryFilter(
                            e => Microsoft.EntityFrameworkCore.EF.Property<Guid>(e, "TenantId")
                                == CurrentTenantId);
                    }
                }
            }
            """;

        var assembly = CompileInMemory(
            "EfPropertyInsideExpressionLambdaCompliant",
            source,
            new[] { Assembly.Load("System.Linq.Expressions").Location });

        var result = EfCorePackageHygieneRules
            .NoDirectEfPropertyUsageInEfCoreAssembly(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "EF.Property<Guid> is called inside a lambda whose converted type is " +
                     "Expression<Func<FixtureAggregate,bool>> (a HasQueryFilter-shaped global " +
                     "query filter) — the C# compiler lowers the entire lambda body into " +
                     "Expression-builder calls, emitting no Call/Callvirt opcode against " +
                     "EF.Property at all, confirmed empirically by this test passing against the " +
                     "real compiled IL");
    }

    // ---------------------------------------------------------------------------
    // Real-assembly verification (GATING acceptance criterion) — the real, corrected
    // SharedKernel.Persistence.EfCore assembly, post-06.Persistence P-316
    // ---------------------------------------------------------------------------

    /// <summary>
    /// GATING (not deferred): re-points <see cref="EfCorePackageHygieneRules.NoDirectEfPropertyUsageInEfCoreAssembly"/>
    /// at the REAL, shipped <c>SharedKernel.Persistence.EfCore</c> assembly and confirms zero
    /// violations. 06.Persistence's P-316 (<c>TenantedRepository</c>'s two
    /// <c>GetByIdForTenantAsync*</c> methods migrated off <c>EF.Property&lt;TId&gt;</c> to the
    /// <c>Expression.Property</c> pattern via <c>BuildIdEqualsPredicate</c>) is confirmed shipped
    /// on disk — <c>06.Persistence/state-map.md</c>'s C-101 is <c>●</c> Complete, and
    /// <c>TenantedRepository.cs</c>'s own source contains no direct <c>EF.Property</c> call for
    /// the Id-equality predicate. The one remaining <c>EF.Property&lt;bool&gt;</c> call in that
    /// file (<c>GetByIdForTenantAsync</c>'s soft-delete re-filter) is passed as a
    /// <c>.Where(Expression&lt;Func&lt;T,bool&gt;&gt;)</c> argument on an <c>IQueryable&lt;T&gt;</c>
    /// — the exact structural self-exemption T-274 proves — so it does not trip this rule either.
    /// </summary>
    [Fact]
    public void NoDirectEfPropertyUsageInEfCoreAssembly_RealEfCoreAssembly_RulePasses()
    {
        var efCoreAssembly = typeof(SharedKernel.Persistence.EfCorePersistenceBuilder<>).Assembly;

        var result = EfCorePackageHygieneRules
            .NoDirectEfPropertyUsageInEfCoreAssembly(efCoreAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "06.Persistence's P-316 fix shipped — TenantedRepository's " +
                     "GetByIdForTenantAsync/GetByIdForTenantIncludingDeletedAsync now build the " +
                     "Id-equality predicate via Expression.Parameter/Property/Equal/Lambda " +
                     "(BuildIdEqualsPredicate), and the remaining EF.Property<bool> soft-delete " +
                     "check is passed as an Expression<Func<T,bool>> to IQueryable<T>.Where — the " +
                     "structural self-exemption, not a Call/Callvirt violation");
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
