using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel.Execution.Context;
using SharedKernel.Messaging.Abstractions.SchemaEvolution;
using SharedKernel.Messaging.MassTransit.Context;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.SchemaEvolution;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Messaging.MassTransit.Tests.ObservabilityTests;

/// <summary>
/// The startup advisories and the translation log, asserted by <c>EventId</c>, level, template and exception.
/// </summary>
public sealed class AdvisoryLogTests
{
    [Fact]
    public void TranslatorWithoutAConsumer_LogsAWarning_7009()
    {
        var logger = new InMemoryLogger();

        TranslatorRegistrationValidator.Validate(typeof(ObsOldSchema), typeof(ObsNewSchema), [], logger);

        logger.Records.ShouldHaveLogged(new EventId(MessagingLoggingEventIds.NoConsumerForNewSchema), LogLevel.Warning);
        logger.Records.ShouldHaveLoggedWithoutException(new EventId(MessagingLoggingEventIds.NoConsumerForNewSchema));
        logger.Records.ShouldFollowLogConvention();
        MessagingLoggingEventIds.NoConsumerForNewSchema.Should().Be(7009);
    }

    [Fact]
    public async Task ARequestContextOverriddenAfterTheBus_LogsAWarning_7011()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestContext>(_ => new PropagatedRequestContext(tenantId: null));
        await using var provider = services.BuildServiceProvider();
        var logger = new InMemoryLogger<RequestContextRegistrationAdvisoryHostedService>();

        await new RequestContextRegistrationAdvisoryHostedService(provider, logger).StartAsync(CancellationToken.None);

        logger.Records.ShouldHaveLogged(new EventId(MessagingLoggingEventIds.RequestContextOverridden), LogLevel.Warning);
        logger.Records.ShouldHaveLoggedTemplate(
            new EventId(MessagingLoggingEventIds.RequestContextOverridden),
            "IRequestContext resolves to '{ResolvedType}' instead of the message-aware context, so call AddSharedKernelRequestContext() before building the messaging bus.");
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public async Task ARequestContextThatCannotBeResolved_LogsADebugWithTheException_7012()
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestContext>(_ => throw new InvalidOperationException("no request in flight"));
        await using var provider = services.BuildServiceProvider();
        var logger = new InMemoryLogger<RequestContextRegistrationAdvisoryHostedService>();

        await new RequestContextRegistrationAdvisoryHostedService(provider, logger).StartAsync(CancellationToken.None);

        logger.Records.ShouldHaveLogged(new EventId(MessagingLoggingEventIds.RequestContextNotResolvable), LogLevel.Debug);
        logger.Records.ShouldHaveLoggedWithException(new EventId(MessagingLoggingEventIds.RequestContextNotResolvable), typeof(InvalidOperationException));
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public async Task ATranslation_LogsADebug_7008()
    {
        var translator = Substitute.For<IMessageVersionTranslator<ObsOldSchema, ObsNewSchema>>();
        translator.Translate(Arg.Any<ObsOldSchema>()).Returns(new ObsNewSchema("n"));
        var logger = new InMemoryLogger<VersionTranslatingConsumer<ObsOldSchema, ObsNewSchema>>();
        var consumer = new VersionTranslatingConsumer<ObsOldSchema, ObsNewSchema>(translator, logger);
        var context = Substitute.For<ConsumeContext<ObsOldSchema>>();
        context.Message.Returns(new ObsOldSchema("o"));

        await consumer.Consume(context);

        logger.Records.ShouldHaveLogged(new EventId(MessagingLoggingEventIds.Translating), LogLevel.Debug);
        logger.Records.ShouldFollowLogConvention();
    }
}

public sealed record ObsOldSchema(string Text);

public sealed record ObsNewSchema(string Text);
