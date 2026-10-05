using MassTransit;
using MassTransit.Context;

namespace SharedKernel.Messaging.MassTransit.Consumers;

/// <summary>
/// Names the consumer (or saga) a message is being delivered to. Carried on the consume context so
/// <see cref="IdempotentConsumerBehavior{TMessage}"/> reserves the message id per consumer.
/// </summary>
/// <param name="Name">The consumer's or saga's full type name.</param>
internal sealed record IdempotentConsumerIdentity(string Name);

/// <summary>
/// Puts an <see cref="IdempotentConsumerIdentity"/> at the front of every consumer and saga message pipe, where
/// MassTransit also places scoped consume filters.
/// </summary>
/// <remarks>
/// MassTransit runs a bus-level scoped consume filter once per consumer of a message, in that consumer's own message
/// pipe, so every consumer of a message on one endpoint gets its own run of
/// <see cref="IdempotentConsumerBehavior{TMessage}"/>. Only this observer knows which consumer that is. It must be
/// connected before the scoped filter is registered so its filter runs first. It adds to the same pipe the scoped
/// filter uses (the message configurator is also the <c>ConsumeContext&lt;TMessage&gt;</c> pipe configurator);
/// <c>configurator.Message(...)</c> would configure a pipe that runs after it.
/// </remarks>
internal sealed class IdempotentConsumerIdentityObserver : IConsumerConfigurationObserver, ISagaConfigurationObserver
{
    public void ConsumerConfigured<TConsumer>(IConsumerConfigurator<TConsumer> configurator)
        where TConsumer : class
    {
    }

    public void ConsumerMessageConfigured<TConsumer, TMessage>(IConsumerMessageConfigurator<TConsumer, TMessage> configurator)
        where TConsumer : class
        where TMessage : class
        => AddIdentity<TMessage>(configurator, typeof(TConsumer));

    public void SagaConfigured<TSaga>(ISagaConfigurator<TSaga> configurator)
        where TSaga : class, ISaga
    {
    }

    public void StateMachineSagaConfigured<TInstance>(ISagaConfigurator<TInstance> configurator, SagaStateMachine<TInstance> stateMachine)
        where TInstance : class, ISaga, SagaStateMachineInstance
    {
    }

    public void SagaMessageConfigured<TSaga, TMessage>(ISagaMessageConfigurator<TSaga, TMessage> configurator)
        where TSaga : class, ISaga
        where TMessage : class
        => AddIdentity<TMessage>(configurator, typeof(TSaga));

    private static void AddIdentity<TMessage>(object configurator, Type consumerType)
        where TMessage : class
    {
        // Without the identity the behavior still keys by receive endpoint, which is correct for one consumer per
        // endpoint, so a configurator of another shape is left alone rather than failing the bus.
        if (configurator is IPipeConfigurator<ConsumeContext<TMessage>> messagePipe)
            messagePipe.UseFilter(new IdempotentConsumerIdentityFilter<TMessage>(consumerType));
    }
}

/// <summary>Passes the message on in a context scope carrying the consumer's <see cref="IdempotentConsumerIdentity"/>.</summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <remarks>
/// A scope rather than a payload on the context itself: every consumer of a message on one endpoint receives the same
/// context, so a payload added there would name whichever consumer ran first.
/// </remarks>
internal sealed class IdempotentConsumerIdentityFilter<TMessage> : IFilter<ConsumeContext<TMessage>>
    where TMessage : class
{
    private readonly IdempotentConsumerIdentity _identity;

    public IdempotentConsumerIdentityFilter(Type consumerType)
        => _identity = new IdempotentConsumerIdentity(consumerType.FullName ?? consumerType.Name);

    public void Probe(ProbeContext context)
        => context.CreateFilterScope("idempotent-consumer-identity").Add("consumer", _identity.Name);

    public Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
        => next.Send(new ConsumeContextScope<TMessage>(context, _identity));
}

/// <summary>Attaches consumer idempotency to a bus.</summary>
internal static class IdempotentConsumerConfiguration
{
    /// <summary>
    /// Adds <see cref="IdempotentConsumerBehavior{TMessage}"/> to every consumer and saga message pipe, preceded by the
    /// filter that tells it which consumer it guards.
    /// </summary>
    /// <param name="configurator">The bus configurator.</param>
    /// <param name="context">The registration context the scoped filter is resolved from.</param>
    public static void UseIdempotentConsumers(this IConsumePipeConfigurator configurator, IRegistrationContext context)
    {
        // Connected first: observers run in connection order, so the identity filter precedes the scoped filter.
        var observer = new IdempotentConsumerIdentityObserver();
        configurator.ConnectConsumerConfigurationObserver(observer);
        configurator.ConnectSagaConfigurationObserver(observer);

        configurator.UseConsumeFilter(typeof(IdempotentConsumerBehavior<>), context);
    }
}
