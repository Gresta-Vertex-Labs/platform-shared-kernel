using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.DomainServices;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="DomainGoldStandardRules.DomainServicesMustExtendAbstractBase"/>.
/// </summary>
/// <remarks>
/// T-31 (fire path): An <c>IDomainService</c> implementor that does not extend <c>DomainService</c> fails.
/// T-32 (pass path): All <c>IDomainService</c> implementors extending <c>DomainService</c> pass.
/// </remarks>
public class DomainGoldStandardRulesTests
{
    // ---------------------------------------------------------------------------
    // T-31 — Fire path: IDomainService implementor not extending DomainService
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-31: A type that implements <c>IDomainService</c> directly (not via <c>DomainService</c>)
    /// must cause <see cref="DomainGoldStandardRules.DomainServicesMustExtendAbstractBase"/> to fail.
    /// </summary>
    [Fact]
    public void DomainServicesMustExtendAbstractBase_DirectImplementation_RuleFails()
    {
        // Arrange — compile a fixture with an IDomainService implementor not extending DomainService
        const string violationSource = """
            namespace SharedKernel.Domain.Abstractions
            {
                public interface IDomainService { }
            }

            namespace MyDomain
            {
                // Directly implements IDomainService without extending DomainService — violation
                public class PricingService : SharedKernel.Domain.Abstractions.IDomainService
                {
                    public decimal CalculatePrice(decimal basePrice) => basePrice * 1.2m;
                }
            }
            """;

        var violationAssembly = CompileInMemory(
            "DirectIDomainServiceImpl",
            violationSource,
            extraReferences: new[] { typeof(IDomainService).Assembly.Location });

        // Act — use the real IDomainService and DomainService types
        var conditionList = DomainGoldStandardRules.DomainServicesMustExtendAbstractBase(
            violationAssembly,
            typeof(IDomainService),
            typeof(DomainService));
        var result = conditionList.GetResult();

        // Assert
        result.IsSuccessful.Should().BeFalse(
            because: "PricingService implements IDomainService directly without extending DomainService");
    }

    // ---------------------------------------------------------------------------
    // T-32 — Pass path: IDomainService implementors extend DomainService
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-32: When all <c>IDomainService</c> implementors extend <c>DomainService</c>,
    /// the rule must pass.
    /// </summary>
    [Fact]
    public void DomainServicesMustExtendAbstractBase_ExtendsAbstractBase_RulePasses()
    {
        // The real SharedKernel.Domain assembly — DomainService extends IDomainService properly
        var domainAssembly = typeof(IDomainService).Assembly;

        // Act
        var conditionList = DomainGoldStandardRules.DomainServicesMustExtendAbstractBase(
            domainAssembly,
            typeof(IDomainService),
            typeof(DomainService));
        var result = conditionList.GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            because: "DomainService abstract class properly extends IDomainService; no direct implementors exist in SharedKernel.Domain");
    }

    // ---------------------------------------------------------------------------
    // AggregateFactoriesMustCreateValidationResults
    // ---------------------------------------------------------------------------

    private const string FactoryFixturePrelude = """
        using System;
        using SharedKernel.Domain.Abstractions;
        using SharedKernel.Domain.Aggregates;
        using SharedKernel.Primitives.Results;

        namespace Shop
        {
            public sealed class Order : AggregateRoot<Guid> { public Order() { } }
            public sealed class Invoice : AggregateRoot<Guid> { public Invoice() { } }
        """;

    public static TheoryData<string, string, bool> FactoryCases => new()
    {
        {
            "static_create_returns_validation_result",
            "public sealed class OrderFactory : IAggregateFactory<Order, Guid> { public static ValidationResult<Order> Create() => ValidationResult<Order>.Success(new Order()); }",
            true
        },
        {
            "instance_create_returns_validation_result",
            "public sealed class OrderFactory : IAggregateFactory<Order, Guid> { public ValidationResult<Order> Create(string customer) => ValidationResult<Order>.Success(new Order()); }",
            true
        },
        {
            "create_returns_the_aggregate_directly",
            "public sealed class OrderFactory : IAggregateFactory<Order, Guid> { public Order Create() => new Order(); }",
            false
        },
        {
            "create_returns_a_different_aggregate",
            "public sealed class OrderFactory : IAggregateFactory<Order, Guid> { public ValidationResult<Invoice> Create() => ValidationResult<Invoice>.Success(new Invoice()); }",
            false
        },
        {
            "create_is_not_public",
            "public sealed class OrderFactory : IAggregateFactory<Order, Guid> { internal ValidationResult<Order> Create() => ValidationResult<Order>.Success(new Order()); }",
            false
        },
        {
            "no_create_method",
            "public sealed class OrderFactory : IAggregateFactory<Order, Guid> { public ValidationResult<Order> Build() => ValidationResult<Order>.Success(new Order()); }",
            false
        },
        {
            "class_that_is_not_a_factory",
            "public sealed class Unrelated { public Order Create() => new Order(); }",
            true
        },
    };

    [Theory]
    [MemberData(nameof(FactoryCases))]
    public void AggregateFactoriesMustCreateValidationResults_JudgesEachShape(string name, string factorySource, bool expectedSuccess)
    {
        var assembly = CompileInMemory(
            $"AggregateFactory_{name}",
            FactoryFixturePrelude + factorySource + "\n}",
            extraReferences:
            [
                typeof(IAggregateFactory<,>).Assembly.Location,
                typeof(SharedKernel.Primitives.Results.ValidationResult<>).Assembly.Location,
                Assembly.Load("System.Collections").Location,
            ]);

        var result = DomainGoldStandardRules.AggregateFactoriesMustCreateValidationResults(
            assembly,
            typeof(IAggregateFactory<,>),
            typeof(SharedKernel.Primitives.Results.ValidationResult<>)).GetResult();

        result.IsSuccessful.Should().Be(expectedSuccess, because: name);
    }

    [Fact]
    public void AggregateFactoriesMustCreateValidationResults_RealDomainAssembly_Passes() =>
        DomainGoldStandardRules.AggregateFactoriesMustCreateValidationResults(
                typeof(IAggregateFactory<,>).Assembly,
                typeof(IAggregateFactory<,>),
                typeof(SharedKernel.Primitives.Results.ValidationResult<>))
            .GetResult().IsSuccessful.Should().BeTrue();

    [Theory]
    [InlineData(typeof(IDomainService), "aggregateFactoryDefinition")]
    [InlineData(typeof(IAggregateFactory<SharedKernel.Domain.Aggregates.TenantedAggregateRoot<Guid>, Guid>), "aggregateFactoryDefinition")]
    public void AggregateFactoriesMustCreateValidationResults_NonDefinitionFactoryAnchor_Throws(Type anchor, string parameter)
    {
        var act = () => DomainGoldStandardRules.AggregateFactoriesMustCreateValidationResults(
            typeof(IDomainService).Assembly,
            anchor,
            typeof(SharedKernel.Primitives.Results.ValidationResult<>));

        act.Should().Throw<ArgumentException>().WithParameterName(parameter).WithMessage("*vacuously*");
    }

    [Fact]
    public void AggregateFactoriesMustCreateValidationResults_ClosedResultAnchor_Throws()
    {
        var act = () => DomainGoldStandardRules.AggregateFactoriesMustCreateValidationResults(
            typeof(IDomainService).Assembly,
            typeof(IAggregateFactory<,>),
            typeof(SharedKernel.Primitives.Results.ValidationResult<int>));

        act.Should().Throw<ArgumentException>().WithParameterName("validationResultDefinition");
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
