using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using FluentAssertions;
using MassTransit;
using MsOptions = Microsoft.Extensions.Options.Options;
using NSubstitute;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Messaging.MassTransit.Tests.ErrorSafetyTests;

/// <summary>
/// SK.07.MessagingErrorSafety T-02: an unrecognised exception leaves <see cref="IMessageBus"/> with its original stack
/// trace (a <c>throw ex;</c> would restart it at the dispatch frame), a recognised broker fault still becomes a failed
/// <c>Result</c>, and cancellation still propagates.
/// </summary>
public sealed class DispatchRethrowTests
{
    [Fact]
    public async Task PublishAsync_UnrecognisedException_RethrowsTheSameInstanceWithTheThrowersFrame()
    {
        var thrown = new InvalidOperationException("broker adapter defect");
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        publishEndpoint
            .Publish(Arg.Any<RethrowMessage>(), Arg.Any<IPipe<PublishContext<RethrowMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowFromBrokerAdapterPublishFrame(thrown));

        var bus = CreateBus(publishEndpoint, Substitute.For<ISendEndpointProvider>());

        var act = async () => await bus.PublishAsync(new RethrowMessage("p"), CancellationToken.None);

        var caught = (await act.Should().ThrowAsync<InvalidOperationException>()).Subject.Single();
        caught.Should().BeSameAs(thrown);
        caught.StackTrace.Should().Contain(nameof(ThrowFromBrokerAdapterPublishFrame),
            "a plain rethrow keeps the frame that threw; 'throw ex;' would drop it");
    }

    [Fact]
    public async Task SendAsync_UnrecognisedException_RethrowsTheSameInstanceWithTheThrowersFrame()
    {
        var thrown = new InvalidOperationException("send adapter defect");
        var endpoint = Substitute.For<ISendEndpoint>();
        endpoint
            .Send(Arg.Any<RethrowMessage>(), Arg.Any<IPipe<SendContext<RethrowMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowFromBrokerAdapterSendFrame(thrown));
        var provider = Substitute.For<ISendEndpointProvider>();
        provider.GetSendEndpoint(Arg.Any<Uri>()).Returns(Task.FromResult(endpoint));

        var bus = CreateBus(Substitute.For<IPublishEndpoint>(), provider);

        var act = async () => await bus.SendAsync(new RethrowMessage("s"), CancellationToken.None);

        var caught = (await act.Should().ThrowAsync<InvalidOperationException>()).Subject.Single();
        caught.Should().BeSameAs(thrown);
        caught.StackTrace.Should().Contain(nameof(ThrowFromBrokerAdapterSendFrame));
    }

    [Fact]
    public async Task PublishAsync_RecognisedFault_StillReturnsUnavailable()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        publishEndpoint
            .Publish(Arg.Any<RethrowMessage>(), Arg.Any<IPipe<PublishContext<RethrowMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowFromBrokerAdapterPublishFrame(new ConnectionException("broker down")));

        var bus = CreateBus(publishEndpoint, Substitute.For<ISendEndpointProvider>());

        var result = await bus.PublishAsync(new RethrowMessage("p"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessagingErrorCodes.Unavailable);
        result.Error.Type.Should().Be(ErrorType.Unavailable);
    }

    [Fact]
    public async Task SendAsync_RecognisedFault_StillReturnsUnavailable()
    {
        var endpoint = Substitute.For<ISendEndpoint>();
        endpoint
            .Send(Arg.Any<RethrowMessage>(), Arg.Any<IPipe<SendContext<RethrowMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowFromBrokerAdapterSendFrame(new ConnectionException("broker down")));
        var provider = Substitute.For<ISendEndpointProvider>();
        provider.GetSendEndpoint(Arg.Any<Uri>()).Returns(Task.FromResult(endpoint));

        var bus = CreateBus(Substitute.For<IPublishEndpoint>(), provider);

        var result = await bus.SendAsync(new RethrowMessage("s"), CancellationToken.None);

        result.Error.Code.Should().Be(MessagingErrorCodes.Unavailable);
    }

    [Fact]
    public async Task PublishAsync_Cancellation_StillPropagates()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        publishEndpoint
            .Publish(Arg.Any<RethrowMessage>(), Arg.Any<IPipe<PublishContext<RethrowMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowFromBrokerAdapterPublishFrame(new OperationCanceledException()));

        var bus = CreateBus(publishEndpoint, Substitute.For<ISendEndpointProvider>());

        var act = async () => await bus.PublishAsync(new RethrowMessage("p"), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task ThrowFromBrokerAdapterPublishFrame(Exception exception) => throw exception;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task ThrowFromBrokerAdapterSendFrame(Exception exception) => throw exception;

    private static MassTransitMessageBus CreateBus(IPublishEndpoint publishEndpoint, ISendEndpointProvider sendEndpointProvider) =>
        new(
            publishEndpoint,
            sendEndpointProvider,
            Substitute.For<IServiceProvider>(),
            new Dictionary<Type, string>(),
            new ConventionSendEndpointResolver(MsOptions.Create(new MessagingOptions { ServiceName = "rethrow-service" })),
            NullLogger<MassTransitMessageBus>.Instance);
}

public sealed record RethrowMessage(string Text);
