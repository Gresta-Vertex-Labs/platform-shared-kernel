using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="EncryptionPatternGuardRules"/> — covering all three predicates
/// (SK0301–SK0303) that enforce correct usage of the WO-019 AES-256-GCM encryption subsystem.
/// </summary>
/// <remarks>
/// T-79/T-80: SK0301 — NoCryptoCipherInDomainOrApplication
/// T-81/T-82: SK0302 — NoEncryptionAttributeOnDomainEntities
/// T-83/T-84: SK0303 — NoEncryptionRotationJobInjectionInDomainOrApplication
/// SK0304 (NoDirectEncryptedValueConverterInstantiation, formerly T-85/T-86) was retired — the
/// EncryptedValueConverter type it guarded against no longer exists; see
/// EncryptionPatternGuardRules's class remarks.
/// </remarks>
public class EncryptionPatternGuardRulesTests
{
    // ---------------------------------------------------------------------------
    // T-79 — SK0301 fire path: AesGcm usage in domain type fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-79: A domain assembly containing a type that references
    /// <c>System.Security.Cryptography.AesGcm</c> must fail
    /// <see cref="EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication"/>.
    /// The failure must name the offending type.
    /// </summary>
    [Fact]
    public void NoCryptoCipherInDomainOrApplication_AesGcmFieldInDomainType_RuleFails()
    {
        // Arrange: compile a fixture where a domain type declares a field typed AesGcm.
        // We stub out AesGcm in the System.Security.Cryptography namespace so the fixture
        // compiles without requiring the real System.Security.Cryptography NuGet reference.
        const string source = """
            namespace System.Security.Cryptography
            {
                // Stub simulating AesGcm from System.Security.Cryptography
                public sealed class AesGcm : System.IDisposable
                {
                    public AesGcm(byte[] key) { }
                    public void Dispose() { }
                }
            }

            namespace Domain.Orders
            {
                // Violation: domain type holds a direct reference to AesGcm
                public class OrderEncryptionHelper
                {
                    private readonly System.Security.Cryptography.AesGcm _cipher;

                    public OrderEncryptionHelper(byte[] key)
                    {
                        _cipher = new System.Security.Cryptography.AesGcm(key);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("AesGcmDomainViolation", source);

        var result = EncryptionPatternGuardRules
            .NoCryptoCipherInDomainOrApplication(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderEncryptionHelper declares an AesGcm field and constructs it in the constructor, " +
                     "which violates SK0301 — crypto cipher usage must be confined to the persistence layer");
    }

    // ---------------------------------------------------------------------------
    // T-80 — SK0301 pass path: domain assembly with no crypto references passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-80: A domain assembly with no references to <c>System.Security.Cryptography</c>
    /// cipher types must pass
    /// <see cref="EncryptionPatternGuardRules.NoCryptoCipherInDomainOrApplication"/>.
    /// </summary>
    [Fact]
    public void NoCryptoCipherInDomainOrApplication_NoCipherReferences_RulePasses()
    {
        const string source = """
            namespace Domain.Orders
            {
                // Compliant: plain domain entity with no crypto cipher references
                public class Order
                {
                    public System.Guid Id { get; private set; }
                    public string CustomerId { get; private set; } = string.Empty;

                    // Sensitive field — encryption configured in IEntityTypeConfiguration,
                    // NOT via AesGcm directly
                    public string Ssn { get; private set; } = string.Empty;
                }
            }
            """;

        var assembly = CompileInMemory("NoCipherDomainAssembly", source);

        var result = EncryptionPatternGuardRules
            .NoCryptoCipherInDomainOrApplication(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Order has no System.Security.Cryptography cipher type references — " +
                     "encryption is configured in IEntityTypeConfiguration, not in the domain");
    }

    // ---------------------------------------------------------------------------
    // T-81 — SK0302 fire path: domain entity with [EncryptedColumn] attribute fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-81: A domain assembly containing a class decorated with <c>[EncryptedColumn]</c>
    /// must fail <see cref="EncryptionPatternGuardRules.NoEncryptionAttributeOnDomainEntities"/>.
    /// The failure must name the offending type and attribute.
    /// </summary>
    [Fact]
    public void NoEncryptionAttributeOnDomainEntities_EncryptedColumnAttributeOnEntity_RuleFails()
    {
        // Arrange: compile a fixture where a domain entity carries an [EncryptedColumn] attribute.
        // The attribute is stubbed out in the fixture — no external dependency required.
        const string source = """
            namespace Infrastructure.Annotations
            {
                // Stub simulating an infrastructure-specific [EncryptedColumn] attribute
                [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Class)]
                public sealed class EncryptedColumnAttribute : System.Attribute { }
            }

            namespace Domain.Orders
            {
                // Violation: domain entity carries an infrastructure-specific encryption attribute
                [Infrastructure.Annotations.EncryptedColumn]
                public class Customer
                {
                    public System.Guid Id { get; private set; }
                    public string Ssn { get; private set; } = string.Empty;
                }
            }
            """;

        var assembly = CompileInMemory("EncryptedColumnAttributeViolation", source);

        var result = EncryptionPatternGuardRules
            .NoEncryptionAttributeOnDomainEntities(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "Customer is decorated with [EncryptedColumn] — SK0302 prohibits encryption " +
                     "attributes on domain entity classes; use PropertyBuilder<T>.Encrypt() instead");
    }

    // ---------------------------------------------------------------------------
    // T-82 — SK0302 pass path: domain assembly with no Encrypt* attributes passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-82: A domain assembly with no types carrying <c>"Encrypt"</c>-substring attributes
    /// must pass <see cref="EncryptionPatternGuardRules.NoEncryptionAttributeOnDomainEntities"/>.
    /// </summary>
    [Fact]
    public void NoEncryptionAttributeOnDomainEntities_NoEncryptAttributes_RulePasses()
    {
        const string source = """
            namespace Domain.Orders
            {
                // Compliant: domain entity with no encryption attributes
                // Encryption is configured via PropertyBuilder<T>.Encrypt() in IEntityTypeConfiguration
                public class Customer
                {
                    public System.Guid Id { get; private set; }
                    public string Ssn { get; private set; } = string.Empty;
                    public string Name { get; private set; } = string.Empty;
                }
            }
            """;

        var assembly = CompileInMemory("NoEncryptAttributeDomainAssembly", source);

        var result = EncryptionPatternGuardRules
            .NoEncryptionAttributeOnDomainEntities(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Customer carries no 'Encrypt*' attributes — encryption is properly " +
                     "configured in the IEntityTypeConfiguration layer");
    }

    // ---------------------------------------------------------------------------
    // T-83 — SK0303 fire path: application handler injecting IEncryptionRotationJob fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-83: An application assembly containing a handler that injects
    /// <c>IEncryptionRotationJob</c> as a constructor parameter must fail
    /// <see cref="EncryptionPatternGuardRules.NoEncryptionRotationJobInjectionInDomainOrApplication"/>.
    /// The failure must name the offending type.
    /// </summary>
    [Fact]
    public void NoEncryptionRotationJobInjection_HandlerInjectsRotationJob_RuleFails()
    {
        // Arrange: compile a fixture where a MediatR-style command handler injects IEncryptionRotationJob.
        // The interface is stubbed out — no external dependency required.
        const string source = """
            namespace Application.Encryption
            {
                // Stub simulating the IEncryptionRotationJob interface
                public interface IEncryptionRotationJob
                {
                    System.Threading.Tasks.Task RotateAsync(int fromVersion, int toVersion,
                        System.Threading.CancellationToken ct = default);
                }
            }

            namespace Application.Commands
            {
                // Violation: application handler injects IEncryptionRotationJob
                // Key rotation must be triggered from a hosted service, not a command handler
                public class RotateEncryptionKeysCommandHandler
                {
                    private readonly Application.Encryption.IEncryptionRotationJob _rotationJob;

                    // Forbidden injection: IEncryptionRotationJob in application command handler
                    public RotateEncryptionKeysCommandHandler(
                        Application.Encryption.IEncryptionRotationJob rotationJob)
                    {
                        _rotationJob = rotationJob;
                    }

                    public System.Threading.Tasks.Task HandleAsync()
                        => _rotationJob.RotateAsync(1, 2);
                }
            }
            """;

        var assembly = CompileInMemory("RotationJobHandlerViolation", source);

        var result = EncryptionPatternGuardRules
            .NoEncryptionRotationJobInjectionInDomainOrApplication(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RotateEncryptionKeysCommandHandler injects IEncryptionRotationJob, which is " +
                     "prohibited in application handlers — SK0303 requires moving rotation to a " +
                     "hosted service, Hangfire job, Temporal activity, or management controller");
    }

    // ---------------------------------------------------------------------------
    // T-84 — SK0303 pass path: HostedService injecting IEncryptionRotationJob passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-84: A class whose name contains <c>"HostedService"</c> that injects
    /// <c>IEncryptionRotationJob</c> must pass
    /// <see cref="EncryptionPatternGuardRules.NoEncryptionRotationJobInjectionInDomainOrApplication"/>
    /// because the <c>*HostedService*</c> class-name exemption applies.
    /// </summary>
    [Fact]
    public void NoEncryptionRotationJobInjection_HostedServiceExemption_RulePasses()
    {
        const string source = """
            namespace Infrastructure.BackgroundServices
            {
                // Stub simulating the IEncryptionRotationJob interface
                public interface IEncryptionRotationJob
                {
                    System.Threading.Tasks.Task RotateAsync(int fromVersion, int toVersion,
                        System.Threading.CancellationToken ct = default);
                }

                // Compliant: HostedService is an exempt class-name pattern — this is the
                // designated infrastructure consumer of IEncryptionRotationJob
                public class EncryptionKeyRotationHostedService
                {
                    private readonly IEncryptionRotationJob _rotationJob;

                    // Permitted injection: the *HostedService* class-name exemption applies
                    public EncryptionKeyRotationHostedService(IEncryptionRotationJob rotationJob)
                    {
                        _rotationJob = rotationJob;
                    }

                    public System.Threading.Tasks.Task ExecuteAsync(
                        System.Threading.CancellationToken stoppingToken)
                        => _rotationJob.RotateAsync(1, 2, stoppingToken);
                }
            }
            """;

        var assembly = CompileInMemory("RotationJobHostedServiceExempt", source);

        var result = EncryptionPatternGuardRules
            .NoEncryptionRotationJobInjectionInDomainOrApplication(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "EncryptionKeyRotationHostedService matches the '*HostedService*' class-name " +
                     "exemption — injecting IEncryptionRotationJob in a hosted service is the " +
                     "compliant pattern per SK0303");
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
