using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using SharedKernel.ArchitectureTests.Predicates;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="ContractsPurityRules"/> and <see cref="NoNonTrivialMethodsPredicate"/>.
/// </summary>
/// <remarks>
/// T-40/T-41: Rule 1 — NoNonTrivialMethods
/// T-42: Rule 2 — NoDomainTypeOnPublicSurface
/// T-43: Rule 3 — NoResultTypeOnPublicSurface
/// T-44/T-45: Rule 4 — IntegrationEventImplementationsMustBeSealed
/// </remarks>
public class ContractsPurityRulesTests
{
    // ---------------------------------------------------------------------------
    // T-40 — Rule 1 fire path: type with non-trivial method fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-40: A contracts type with a non-trivial method (e.g., <c>Validate()</c>) must fail
    /// <see cref="ContractsPurityRules.ContractsAssembliesHaveNoNonTrivialMethods"/>.
    /// </summary>
    [Fact]
    public void ContractsAssembliesHaveNoNonTrivialMethods_TypeWithNonTrivialMethod_RuleFails()
    {
        const string source = """
            using System;
            namespace SharedKernel.Contracts
            {
                public class OrderDto
                {
                    public Guid Id { get; set; }
                    public DateTime Deadline { get; set; }

                    // Non-trivial method — domain logic in a DTO
                    public bool IsExpired() => Deadline < DateTime.UtcNow;
                }
            }
            """;

        var assembly = CompileInMemory("NonTrivialMethodViolation", source);
        var tempPath = assembly.Location;
        using var cecilAssembly = AssemblyDefinition.ReadAssembly(tempPath);

        var typeDefinition = cecilAssembly.MainModule.Types
            .Single(t => t.Name == "OrderDto");

        var predicate = new NoNonTrivialMethodsPredicate();
        predicate.MeetsRule(typeDefinition).Should().BeFalse(
            because: "OrderDto.IsExpired() is a non-trivial method — it contains conditional logic");
    }

    // ---------------------------------------------------------------------------
    // T-41 — Rule 1 pass path: pure DTO passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-41: A pure DTO contracts assembly with only constructors and properties must pass
    /// <see cref="ContractsPurityRules.ContractsAssembliesHaveNoNonTrivialMethods"/>.
    /// </summary>
    [Fact]
    public void ContractsAssembliesHaveNoNonTrivialMethods_PureDtoAssembly_RulePasses()
    {
        const string source = """
            using System;
            namespace SharedKernel.Contracts
            {
                public record OrderDto(Guid Id, string Status);
            }
            """;

        var assembly = CompileInMemory("PureDtoAssembly", source);
        var result = ContractsPurityRules
            .ContractsAssembliesHaveNoNonTrivialMethods(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderDto is a pure record with no non-trivial methods");
    }

    // ---------------------------------------------------------------------------
    // T-42 — Rule 2 fire path: domain type on public surface fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-42: A contracts type with a property of a domain type causes a domain assembly
    /// dependency, which Rule 2 must detect.
    /// </summary>
    [Fact]
    public void ContractsAssembliesHaveNoDomainTypeOnPublicSurface_DomainTypeExposed_RuleFails()
    {
        // Arrange — compile a fixture where the contracts type references SharedKernel.Domain
        const string source = """
            namespace SharedKernel.Domain
            {
                public abstract class AggregateRoot { }
            }

            namespace SharedKernel.Contracts
            {
                public class OrderSummaryDto
                {
                    public SharedKernel.Domain.AggregateRoot? DomainObject { get; set; }
                }
            }
            """;

        var assembly = CompileInMemory("DomainTypeLeakage", source);
        var result = ContractsPurityRules
            .ContractsAssembliesHaveNoDomainTypeOnPublicSurface(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderSummaryDto exposes AggregateRoot (a domain type) on its public surface");
    }

    // ---------------------------------------------------------------------------
    // T-43 — Rule 3 fire path: Result<T> on public surface fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-43: A contracts type with a <c>Result&lt;T&gt;</c> property must fail Rule 3.
    /// </summary>
    [Fact]
    public void ContractsAssembliesHaveNoResultTypeOnPublicSurface_ResultTypeExposed_RuleFails()
    {
        // Arrange — compile a fixture with SharedKernel.Primitives Result type
        const string source = """
            namespace SharedKernel.Primitives
            {
                public class Result<T>
                {
                    public T? Value { get; }
                    public bool IsSuccess { get; }
                }
            }

            namespace SharedKernel.Contracts
            {
                public class CreateOrderResponse
                {
                    public SharedKernel.Primitives.Result<System.Guid>? OrderId { get; set; }
                }
            }
            """;

        var assembly = CompileInMemory("ResultTypeLeakage", source);
        var result = ContractsPurityRules
            .ContractsAssembliesHaveNoResultTypeOnPublicSurface(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "CreateOrderResponse exposes Result<Guid> which belongs to SharedKernel.Primitives");
    }

    // ---------------------------------------------------------------------------
    // T-44 — Rule 4 fire path: non-sealed IIntegrationEvent fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-44: A non-sealed class implementing <c>IIntegrationEvent</c> must fail Rule 4.
    /// </summary>
    [Fact]
    public void IntegrationEventImplementationsMustBeSealed_NonSealedImplementation_RuleFails()
    {
        const string source = """
            using System;
            namespace SharedKernel.Contracts.Events
            {
                public interface IIntegrationEvent
                {
                    Guid EventId { get; }
                    DateTimeOffset OccurredOn { get; }
                }
            }

            namespace MyContracts
            {
                // Non-sealed class — violation
                public class OrderCreatedEvent : SharedKernel.Contracts.Events.IIntegrationEvent
                {
                    public Guid EventId { get; } = Guid.NewGuid();
                    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
                }
            }
            """;

        var assembly = CompileInMemory(
            "NonSealedIntegrationEvent",
            source,
            extraReferences: new[] { typeof(SharedKernel.Contracts.Events.IIntegrationEvent).Assembly.Location });

        var result = ContractsPurityRules
            .IntegrationEventImplementationsMustBeSealed(
                assembly,
                typeof(SharedKernel.Contracts.Events.IIntegrationEvent))
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderCreatedEvent is not sealed");
    }

    // ---------------------------------------------------------------------------
    // T-45 — Rule 4 pass path: sealed IIntegrationEvent passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-45: When all <c>IIntegrationEvent</c> implementations are sealed, Rule 4 passes.
    /// </summary>
    [Fact]
    public void IntegrationEventImplementationsMustBeSealed_SealedImplementation_RulePasses()
    {
        const string source = """
            using System;
            namespace SharedKernel.Contracts.Events
            {
                public interface IIntegrationEvent
                {
                    Guid EventId { get; }
                    DateTimeOffset OccurredOn { get; }
                }
            }

            namespace MyContracts
            {
                // Sealed record — compliant
                public sealed record OrderCreatedEvent(
                    Guid EventId,
                    DateTimeOffset OccurredOn)
                    : SharedKernel.Contracts.Events.IIntegrationEvent;
            }
            """;

        var assembly = CompileInMemory(
            "SealedIntegrationEvent",
            source,
            extraReferences: new[] { typeof(SharedKernel.Contracts.Events.IIntegrationEvent).Assembly.Location });

        var result = ContractsPurityRules
            .IntegrationEventImplementationsMustBeSealed(
                assembly,
                typeof(SharedKernel.Contracts.Events.IIntegrationEvent))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderCreatedEvent is a sealed record");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Assembly CompileInMemory(
        string assemblyName,
        string source,
        string[]? extraReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var refList = new System.Collections.Generic.List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        if (extraReferences is not null)
        {
            foreach (var refPath in extraReferences)
                refList.Add(MetadataReference.CreateFromFile(refPath));
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
