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
/// Tests for <see cref="DomainLayerPurityRules"/> and the three supporting predicates:
/// <see cref="DoesNotImplementOpenGenericInterfacePredicate"/>,
/// <see cref="DoesNotCallSystemClockPredicate"/>, and
/// <see cref="NoInfrastructureConstructorParametersPredicate"/>.
/// </summary>
public class DomainLayerPurityRulesTests
{
    // ---------------------------------------------------------------------------
    // T-23/T-24 — Rule 1: DomainAssembliesNeverReferenceInfrastructure
    // ---------------------------------------------------------------------------

    /// <summary>T-23: A domain assembly declaring types in EntityFramework namespace must fail Rule 1.</summary>
    [Fact]
    public void DomainAssembliesNeverReferenceInfrastructure_ViolatingAssembly_Fails()
    {
        // NetArchTest's NotHaveDependencyOn checks for the forbidden term as a prefix/substring
        // of referenced type namespaces. Use a namespace starting with "EntityFramework" so the
        // "EntityFramework" term matches.
        const string violationSource = """
            namespace EntityFramework.Core
            {
                public class DbContext { }
            }

            namespace SharedKernel.Domain
            {
                public class OrderAggregate
                {
                    private readonly EntityFramework.Core.DbContext _db;
                    public OrderAggregate(EntityFramework.Core.DbContext db) { _db = db; }
                }
            }
            """;

        var domainAssembly = CompileInMemory("InfraDomainViolation", violationSource);

        var result = DomainLayerPurityRules
            .DomainAssembliesNeverReferenceInfrastructure(domainAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderAggregate references EntityFramework.Core.DbContext which matches the forbidden 'EntityFramework' term");
    }

    /// <summary>T-24: A clean domain assembly passes Rule 1.</summary>
    [Fact]
    public void DomainAssembliesNeverReferenceInfrastructure_CleanAssembly_Passes()
    {
        const string source = """
            namespace SharedKernel.Domain
            {
                public class OrderAggregate
                {
                    public string OrderId { get; } = string.Empty;
                }
            }
            """;

        var assembly = CompileInMemory("CleanDomainRule1", source);
        var result = DomainLayerPurityRules
            .DomainAssembliesNeverReferenceInfrastructure(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderAggregate has no infrastructure references");
    }

    // ---------------------------------------------------------------------------
    // T-25/T-26 — Rule 2: DomainAssembliesNeverContainEventHandlers
    // ---------------------------------------------------------------------------

    /// <summary>T-25: A type implementing IDomainEventHandler fails Rule 2.</summary>
    [Fact]
    public void DomainAssembliesNeverContainEventHandlers_ViolatingAssembly_Fails()
    {
        // Arrange: compile a fixture with a type implementing IDomainEventHandler
        const string source = """
            namespace Application.Handlers
            {
                public interface IDomainEventHandler<TEvent> { void Handle(TEvent e); }
                public class OrderCreatedHandler : IDomainEventHandler<string>
                {
                    public void Handle(string e) { }
                }
            }
            """;

        var assembly = CompileInMemory("EventHandlerViolation", source);
        var tempPath = assembly.Location;
        using var cecilAssembly = AssemblyDefinition.ReadAssembly(tempPath);

        var typeDefinition = cecilAssembly.MainModule.Types
            .Single(t => t.Name == "OrderCreatedHandler");

        var predicate = new DoesNotImplementOpenGenericInterfacePredicate("IDomainEventHandler");
        predicate.MeetsRule(typeDefinition).Should().BeFalse(
            because: "OrderCreatedHandler implements IDomainEventHandler<string>");
    }

    /// <summary>T-26: A clean domain type passes Rule 2.</summary>
    [Fact]
    public void DomainAssembliesNeverContainEventHandlers_CleanAssembly_Passes()
    {
        const string source = """
            namespace SharedKernel.Domain
            {
                public class OrderAggregate
                {
                    public string Id { get; } = string.Empty;
                }
            }
            """;

        var assembly = CompileInMemory("CleanDomainRule2", source);
        var result = DomainLayerPurityRules
            .DomainAssembliesNeverContainEventHandlers(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderAggregate does not implement IDomainEventHandler");
    }

    // ---------------------------------------------------------------------------
    // T-27/T-28 — Rule 3: DomainAssembliesNeverCallSystemClock
    // ---------------------------------------------------------------------------

    /// <summary>T-27: A method calling DateTime.UtcNow fails Rule 3.</summary>
    [Fact]
    public void DomainAssembliesNeverCallSystemClock_ViolatingAssembly_Fails()
    {
        const string source = """
            using System;
            namespace SharedKernel.Domain
            {
                public class OrderAggregate
                {
                    public DateTimeOffset GetNow() => DateTime.UtcNow;
                }
            }
            """;

        var assembly = CompileInMemory("ClockViolation", source);
        var result = DomainLayerPurityRules
            .DomainAssembliesNeverCallSystemClock(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "GetNow() calls DateTime.UtcNow directly");
    }

    /// <summary>T-28: A clean domain type passes Rule 3.</summary>
    [Fact]
    public void DomainAssembliesNeverCallSystemClock_CleanAssembly_Passes()
    {
        const string source = """
            namespace SharedKernel.Domain
            {
                public interface IClock { System.DateTimeOffset UtcNow { get; } }
                public class OrderAggregate
                {
                    private readonly IClock _clock;
                    public OrderAggregate(IClock clock) { _clock = clock; }
                    public System.DateTimeOffset GetNow() => _clock.UtcNow;
                }
            }
            """;

        var assembly = CompileInMemory("CleanDomainRule3", source);
        var result = DomainLayerPurityRules
            .DomainAssembliesNeverCallSystemClock(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderAggregate uses IClock.UtcNow, not DateTime.UtcNow directly");
    }

    // ---------------------------------------------------------------------------
    // T-29/T-30 — Rule 4: DomainServicesHaveNoInfrastructureConstructorParameters
    // ---------------------------------------------------------------------------

    /// <summary>T-29: A domain service with infra constructor parameters fails Rule 4.</summary>
    [Fact]
    public void DomainServicesHaveNoInfrastructureConstructorParameters_ViolatingAssembly_Fails()
    {
        // Compile a fixture with IDomainService implementor having EF Core param
        const string source = """
            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext { }
            }

            namespace SharedKernel.Domain.Abstractions
            {
                public interface IDomainService { }
            }

            namespace SharedKernel.Domain.Services
            {
                public class PricingService : SharedKernel.Domain.Abstractions.IDomainService
                {
                    public PricingService(Microsoft.EntityFrameworkCore.DbContext db) { }
                }
            }
            """;

        var assembly = CompileInMemory("InfraParamViolation", source);
        var tempPath = assembly.Location;
        using var cecilAssembly = AssemblyDefinition.ReadAssembly(tempPath);
        var typeDefinition = cecilAssembly.MainModule.Types
            .First(t => t.Name == "PricingService");

        var predicate = new NoInfrastructureConstructorParametersPredicate();
        predicate.MeetsRule(typeDefinition).Should().BeFalse(
            because: "PricingService constructor accepts a DbContext parameter");
    }

    /// <summary>T-30: A clean domain service passes Rule 4.</summary>
    [Fact]
    public void DomainServicesHaveNoInfrastructureConstructorParameters_CleanAssembly_Passes()
    {
        const string source = """
            namespace SharedKernel.Domain.Abstractions
            {
                public interface IDomainService { }
                public interface IClock { System.DateTimeOffset UtcNow { get; } }
            }

            namespace SharedKernel.Domain.Services
            {
                public class PricingService : SharedKernel.Domain.Abstractions.IDomainService
                {
                    public PricingService(SharedKernel.Domain.Abstractions.IClock clock) { }
                }
            }
            """;

        var assembly = CompileInMemory("CleanDomainRule4", source);
        var result = DomainLayerPurityRules
            .DomainServicesHaveNoInfrastructureConstructorParameters(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "PricingService only accepts IClock which is a domain interface");
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
