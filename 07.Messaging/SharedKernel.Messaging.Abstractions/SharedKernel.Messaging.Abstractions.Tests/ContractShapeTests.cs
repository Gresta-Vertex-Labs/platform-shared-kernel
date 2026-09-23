using FluentAssertions;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.Extensions;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Options;

namespace SharedKernel.Messaging.Abstractions.Tests;

/// <summary>
/// Compilation/contract shape tests for Abstractions interfaces.
/// Verifies T-01 / T-02 interface existence and method signatures compile correctly.
/// AddSharedKernelMessaging DI test (T-03) lives in the MassTransit test project because
/// the extension method and builder live in SharedKernel.Messaging.MassTransit.
/// </summary>
public sealed class ContractShapeTests
{
    [Fact]
    public void IMessageBus_Exists_AndIsInterface()
    {
        typeof(IMessageBus).IsInterface.Should().BeTrue();
    }

    [Fact]
    public void IMessageBus_HasPublishAsync_WithoutConfigure()
    {
        var method = typeof(IMessageBus).GetMethod(nameof(IMessageBus.PublishAsync),
            [typeof(object), typeof(CancellationToken)]);
        // Generic method — use GetMethods to find by name
        var methods = typeof(IMessageBus).GetMethods()
            .Where(m => m.Name == nameof(IMessageBus.PublishAsync) && m.IsGenericMethodDefinition)
            .ToList();
        methods.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void IMessageBus_HasSendAsync()
    {
        var methods = typeof(IMessageBus).GetMethods()
            .Where(m => m.Name == nameof(IMessageBus.SendAsync) && m.IsGenericMethodDefinition)
            .ToList();
        methods.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void IEventPublisher_Exists_AndIsInterface()
    {
        typeof(IEventPublisher).IsInterface.Should().BeTrue();
    }

    [Fact]
    public void IEventPublisher_HasPublishAsync_TwoOverloads()
    {
        var methods = typeof(IEventPublisher).GetMethods()
            .Where(m => m.Name == nameof(IEventPublisher.PublishAsync) && m.IsGenericMethodDefinition)
            .ToList();
        methods.Should().HaveCount(2, "IEventPublisher has two PublishAsync overloads");
    }

    [Fact]
    public void IEventPublisher_PublishAsync_ConstrainsTEventToIntegrationEvents()
    {
        var methods = typeof(IEventPublisher).GetMethods()
            .Where(m => m.Name == nameof(IEventPublisher.PublishAsync) && m.IsGenericMethodDefinition)
            .ToList();

        methods.Should().AllSatisfy(m =>
            m.GetGenericArguments().Single().GetGenericParameterConstraints()
                .Should().Contain(typeof(IIntegrationEvent),
                    "only IIntegrationEvent types may be published through IEventPublisher"));
    }

    [Fact]
    public void IMessagingBuilder_Exists_AndIsInterface()
    {
        typeof(IMessagingBuilder).IsInterface.Should().BeTrue();
    }

    [Fact]
    public void IMessagingBuilder_HasServicesProperty()
    {
        var prop = typeof(IMessagingBuilder).GetProperty(nameof(IMessagingBuilder.Services));
        prop.Should().NotBeNull();
        prop!.CanRead.Should().BeTrue();
    }

    [Fact]
    public void PublishContext_IsSealed_AndNotRecord()
    {
        var t = typeof(PublishContext);
        t.IsSealed.Should().BeTrue();
        // Records have a Clone method; sealed class should not unless it extends a record
        t.GetMethod("<Clone>$").Should().BeNull("PublishContext must be a class, not a record");
    }

    [Fact]
    public void MessagingOptions_HasSectionName_Constant()
    {
        var field = typeof(MessagingOptions).GetField(nameof(MessagingOptions.SectionName));
        field.Should().NotBeNull();
        field!.IsLiteral.Should().BeTrue("SectionName should be a const");
    }
}
