using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="MessagingArchitectureRules"/> — covering both predicates
/// (SK0701 and SK0702) that enforce messaging architecture boundaries.
/// </summary>
/// <remarks>
/// T-87/T-88/T-89: SK0701 — NoDirectBusInjectionOutsideMessaging
/// T-90/T-91: SK0702 — NoEventPublisherInDomainLayer
/// </remarks>
public class MessagingArchitectureRulesTests
{
    // ---------------------------------------------------------------------------
    // T-87 — SK0701 fire path: IBus injection in application handler fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-87: An application assembly containing a command handler class with a constructor
    /// parameter typed <c>IBus</c> must fail
    /// <see cref="MessagingArchitectureRules.NoDirectBusInjectionOutsideMessaging"/>.
    /// The failure must reference the offending type.
    /// </summary>
    [Fact]
    public void NoDirectBusInjectionOutsideMessaging_IBusInApplicationHandler_RuleFails()
    {
        // Arrange: application handler injects IBus directly instead of IMessageBus
        const string source = """
            namespace Application.Commands
            {
                // Stub simulating MassTransit IBus
                public interface IBus { }

                // Violation: application handler injects MassTransit IBus directly
                // Use IMessageBus from SharedKernel.Messaging.Abstractions instead
                public class PlaceOrderCommandHandler
                {
                    private readonly IBus _bus;

                    public PlaceOrderCommandHandler(IBus bus)
                    {
                        _bus = bus;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("IBusInjectionViolation", source);

        var result = MessagingArchitectureRules
            .NoDirectBusInjectionOutsideMessaging(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "PlaceOrderCommandHandler injects IBus (a MassTransit transport type) directly " +
                     "— SK0701 requires using IMessageBus from SharedKernel.Messaging.Abstractions");
    }

    // ---------------------------------------------------------------------------
    // T-88 — SK0701 pass path (namespace exemption): SharedKernel.Messaging.MassTransit exempt
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-88: A type in the <c>SharedKernel.Messaging.MassTransit</c> namespace that injects
    /// <c>IBus</c> must pass
    /// <see cref="MessagingArchitectureRules.NoDirectBusInjectionOutsideMessaging"/>
    /// because the <c>SharedKernel.Messaging.*</c> namespace prefix exemption applies.
    /// </summary>
    [Fact]
    public void NoDirectBusInjectionOutsideMessaging_MessagingNamespaceExemption_RulePasses()
    {
        // Arrange: type inside SharedKernel.Messaging.MassTransit injects IBus (legitimate)
        const string source = """
            namespace SharedKernel.Messaging.MassTransit
            {
                // Stub simulating MassTransit IBus
                public interface IBus { }

                // Compliant: SharedKernel.Messaging.* types may reference MassTransit directly —
                // this namespace owns the transport wiring
                public class MassTransitMessageBus
                {
                    private readonly IBus _bus;

                    public MassTransitMessageBus(IBus bus)
                    {
                        _bus = bus;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("MessagingNamespaceExempt", source);

        var result = MessagingArchitectureRules
            .NoDirectBusInjectionOutsideMessaging(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "MassTransitMessageBus is inside SharedKernel.Messaging.MassTransit — " +
                     "the namespace exemption applies and IBus injection is permitted");
    }

    // ---------------------------------------------------------------------------
    // T-89 — SK0701 pass path (abstraction): IMessageBus injection passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-89: An application assembly containing a command handler that injects
    /// <c>IMessageBus</c> (the abstraction, not a MassTransit transport type) must pass
    /// <see cref="MessagingArchitectureRules.NoDirectBusInjectionOutsideMessaging"/>.
    /// </summary>
    [Fact]
    public void NoDirectBusInjectionOutsideMessaging_IMessageBusAbstractionInjection_RulePasses()
    {
        // Arrange: handler correctly injects IMessageBus (the SharedKernel abstraction)
        const string source = """
            namespace Application.Commands
            {
                // The SharedKernel abstraction — not a MassTransit transport type
                public interface IMessageBus
                {
                    System.Threading.Tasks.Task SendAsync<T>(T message,
                        System.Threading.CancellationToken ct = default);
                }

                // Compliant: application handler injects IMessageBus (the abstraction)
                public class PlaceOrderCommandHandler
                {
                    private readonly IMessageBus _messageBus;

                    public PlaceOrderCommandHandler(IMessageBus messageBus)
                    {
                        _messageBus = messageBus;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("IMessageBusAbstractionCompliant", source);

        var result = MessagingArchitectureRules
            .NoDirectBusInjectionOutsideMessaging(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "PlaceOrderCommandHandler injects IMessageBus (not a forbidden MassTransit " +
                     "transport type) — SK0701 only fires on IBus, IPublishEndpoint, ISendEndpointProvider");
    }

    // ---------------------------------------------------------------------------
    // T-90 — SK0702 fire path: domain service injecting IEventPublisher fails
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-90: A domain assembly containing a domain service class with a constructor parameter
    /// typed <c>IEventPublisher</c> must fail
    /// <see cref="MessagingArchitectureRules.NoEventPublisherInDomainLayer"/>.
    /// The failure must reference the offending type.
    /// </summary>
    [Fact]
    public void NoEventPublisherInDomainLayer_DomainServiceInjectsIEventPublisher_RuleFails()
    {
        // Arrange: domain service injects IEventPublisher — the correct flow is
        // domain event → IDomainEventDispatcher → application handler → IEventPublisher
        const string source = """
            namespace Orders.Domain.Services
            {
                // Stub simulating the IEventPublisher interface
                public interface IEventPublisher
                {
                    System.Threading.Tasks.Task PublishAsync<T>(T integrationEvent,
                        System.Threading.CancellationToken ct = default);
                }

                // Stub simulating the IDomainService marker interface
                public interface IDomainService { }

                // Violation: domain service injects IEventPublisher directly
                // Domain types must raise events via AddDomainEvent(), not by calling IEventPublisher
                public class OrderPricingService : IDomainService
                {
                    private readonly IEventPublisher _publisher;

                    public OrderPricingService(IEventPublisher publisher)
                    {
                        _publisher = publisher;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("DomainServiceEventPublisherViolation", source);

        var result = MessagingArchitectureRules
            .NoEventPublisherInDomainLayer(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "OrderPricingService is a domain service (implements IDomainService) that injects " +
                     "IEventPublisher — SK0702 prohibits this; the correct flow is AddDomainEvent() → " +
                     "IDomainEventDispatcher → application handler → IEventPublisher");
    }

    // ---------------------------------------------------------------------------
    // T-91 — SK0702 pass path: application service injecting IEventPublisher passes
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-91: An application assembly containing an application service class with a constructor
    /// parameter typed <c>IEventPublisher</c> must pass
    /// <see cref="MessagingArchitectureRules.NoEventPublisherInDomainLayer"/>
    /// because application-layer types are not in scope of SK0702.
    /// </summary>
    [Fact]
    public void NoEventPublisherInDomainLayer_ApplicationServiceInjectsIEventPublisher_RulePasses()
    {
        // Arrange: application service injects IEventPublisher — this is the correct layer
        const string source = """
            namespace Orders.Application.EventHandlers
            {
                // Stub simulating the IEventPublisher interface
                public interface IEventPublisher
                {
                    System.Threading.Tasks.Task PublishAsync<T>(T integrationEvent,
                        System.Threading.CancellationToken ct = default);
                }

                // Compliant: application handler receives domain event and publishes
                // integration event via IEventPublisher — this is the correct flow
                public class OrderCreatedDomainEventHandler
                {
                    private readonly IEventPublisher _publisher;

                    // Application layer is the correct place to inject IEventPublisher
                    public OrderCreatedDomainEventHandler(IEventPublisher publisher)
                    {
                        _publisher = publisher;
                    }
                }
            }
            """;

        var assembly = CompileInMemory("ApplicationServiceEventPublisherCompliant", source);

        var result = MessagingArchitectureRules
            .NoEventPublisherInDomainLayer(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "OrderCreatedDomainEventHandler is in the application namespace " +
                     "(does not contain '.Domain.') and does not implement a domain marker interface — " +
                     "SK0702 does not apply to application-layer types");
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
