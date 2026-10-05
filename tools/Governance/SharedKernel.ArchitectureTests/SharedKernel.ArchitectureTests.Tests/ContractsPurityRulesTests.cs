using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using SharedKernel.ArchitectureTests.Predicates;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Contracts.Events;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="ContractsPurityRules"/>, <see cref="NoNonTrivialMethodsPredicate"/> and
/// <see cref="NoResultTypedPublicMemberPredicate"/>.
/// </summary>
/// <remarks>
/// Every rule is proven both ways: it fails on a contrived violation fixture, and it passes against the real
/// <c>SharedKernel.Contracts</c> assembly. The real-assembly tests exist because these rules were once only
/// ever run against fixtures, and failed the moment they met the real package.
/// </remarks>
public class ContractsPurityRulesTests
{
    private static readonly Assembly RealContractsAssembly = typeof(EventEnvelope).Assembly;

    private static readonly string ContractsLocation = typeof(IIntegrationEvent).Assembly.Location;

    private static readonly string PrimitivesLocation = typeof(ValidationResult).Assembly.Location;

    // ---------------------------------------------------------------------------
    // Integration events have no non-trivial methods
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The predicate itself flags a type with a non-trivial method (e.g. <c>IsExpired()</c>).
    /// </summary>
    [Fact]
    public void NoNonTrivialMethodsPredicate_TypeWithNonTrivialMethod_ReturnsFalse()
    {
        const string source = """
            using System;
            namespace SharedKernel.Contracts
            {
                public class OrderDto
                {
                    public Guid Id { get; set; }
                    public DateTime Deadline { get; set; }

                    // Non-trivial method — logic in a DTO
                    public bool IsExpired() => Deadline < DateTime.UtcNow;
                }
            }
            """;

        var assembly = CompileInMemory("NonTrivialMethodViolation", source);
        using var cecilAssembly = AssemblyDefinition.ReadAssembly(assembly.Location);

        var typeDefinition = cecilAssembly.MainModule.Types
            .Single(t => t.Name == "OrderDto");

        new NoNonTrivialMethodsPredicate().MeetsRule(typeDefinition).Should().BeFalse(
            because: "OrderDto.IsExpired() is a non-trivial method — it contains conditional logic");
    }

    /// <summary>
    /// An integration event carrying a non-trivial method fails
    /// <see cref="ContractsPurityRules.IntegrationEventsHaveNoNonTrivialMethods"/>.
    /// </summary>
    [Fact]
    public void IntegrationEventsHaveNoNonTrivialMethods_EventWithMethod_RuleFails()
    {
        const string source = """
            using System;
            using SharedKernel.Contracts.Events;
            namespace MyContracts
            {
                [IntegrationEvent("tests.architecture.order-placed")]
                public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, DateTimeOffset Deadline)
                    : IIntegrationEvent
                {
                    public bool IsExpired() => Deadline < DateTimeOffset.UtcNow;
                }
            }
            """;

        var assembly = CompileInMemory("EventWithMethod", source, ContractsLocation);

        var result = ContractsPurityRules
            .IntegrationEventsHaveNoNonTrivialMethods(assembly, typeof(IIntegrationEvent))
            .GetResult();

        result.IsSuccessful.Should().BeFalse(because: "OrderPlaced.IsExpired() is behaviour on an integration event");
        result.FailingTypeNames.Should().ContainSingle().Which.Should().Be("MyContracts.OrderPlaced");
    }

    /// <summary>
    /// A pure event record passes, and a non-event contract type with a factory in the same assembly is not
    /// judged by this rule.
    /// </summary>
    [Fact]
    public void IntegrationEventsHaveNoNonTrivialMethods_PureEventAndDtoWithFactory_RulePasses()
    {
        const string source = """
            using System;
            using SharedKernel.Contracts.Events;
            namespace MyContracts
            {
                [IntegrationEvent("tests.architecture.order-shipped")]
                public sealed record OrderShipped(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId)
                    : IIntegrationEvent;

                public sealed record OrderQuery(int Page)
                {
                    public static OrderQuery Create(int? page) => new(page ?? 1);
                }
            }
            """;

        var assembly = CompileInMemory("PureEventAssembly", source, ContractsLocation);

        var result = ContractsPurityRules
            .IntegrationEventsHaveNoNonTrivialMethods(assembly, typeof(IIntegrationEvent))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderShipped is pure data and OrderQuery is not an integration event");
    }

    /// <summary>The rule passes against the real <c>SharedKernel.Contracts</c> assembly.</summary>
    [Fact]
    public void IntegrationEventsHaveNoNonTrivialMethods_RealContractsAssembly_RulePasses()
    {
        var result = ContractsPurityRules
            .IntegrationEventsHaveNoNonTrivialMethods(RealContractsAssembly, typeof(IIntegrationEvent))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Contracts ships no integration event with behaviour");
    }

    // ---------------------------------------------------------------------------
    // No domain type on the public surface
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A contracts type with a property of a domain type causes a domain assembly dependency, which the rule
    /// must detect.
    /// </summary>
    [Fact]
    public void ContractsAssembliesHaveNoDomainTypeOnPublicSurface_DomainTypeExposed_RuleFails()
    {
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

    /// <summary>
    /// A generic envelope constrained to a domain interface is no longer exempt: the rule fails on it like
    /// any other type.
    /// </summary>
    [Fact]
    public void ContractsAssembliesHaveNoDomainTypeOnPublicSurface_EnvelopeConstrainedToDomainType_RuleFails()
    {
        const string source = """
            namespace SharedKernel.Domain
            {
                public interface IDomainEvent { }
            }

            namespace SharedKernel.Contracts
            {
                public sealed record EventEnvelope<TEvent>(TEvent Payload)
                    where TEvent : SharedKernel.Domain.IDomainEvent;
            }
            """;

        var assembly = CompileInMemory("EnvelopeDomainConstraint", source);
        var result = ContractsPurityRules
            .ContractsAssembliesHaveNoDomainTypeOnPublicSurface(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "EventEnvelope`1 is constrained to a domain type and no type is exempt any more");
    }

    /// <summary>The rule passes against the real <c>SharedKernel.Contracts</c> assembly.</summary>
    [Fact]
    public void ContractsAssembliesHaveNoDomainTypeOnPublicSurface_RealContractsAssembly_RulePasses()
    {
        var result = ContractsPurityRules
            .ContractsAssembliesHaveNoDomainTypeOnPublicSurface(RealContractsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "SharedKernel.Contracts does not reference SharedKernel.Domain");
    }

    // ---------------------------------------------------------------------------
    // No Result type on the public surface
    // ---------------------------------------------------------------------------

    /// <summary>A contracts type with a <c>Result&lt;T&gt;</c> property fails the rule.</summary>
    [Fact]
    public void ContractsAssembliesHaveNoResultTypeOnPublicSurface_ResultTypeExposed_RuleFails()
    {
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
        result.FailingTypeNames.Should().ContainSingle().Which.Should().Be("SharedKernel.Contracts.CreateOrderResponse");
    }

    /// <summary>
    /// The real outcome types are caught when wrapped in a nullable, a collection or an array, and through a
    /// public field.
    /// </summary>
    [Fact]
    public void ContractsAssembliesHaveNoResultTypeOnPublicSurface_WrappedRealOutcomeTypes_RuleFails()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using SharedKernel.Primitives.Results;
            namespace MyContracts
            {
                public sealed record NullableStructResponse(Result? Outcome);

                public sealed record ListResponse(IReadOnlyList<ValidationResult<Guid>> Outcomes);

                public sealed class FieldResponse
                {
                    public ValidationResult[] Outcomes = [];
                }

                public sealed record CleanResponse(Guid OrderId);
            }
            """;

        var assembly = CompileInMemory("WrappedResultLeakage", source, PrimitivesLocation);
        var result = ContractsPurityRules
            .ContractsAssembliesHaveNoResultTypeOnPublicSurface(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse();
        result.FailingTypeNames.Should().BeEquivalentTo(
            "MyContracts.NullableStructResponse",
            "MyContracts.ListResponse",
            "MyContracts.FieldResponse");
    }

    /// <summary>
    /// A static factory returning <c>ValidationResult&lt;T&gt;</c> and a codec returning <c>Result&lt;T&gt;</c>
    /// are allowed, as is referencing <c>Error</c>.
    /// </summary>
    [Fact]
    public void ContractsAssembliesHaveNoResultTypeOnPublicSurface_FactoryMethodsReturningOutcomes_RulePasses()
    {
        const string source = """
            using SharedKernel.Primitives.Errors;
            using SharedKernel.Primitives.Results;
            namespace MyContracts
            {
                public sealed record PageQuery
                {
                    private PageQuery(int page) => Page = page;

                    public int Page { get; }

                    public static ValidationResult<PageQuery> Create(int page) =>
                        page < 1
                            ? ValidationResult<PageQuery>.Failure([Error.Validation("page.out_of_range", "Page must be at least 1.")])
                            : ValidationResult<PageQuery>.Success(new PageQuery(page));
                }

                public static class QueryCursor
                {
                    public static Result<int> Decode(string cursor) =>
                        int.TryParse(cursor, out var value)
                            ? Result<int>.Success(value)
                            : Result<int>.Failure(Error.Validation("cursor.invalid", "Bad cursor."));
                }
            }
            """;

        var assembly = CompileInMemory("OutcomeFactories", source, PrimitivesLocation);
        var result = ContractsPurityRules
            .ContractsAssembliesHaveNoResultTypeOnPublicSurface(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "outcome types appear only as method return types");
    }

    /// <summary>
    /// The rule passes against the real <c>SharedKernel.Contracts</c> assembly, whose <c>PageRequest.Create</c>,
    /// <c>CursorPageRequest.Create</c> and <c>PageCursor.Decode</c> return outcome types.
    /// </summary>
    [Fact]
    public void ContractsAssembliesHaveNoResultTypeOnPublicSurface_RealContractsAssembly_RulePasses()
    {
        var result = ContractsPurityRules
            .ContractsAssembliesHaveNoResultTypeOnPublicSurface(RealContractsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "SharedKernel.Contracts returns outcome types only from factory methods, never through a property or field");
    }

    // ---------------------------------------------------------------------------
    // Integration events are sealed
    // ---------------------------------------------------------------------------

    /// <summary>A non-sealed class implementing <c>IIntegrationEvent</c> fails the rule.</summary>
    [Fact]
    public void IntegrationEventImplementationsMustBeSealed_NonSealedImplementation_RuleFails()
    {
        const string source = """
            using System;
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

        var assembly = CompileInMemory("NonSealedIntegrationEvent", source, ContractsLocation);

        var result = ContractsPurityRules
            .IntegrationEventImplementationsMustBeSealed(assembly, typeof(IIntegrationEvent))
            .GetResult();

        result.IsSuccessful.Should().BeFalse(because: "OrderCreatedEvent is not sealed");
    }

    /// <summary>When all <c>IIntegrationEvent</c> implementations are sealed, the rule passes.</summary>
    [Fact]
    public void IntegrationEventImplementationsMustBeSealed_SealedImplementation_RulePasses()
    {
        const string source = """
            using System;
            namespace MyContracts
            {
                // Sealed record — compliant
                public sealed record OrderCreatedEvent(
                    Guid EventId,
                    DateTimeOffset OccurredOn)
                    : SharedKernel.Contracts.Events.IIntegrationEvent;
            }
            """;

        var assembly = CompileInMemory("SealedIntegrationEvent", source, ContractsLocation);

        var result = ContractsPurityRules
            .IntegrationEventImplementationsMustBeSealed(assembly, typeof(IIntegrationEvent))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "OrderCreatedEvent is a sealed record");
    }

    /// <summary>The rule passes against the real <c>SharedKernel.Contracts</c> assembly.</summary>
    [Fact]
    public void IntegrationEventImplementationsMustBeSealed_RealContractsAssembly_RulePasses()
    {
        var result = ContractsPurityRules
            .IntegrationEventImplementationsMustBeSealed(RealContractsAssembly, typeof(IIntegrationEvent))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "SharedKernel.Contracts ships no non-sealed integration event");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Assembly CompileInMemory(
        string assemblyName,
        string source,
        params string[] extraReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var trustedPlatformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        var refList = trustedPlatformAssemblies
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal)
                || Path.GetFileName(path) is "mscorlib.dll" or "netstandard.dll")
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        foreach (var refPath in extraReferences)
            refList.Add(MetadataReference.CreateFromFile(refPath));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: refList,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

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
                throw new InvalidOperationException(
                    $"Fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
            }
        }

        return Assembly.LoadFrom(tempPath);
    }
}
