using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="SharedKernelLayeringRules.PersistenceNeverReferencesApplicationOrSecurity"/> —
/// the mechanical lock on 06.Persistence's removal of its former <c>SharedKernel.Application</c> and
/// <c>SharedKernel.Security</c> dependencies.
/// </summary>
/// <remarks>
/// <para>
/// Two independent verification mechanisms, since a compiled-assembly (IL) check alone cannot easily
/// cover every TEST project without adding a heavy, unprecedented set of test-project
/// <c>ProjectReference</c>s to this governance project:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="PersistenceNeverReferencesApplicationOrSecurity_RealProductionAssembly_RulePasses"/> —
/// the NetArchTest IL-level rule, run against every real, compiled 06.Persistence PRODUCTION
/// assembly.
/// </description></item>
/// <item><description>
/// <see cref="PersistenceSourceTree_NoProductionOrTestFileReferencesApplicationOrSecurity"/> — a
/// Roslyn syntax scan of every <c>.cs</c> file under the <c>06.Persistence</c> folder on disk,
/// PRODUCTION AND TEST alike, checking real code nodes (never comment trivia) for a
/// <c>using</c> directive or qualified-name reference into either forbidden namespace.
/// </description></item>
/// </list>
/// </remarks>
public sealed class PersistenceLayeringRulesTests
{
    // ---------------------------------------------------------------------------
    // Fire path — contrived fixtures proving the rule actually detects each forbidden namespace
    // ---------------------------------------------------------------------------

    [Fact]
    public void PersistenceNeverReferencesApplicationOrSecurity_ApplicationDependency_RuleFails()
    {
        const string violationSource = """
            namespace SharedKernel.Application.Behaviors.Transaction
            {
                public interface IUnitOfWork { }
            }

            namespace SharedKernel.Persistence.EfCore
            {
                public sealed class RogueBridge
                {
                    private readonly SharedKernel.Application.Behaviors.Transaction.IUnitOfWork _uow;
                    public RogueBridge(SharedKernel.Application.Behaviors.Transaction.IUnitOfWork uow) { _uow = uow; }
                }
            }
            """;

        var violationAssembly = CompileInMemory("PersistenceApplicationViolation", violationSource);

        var result = SharedKernelLayeringRules
            .PersistenceNeverReferencesApplicationOrSecurity(violationAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RogueBridge depends on SharedKernel.Application.Behaviors — the MediatR pipeline persistence must never reference");
    }

    [Fact]
    public void PersistenceNeverReferencesApplicationOrSecurity_SecurityDependency_RuleFails()
    {
        const string violationSource = """
            namespace SharedKernel.Security.Abstractions
            {
                public interface IUserContext { }
            }

            namespace SharedKernel.Persistence.EfCore
            {
                public sealed class RogueActorBridge
                {
                    private readonly SharedKernel.Security.Abstractions.IUserContext _user;
                    public RogueActorBridge(SharedKernel.Security.Abstractions.IUserContext user) { _user = user; }
                }
            }
            """;

        var violationAssembly = CompileInMemory("PersistenceSecurityViolation", violationSource);

        var result = SharedKernelLayeringRules
            .PersistenceNeverReferencesApplicationOrSecurity(violationAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RogueActorBridge depends on SharedKernel.Security.Abstractions.IUserContext — a dependency this domain removed");
    }

    [Fact]
    public void PersistenceNeverReferencesApplicationOrSecurity_ApplicationAbstractionsDependency_RulePasses()
    {
        // P-558: the shared contracts in SharedKernel.Application.Abstractions (namespaces .Context,
        // .Transactions, .Auditing) are the one part of 05.Application persistence may depend on.
        const string cleanSource = """
            namespace SharedKernel.Application.Context
            {
                public interface IRequestContext { string? UserId { get; } }
            }

            namespace SharedKernel.Application.Transactions
            {
                public interface IUnitOfWork { }
            }

            namespace SharedKernel.Application.Auditing
            {
                public interface IAuditTrailWriter { }
            }

            namespace SharedKernel.Persistence.EfCore
            {
                public sealed class SharedContractConsumer
                {
                    public SharedContractConsumer(
                        SharedKernel.Application.Context.IRequestContext context,
                        SharedKernel.Application.Transactions.IUnitOfWork unitOfWork,
                        SharedKernel.Application.Auditing.IAuditTrailWriter writer) { }
                }
            }
            """;

        var cleanAssembly = CompileInMemory("PersistenceLayeringClean", cleanSource);

        var result = SharedKernelLayeringRules
            .PersistenceNeverReferencesApplicationOrSecurity(cleanAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedContractConsumer depends only on SharedKernel.Application.Abstractions' namespaces");
    }

    [Fact]
    public void PersistenceNeverReferencesApplicationOrSecurity_MediatRDependency_RuleFails()
    {
        const string violationSource = """
            namespace MediatR
            {
                public interface ISender { }
            }

            namespace SharedKernel.Persistence.EfCore
            {
                public sealed class RogueDispatcher
                {
                    public RogueDispatcher(MediatR.ISender sender) { }
                }
            }
            """;

        var violationAssembly = CompileInMemory("PersistenceMediatRViolation", violationSource);

        var result = SharedKernelLayeringRules
            .PersistenceNeverReferencesApplicationOrSecurity(violationAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(because: "persistence must never depend on MediatR");
    }

    [Theory]
    [MemberData(nameof(RealPersistenceProductionAssemblies))]
    public void PersistenceForbiddenAssemblyReferences_RealProductionAssembly_IsEmpty(Assembly assembly)
    {
        SharedKernelLayeringRules.PersistenceForbiddenAssemblyReferences(assembly).Should().BeEmpty(
            because: $"'{assembly.GetName().Name}' may reference SharedKernel.Application.Abstractions only");
    }

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
        typeof(SharedKernel.Persistence.Dapper.ReadModels.DapperReadService).Assembly
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
        typeof(SharedKernel.Persistence.Npgsql.Extensions.NpgsqlPersistenceExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Should().NotContain(name =>
                name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("SharedKernel.Persistence.EfCore", StringComparison.Ordinal),
                because: "the shared data source and the SQLSTATE classifier serve Dapper as well");
    }

    [Fact]
    public void PersistenceAbstractions_ReferencesTheSharedApplicationAbstractions()
    {
        typeof(SharedKernel.Persistence.Abstractions.Context.ICrossTenantScope).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().Contain("SharedKernel.Application.Abstractions");
    }

    // ---------------------------------------------------------------------------
    // Pass path — every real, compiled 06.Persistence production assembly
    // ---------------------------------------------------------------------------

    public static IEnumerable<object[]> RealPersistenceProductionAssemblies()
    {
        yield return [typeof(SharedKernel.Persistence.Abstractions.Context.ICrossTenantScope).Assembly];
        yield return [typeof(SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext).Assembly];
        yield return [typeof(SharedKernel.Persistence.EfCore.Auditing.Extensions.EfCorePersistenceBuilderAuditingExtensions).Assembly];
        yield return [typeof(SharedKernel.Persistence.EfCore.Encryption.Extensions.EfCorePersistenceBuilderEncryptionExtensions).Assembly];
        yield return [typeof(SharedKernel.Persistence.Npgsql.Extensions.NpgsqlPersistenceExtensions).Assembly];
        yield return [typeof(SharedKernel.Persistence.Dapper.ReadModels.DapperReadService).Assembly];
    }

    /// <summary>
    /// Every real, currently-shipped 06.Persistence production assembly must pass — proving the
    /// removal of the former <c>SharedKernel.Application</c>/<c>SharedKernel.Security</c> dependencies
    /// holds across the WHOLE domain, not merely one package.
    /// </summary>
    [Theory]
    [MemberData(nameof(RealPersistenceProductionAssemblies))]
    public void PersistenceNeverReferencesApplicationOrSecurity_RealProductionAssembly_RulePasses(Assembly assembly)
    {
        var result = SharedKernelLayeringRules
            .PersistenceNeverReferencesApplicationOrSecurity(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"'{assembly.GetName().Name}' must not reference SharedKernel.Application or SharedKernel.Security");
    }

    // ---------------------------------------------------------------------------
    // Source-tree scan — covers TEST projects too, which the IL-level rule above does not reach
    // without an unprecedented set of test-project ProjectReferences on this governance project.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Every <c>.cs</c> file physically inside the <c>06.Persistence</c> folder — production AND
    /// test — must contain no real (non-comment) reference to <c>SharedKernel.Application</c> or
    /// <c>SharedKernel.Security</c>. Complements the IL-level rule above, which only reaches
    /// production assemblies already referenced by this project.
    /// </summary>
    [Fact]
    public void PersistenceSourceTree_NoProductionOrTestFileReferencesApplicationOrSecurity()
    {
        var persistenceRoot = FindPersistenceRoot();

        var csharpFiles = Directory.EnumerateFiles(persistenceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsBuildOutputSegment(path))
                .ToList();

        csharpFiles.Should().NotBeEmpty("the 06.Persistence source tree must be found for this check to mean anything");

        var offendingFiles = new List<string>();

        foreach (var file in csharpFiles)
        {
            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source, path: file);
            var root = tree.GetRoot();

            // Every NameSyntax node's own .ToString() (a QualifiedNameSyntax/IdentifierNameSyntax,
            // never comment trivia — DescendantNodes() only visits real syntax, not trivia) — this
            // reaches a `using SharedKernel.Application;` directive through its Name child exactly
            // as it reaches a fully-qualified inline reference, with no separate case needed. A
            // doc-comment mentioning either namespace in prose (several already exist, deliberately,
            // to document the REMOVAL of these dependencies) is never a NameSyntax node, so it is
            // never mistaken for a real reference.
            var referencesForbiddenNamespace = root.DescendantNodes()
                .OfType<NameSyntax>()
                .Where(name => name.Parent is not QualifiedNameSyntax)
                .Any(name => IsForbiddenNamespaceReference(name.ToString()));

            if (referencesForbiddenNamespace)
                offendingFiles.Add(file);
        }

        offendingFiles.Should().BeEmpty(
            "no .cs file under 06.Persistence (production or test) may reference 05.Application beyond " +
                "SharedKernel.Application.Abstractions, MediatR, or SharedKernel.Security");
    }

    private static bool IsForbiddenNamespaceReference(string nodeText) =>
        (nodeText.StartsWith("SharedKernel.Application", StringComparison.Ordinal)
            && !SharedKernelLayeringRules.ApplicationAbstractionsNamespaces.Any(allowed =>
                nodeText == allowed || nodeText.StartsWith(allowed + ".", StringComparison.Ordinal)))
            || nodeText.StartsWith("MediatR", StringComparison.Ordinal)
            || nodeText.StartsWith("SharedKernel.Security", StringComparison.Ordinal);

    private static bool ContainsBuildOutputSegment(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    // Walks up from this test assembly's own output directory until it finds the repo root
    // (identified by Platform.SharedKernel.slnx), then descends into 06.Persistence.
    private static string FindPersistenceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")))
            directory = directory.Parent;

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root (Platform.SharedKernel.slnx) by walking up from " +
                    $"'{AppContext.BaseDirectory}'.");
        }

        var persistenceRoot = Path.Combine(directory.FullName, "06.Persistence");

        if (!Directory.Exists(persistenceRoot))
        {
            throw new InvalidOperationException(
                $"Repository root '{directory.FullName}' was found, but it has no '06.Persistence' folder.");
        }

        return persistenceRoot;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles C# source code and emits it to a temp file, then loads the assembly from disk —
    /// NetArchTest's Mono.Cecil backing requires a physical file path.
    /// </summary>
    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tempPath = Path.Combine(Path.GetTempPath(), $"{assemblyName}_{Guid.NewGuid():N}.dll");
        using (var fs = File.OpenWrite(tempPath))
        {
            var emitResult = compilation.Emit(fs);

            if (!emitResult.Success)
            {
                var errors = string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                throw new InvalidOperationException($"Test fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
            }
        }

        return Assembly.LoadFrom(tempPath);
    }
}
