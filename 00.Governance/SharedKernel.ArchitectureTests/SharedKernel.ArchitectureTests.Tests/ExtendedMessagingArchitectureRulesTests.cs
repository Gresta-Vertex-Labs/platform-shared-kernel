using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="ExtendedMessagingArchitectureRules"/> — covering both predicates
/// (SK0706 and SK0707) introduced in WO-021 P-133.
/// </summary>
/// <remarks>
/// T-96/T-97: SK0706 — NoDirectMassTransitSchedulerInjection
/// T-98/T-99: SK0707 — SagaStatesMustExtendSagaStateBase
/// </remarks>
public class ExtendedMessagingArchitectureRulesTests
{
    // ---------------------------------------------------------------------------
    // T-96 — SK0706 fire path: MassTransit.IMessageScheduler injection fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-96: An application assembly containing a command handler with a constructor parameter
    /// typed <c>MassTransit.IMessageScheduler</c> must fail
    /// <see cref="ExtendedMessagingArchitectureRules.NoDirectMassTransitSchedulerInjection"/>.
    /// The failure must reference the offending type.
    /// </summary>
    [Fact]
    public void NoDirectMassTransitSchedulerInjection_MassTransitSchedulerInHandler_RuleFails()
    {
        // Arrange: application handler injects MassTransit.IMessageScheduler directly
        const string source = """
            namespace MassTransit
            {
                // Stub simulating the MassTransit IMessageScheduler interface
                public interface IMessageScheduler { }
            }

            namespace Application.Commands
            {
                using MassTransit;

                // Violation: application handler injects MassTransit.IMessageScheduler directly
                // Use SharedKernel.Messaging.Abstractions.IMessageScheduler instead
                public class ScheduleReminderHandler
                {
                    private readonly IMessageScheduler _scheduler;

                    public ScheduleReminderHandler(IMessageScheduler scheduler)
                    {
                        _scheduler = scheduler;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("MassTransitSchedulerInjectionViolation", source);

        var result = ExtendedMessagingArchitectureRules
            .NoDirectMassTransitSchedulerInjection(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "ScheduleReminderHandler injects MassTransit.IMessageScheduler directly " +
                     "— SK0706 requires using SharedKernel.Messaging.Abstractions.IMessageScheduler");
    }

    // ---------------------------------------------------------------------------
    // T-97 — SK0706 pass path: SharedKernel.Messaging.Abstractions.IMessageScheduler passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-97: An application assembly containing a service that injects only
    /// <c>SharedKernel.Messaging.Abstractions.IMessageScheduler</c> must pass
    /// <see cref="ExtendedMessagingArchitectureRules.NoDirectMassTransitSchedulerInjection"/>.
    /// </summary>
    [Fact]
    public void NoDirectMassTransitSchedulerInjection_PlatformAbstractionInjection_RulePasses()
    {
        // Arrange: handler correctly injects the platform IMessageScheduler abstraction
        const string source = """
            namespace SharedKernel.Messaging.Abstractions
            {
                // The platform abstraction — not a MassTransit transport type
                public interface IMessageScheduler
                {
                    System.Threading.Tasks.Task ScheduleSendAsync<T>(System.DateTimeOffset scheduledTime,
                        T message, System.Threading.CancellationToken ct = default);
                }
            }

            namespace Application.Services
            {
                using SharedKernel.Messaging.Abstractions;

                // Compliant: application service injects the platform IMessageScheduler abstraction
                public class ReminderSchedulingService
                {
                    private readonly IMessageScheduler _scheduler;

                    public ReminderSchedulingService(IMessageScheduler scheduler)
                    {
                        _scheduler = scheduler;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("PlatformSchedulerAbstractionCompliant", source);

        var result = ExtendedMessagingArchitectureRules
            .NoDirectMassTransitSchedulerInjection(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "ReminderSchedulingService injects SharedKernel.Messaging.Abstractions.IMessageScheduler " +
                     "(not MassTransit.IMessageScheduler) — SK0706 only fires on the MassTransit namespace form");
    }

    // ---------------------------------------------------------------------------
    // T-98 — SK0707 fire path: ISaga implementor not extending SagaStateBase fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-98: An assembly containing a class that implements <c>ISaga</c> but extends
    /// <c>object</c> directly (no <c>SagaStateBase</c>) must fail
    /// <see cref="ExtendedMessagingArchitectureRules.SagaStatesMustExtendSagaStateBase"/>.
    /// The failure must reference the offending type.
    /// </summary>
    [Fact]
    public void SagaStatesMustExtendSagaStateBase_IsSagaWithoutSagaStateBase_RuleFails()
    {
        // Arrange: saga state class implements ISaga but does not extend SagaStateBase
        const string source = """
            namespace SharedKernel.Messaging.MassTransit
            {
                // Stub simulating the MassTransit ISaga marker interface
                public interface ISaga
                {
                    System.Guid CorrelationId { get; set; }
                }
            }

            namespace Orders.Sagas
            {
                using SharedKernel.Messaging.MassTransit;

                // Violation: implements ISaga but does not extend SagaStateBase
                public class OrderSagaState : ISaga
                {
                    public System.Guid CorrelationId { get; set; }
                }
            }
            """;

        var assembly = CompileInMemory("SagaStateMissingBaseViolation", source);

        var result = ExtendedMessagingArchitectureRules
            .SagaStatesMustExtendSagaStateBase(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderSagaState implements ISaga but does not extend SagaStateBase " +
                     "— SK0707 requires every ISaga implementor to extend SagaStateBase");
    }

    // ---------------------------------------------------------------------------
    // T-99 — SK0707 pass path: ISaga implementor extending SagaStateBase passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-99: An assembly containing a saga state class that implements <c>ISaga</c> and extends
    /// <c>SagaStateBase</c> must pass
    /// <see cref="ExtendedMessagingArchitectureRules.SagaStatesMustExtendSagaStateBase"/>.
    /// </summary>
    [Fact]
    public void SagaStatesMustExtendSagaStateBase_IsSagaExtendingSagaStateBase_RulePasses()
    {
        // Arrange: saga state class implements ISaga and extends SagaStateBase
        const string source = """
            namespace SharedKernel.Messaging.MassTransit
            {
                // Stub simulating the MassTransit ISaga marker interface
                public interface ISaga
                {
                    System.Guid CorrelationId { get; set; }
                }

                // Stub simulating the platform SagaStateBase abstract class
                public abstract class SagaStateBase : ISaga
                {
                    public System.Guid CorrelationId { get; set; }
                    public int Version { get; set; }
                    public System.DateTimeOffset CreatedAt { get; set; }
                    public System.DateTimeOffset ModifiedAt { get; set; }
                }
            }

            namespace Orders.Sagas
            {
                using SharedKernel.Messaging.MassTransit;

                // Compliant: extends SagaStateBase, which implements ISaga
                public class OrderSagaState : SagaStateBase
                {
                }
            }
            """;

        var assembly = CompileInMemory("SagaStateWithBaseCompliant", source);

        var result = ExtendedMessagingArchitectureRules
            .SagaStatesMustExtendSagaStateBase(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderSagaState implements ISaga (transitively, via SagaStateBase) and " +
                     "extends SagaStateBase — SK0707 is satisfied");
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
