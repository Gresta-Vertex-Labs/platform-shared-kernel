using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="RepositoryContractCompletenessRules"/> — covering:
/// <c>AllReadRepositoryImplementorsMustHaveGetByIdAsync</c> (T-63/T-64) and
/// <c>AllReadRepositoryImplementorsMustHaveGetByIdsAsync</c> (Rules 3 fire/pass).
/// </summary>
/// <remarks>
/// T-63/T-64: AllReadRepositoryImplementorsMustHaveGetByIdAsync
/// T-65/T-66: Rule 3 — AllReadRepositoryImplementorsMustHaveGetByIdsAsync
/// </remarks>
public class RepositoryContractCompletenessRulesTests
{
    // ---------------------------------------------------------------------------
    // T-63 — GetByIdAsync fire path: IReadRepository implementor without GetByIdAsync fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-63: An assembly containing an <c>IReadRepository&lt;Order, Guid&gt;</c> implementor that does NOT
    /// declare <c>GetByIdAsync</c> must fail
    /// <see cref="RepositoryContractCompletenessRules.AllReadRepositoryImplementorsMustHaveGetByIdAsync"/>.
    /// </summary>
    [Fact]
    public void AllReadRepositoryImplementorsMustHaveGetByIdAsync_MissingGetByIdAsync_RuleFails()
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
                // Violation: the read contract's aggregate lookup is missing
                public class OrderReadRepository
                    : Persistence.Abstractions.IReadRepository<Domain.Order, Guid>
                {
                    public Task<IReadOnlyList<Domain.Order>> GetByIdsAsync(
                        IEnumerable<Guid> ids, CancellationToken ct = default)
                    {
                        return Task.FromResult<IReadOnlyList<Domain.Order>>(new List<Domain.Order>());
                    }
                }
            }
            """;

        var assembly = CompileInMemory("MissingGetByIdAsyncViolation", source);

        var result = RepositoryContractCompletenessRules
            .AllReadRepositoryImplementorsMustHaveGetByIdAsync(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderReadRepository implements IReadRepository<Order, Guid> but does not declare GetByIdAsync");
    }

    // ---------------------------------------------------------------------------
    // T-64 — GetByIdAsync pass path: read and write implementors that declare it pass
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-64: read-side and write-side (<c>IRepository : IReadRepository</c>) implementors that declare
    /// <c>GetByIdAsync</c> pass
    /// <see cref="RepositoryContractCompletenessRules.AllReadRepositoryImplementorsMustHaveGetByIdAsync"/>.
    /// </summary>
    [Fact]
    public void AllReadRepositoryImplementorsMustHaveGetByIdAsync_GetByIdAsyncPresent_RulePasses()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Persistence.Abstractions
            {
                public interface IReadRepository<TEntity, TId> { }
                public interface IRepository<TEntity, TId> : IReadRepository<TEntity, TId> { }
            }

            namespace Domain
            {
                public class Order { public Guid Id { get; set; } }
            }

            namespace Persistence.Repositories
            {
                public class OrderReadRepository
                    : Persistence.Abstractions.IReadRepository<Domain.Order, Guid>
                {
                    public Task<Domain.Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
                        => Task.FromResult<Domain.Order?>(null);
                }

                public class OrderRepository
                    : Persistence.Abstractions.IRepository<Domain.Order, Guid>
                {
                    public Task<Domain.Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
                        => Task.FromResult<Domain.Order?>(null);
                }
            }
            """;

        var assembly = CompileInMemory("GetByIdAsyncPresent", source);

        var result = RepositoryContractCompletenessRules
            .AllReadRepositoryImplementorsMustHaveGetByIdAsync(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "both repositories declare GetByIdAsync, the read contract's aggregate lookup");
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
    // Real assembly: the shipped EF Core repositories satisfy the read contract rules
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The concrete repositories in <c>SharedKernel.Persistence.EfCore</c> declare both lookups and the
    /// read-only repository never tracks.
    /// </summary>
    [Fact]
    public void EfCoreRepositories_SatisfyTheReadContractRules()
    {
        var assembly = typeof(SharedKernel.Persistence.EfCore.Repositories.EfReadRepository<,>).Assembly;

        RepositoryContractCompletenessRules.AllReadRepositoryImplementorsMustHaveGetByIdAsync(assembly)
            .GetResult().IsSuccessful.Should().BeTrue();
        RepositoryContractCompletenessRules.AllReadRepositoryImplementorsMustHaveGetByIdsAsync(assembly)
            .GetResult().IsSuccessful.Should().BeTrue();
        PersistenceInterfaceOwnershipRules.ReadOnlyRepositoriesNeverTrack(assembly)
            .GetResult().IsSuccessful.Should().BeTrue();
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
