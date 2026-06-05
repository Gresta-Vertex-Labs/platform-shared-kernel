using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="RepositoryContractCompletenessRules"/> — covering:
/// <c>AllRepositoryImplementorsMustHaveExistsAsync</c> (Rules 2 fire/pass) and
/// <c>AllReadRepositoryImplementorsMustHaveGetByIdsAsync</c> (Rules 3 fire/pass).
/// </summary>
/// <remarks>
/// T-63/T-64: Rule 2 — AllRepositoryImplementorsMustHaveExistsAsync
/// T-65/T-66: Rule 3 — AllReadRepositoryImplementorsMustHaveGetByIdsAsync
/// </remarks>
public class RepositoryContractCompletenessRulesTests
{
    // ---------------------------------------------------------------------------
    // T-63 — Rule 2 fire path: IRepository implementor without ExistsAsync fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-63: An assembly containing an <c>IRepository&lt;Order, Guid&gt;</c> implementor that
    /// does NOT declare <c>ExistsAsync</c> must fail
    /// <see cref="RepositoryContractCompletenessRules.AllRepositoryImplementorsMustHaveExistsAsync"/>.
    /// The failure message must reference the offending type.
    /// </summary>
    [Fact]
    public void AllRepositoryImplementorsMustHaveExistsAsync_MissingExistsAsync_RuleFails()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Persistence.Abstractions
            {
                public interface IRepository<TEntity, TId> { }
            }

            namespace Domain
            {
                public class Order { public Guid Id { get; set; } }
            }

            namespace Persistence.Repositories
            {
                // Violation: IRepository implementor missing ExistsAsync (added in P-093)
                public class OrderRepository
                    : Persistence.Abstractions.IRepository<Domain.Order, Guid>
                {
                    public Task<Domain.Order?> GetByIdAsync(
                        Guid id, CancellationToken ct = default)
                    {
                        return Task.FromResult<Domain.Order?>(null);
                    }

                    public Task<IReadOnlyList<Domain.Order>> FindAsync(
                        CancellationToken ct = default)
                    {
                        return Task.FromResult<IReadOnlyList<Domain.Order>>(new List<Domain.Order>());
                    }

                    // ExistsAsync is intentionally omitted to trigger the rule
                }
            }
            """;

        var assembly = CompileInMemory("MissingExistsAsyncViolation", source);

        var result = RepositoryContractCompletenessRules
            .AllRepositoryImplementorsMustHaveExistsAsync(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderRepository implements IRepository<Order, Guid> but does not declare " +
                     "ExistsAsync, which was added to the interface contract in P-093");
    }

    // ---------------------------------------------------------------------------
    // T-64 — Rule 2 pass path: IRepository implementor with ExistsAsync passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-64: An assembly where all <c>IRepository&lt;,&gt;</c> implementors declare
    /// <c>ExistsAsync</c> must pass
    /// <see cref="RepositoryContractCompletenessRules.AllRepositoryImplementorsMustHaveExistsAsync"/>.
    /// </summary>
    [Fact]
    public void AllRepositoryImplementorsMustHaveExistsAsync_ExistsAsyncPresent_RulePasses()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Persistence.Abstractions
            {
                public interface IRepository<TEntity, TId> { }
            }

            namespace Domain
            {
                public class Order { public Guid Id { get; set; } }
            }

            namespace Persistence.Repositories
            {
                // Compliant: ExistsAsync declared per the P-093 interface contract
                public class OrderRepository
                    : Persistence.Abstractions.IRepository<Domain.Order, Guid>
                {
                    public Task<Domain.Order?> GetByIdAsync(
                        Guid id, CancellationToken ct = default)
                    {
                        return Task.FromResult<Domain.Order?>(null);
                    }

                    public Task<bool> ExistsAsync(
                        Guid id, CancellationToken ct = default)
                    {
                        return Task.FromResult(false);
                    }

                    public Task<IReadOnlyList<Domain.Order>> FindAsync(
                        CancellationToken ct = default)
                    {
                        return Task.FromResult<IReadOnlyList<Domain.Order>>(new List<Domain.Order>());
                    }
                }
            }
            """;

        var assembly = CompileInMemory("ExistsAsyncPresent", source);

        var result = RepositoryContractCompletenessRules
            .AllRepositoryImplementorsMustHaveExistsAsync(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderRepository declares ExistsAsync per the P-093 interface contract");
    }

    // ---------------------------------------------------------------------------
    // T-65 — Rule 3 fire path: IReadRepository implementor without GetByIdsAsync fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-65: An assembly containing an <c>IReadRepository&lt;Order, Guid&gt;</c> implementor
    /// that does NOT declare <c>GetByIdsAsync</c> must fail
    /// <see cref="RepositoryContractCompletenessRules.AllReadRepositoryImplementorsMustHaveGetByIdsAsync"/>.
    /// The failure message must reference the offending type.
    /// </summary>
    [Fact]
    public void AllReadRepositoryImplementorsMustHaveGetByIdsAsync_MissingGetByIdsAsync_RuleFails()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Persistence.Abstractions
            {
                public interface IReadRepository<TEntity, TId> { }
            }

            namespace Domain
            {
                public class Order { public Guid Id { get; set; } }
            }

            namespace Persistence.Repositories
            {
                // Violation: IReadRepository implementor missing GetByIdsAsync (added in P-093)
                public class OrderReadRepository
                    : Persistence.Abstractions.IReadRepository<Domain.Order, Guid>
                {
                    public Task<Domain.Order?> GetAsync(
                        Guid id, CancellationToken ct = default)
                    {
                        return Task.FromResult<Domain.Order?>(null);
                    }

                    public Task<IReadOnlyList<Domain.Order>> FindAsync(
                        CancellationToken ct = default)
                    {
                        return Task.FromResult<IReadOnlyList<Domain.Order>>(new List<Domain.Order>());
                    }

                    // GetByIdsAsync is intentionally omitted to trigger the rule
                }
            }
            """;

        var assembly = CompileInMemory("MissingGetByIdsAsyncViolation", source);

        var result = RepositoryContractCompletenessRules
            .AllReadRepositoryImplementorsMustHaveGetByIdsAsync(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderReadRepository implements IReadRepository<Order, Guid> but does not " +
                     "declare GetByIdsAsync, which was added to the interface contract in P-093");
    }

    // ---------------------------------------------------------------------------
    // T-66 — Rule 3 pass path: IReadRepository implementor with GetByIdsAsync passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-66: An assembly where all <c>IReadRepository&lt;,&gt;</c> implementors declare
    /// <c>GetByIdsAsync</c> must pass
    /// <see cref="RepositoryContractCompletenessRules.AllReadRepositoryImplementorsMustHaveGetByIdsAsync"/>.
    /// </summary>
    [Fact]
    public void AllReadRepositoryImplementorsMustHaveGetByIdsAsync_GetByIdsAsyncPresent_RulePasses()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Persistence.Abstractions
            {
                public interface IReadRepository<TEntity, TId> { }
            }

            namespace Domain
            {
                public class Order { public Guid Id { get; set; } }
            }

            namespace Persistence.Repositories
            {
                // Compliant: GetByIdsAsync declared per the P-093 interface contract
                public class OrderReadRepository
                    : Persistence.Abstractions.IReadRepository<Domain.Order, Guid>
                {
                    public Task<Domain.Order?> GetAsync(
                        Guid id, CancellationToken ct = default)
                    {
                        return Task.FromResult<Domain.Order?>(null);
                    }

                    public Task<IReadOnlyList<Domain.Order>> GetByIdsAsync(
                        IEnumerable<Guid> ids, CancellationToken ct = default)
                    {
                        return Task.FromResult<IReadOnlyList<Domain.Order>>(new List<Domain.Order>());
                    }

                    public Task<IReadOnlyList<Domain.Order>> FindAsync(
                        CancellationToken ct = default)
                    {
                        return Task.FromResult<IReadOnlyList<Domain.Order>>(new List<Domain.Order>());
                    }
                }
            }
            """;

        var assembly = CompileInMemory("GetByIdsAsyncPresent", source);

        var result = RepositoryContractCompletenessRules
            .AllReadRepositoryImplementorsMustHaveGetByIdsAsync(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderReadRepository declares GetByIdsAsync per the P-093 interface contract");
    }

    // ---------------------------------------------------------------------------
    // Additional scoping test: IReadRepository implementors are NOT checked by
    // AllRepositoryImplementorsMustHaveExistsAsync
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Scoping test: an assembly containing only <c>IReadRepository</c> implementors (no write-side
    /// <c>IRepository</c> implementors) must pass
    /// <see cref="RepositoryContractCompletenessRules.AllRepositoryImplementorsMustHaveExistsAsync"/>
    /// even if <c>ExistsAsync</c> is absent — read repositories are out of scope for the write-side rule.
    /// </summary>
    [Fact]
    public void AllRepositoryImplementorsMustHaveExistsAsync_ReadRepositoryImplementor_NotInScope()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Persistence.Abstractions
            {
                // IReadRepository only — no write-side IRepository here
                public interface IReadRepository<TEntity, TId> { }
            }

            namespace Domain
            {
                public class Order { public Guid Id { get; set; } }
            }

            namespace Persistence.Repositories
            {
                // IReadRepository implementor — should NOT be required to have ExistsAsync
                // (ExistsAsync is a write-side contract; read-side has GetByIdsAsync instead)
                public class OrderReadRepository
                    : Persistence.Abstractions.IReadRepository<Domain.Order, Guid>
                {
                    public Task<Domain.Order?> GetAsync(
                        Guid id, CancellationToken ct = default)
                    {
                        return Task.FromResult<Domain.Order?>(null);
                    }

                    // No ExistsAsync — this is correct; read repositories don't have it
                }
            }
            """;

        var assembly = CompileInMemory("ReadOnlyRepoNoExistsAsync", source);

        var result = RepositoryContractCompletenessRules
            .AllRepositoryImplementorsMustHaveExistsAsync(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "IReadRepository implementors are excluded from the ExistsAsync rule — " +
                     "ExistsAsync is a write-side (IRepository) contract");
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
