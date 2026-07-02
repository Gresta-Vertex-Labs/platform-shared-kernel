using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.FireAndForget;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.FireAndForget;

/// <summary>
/// Verifies <see cref="FireAndForgetGuardBehavior{TRequest,TResponse}"/> (T-26):
/// when a caller mistakenly routes an <see cref="IFireAndForgetCommand"/> through
/// <c>ISender.Send</c>, an <see cref="InvalidOperationException"/> is thrown with the
/// documented message format before the handler runs.
/// </summary>
public sealed class FireAndForgetGuardBehaviorTests
{
    // IFireAndForgetCommand already extends ICommand, so no double-declaration needed.
    private sealed record AccidentallySentFnFCommand : IFireAndForgetCommand;

    // A plain command that does NOT implement IFireAndForgetCommand — guard must not interfere.
    private sealed record PlainCommand : ICommand;

    private sealed class PlainCommandHandler : IRequestHandler<PlainCommand, Result>
    {
        public bool WasInvoked { get; private set; }

        public Task<Result> Handle(PlainCommand request, CancellationToken ct)
        {
            WasInvoked = true;
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FnFCommandHandler : IRequestHandler<AccidentallySentFnFCommand, Result>
    {
        public bool WasInvoked { get; private set; }

        public Task<Result> Handle(AccidentallySentFnFCommand request, CancellationToken ct)
        {
            WasInvoked = true;
            return Task.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task Send_FireAndForgetCommandViaISender_ThrowsInvalidOperationExceptionWithDocumentedMessage()
    {
        var handler = new FnFCommandHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<AccidentallySentFnFCommand, Result>>(handler);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FireAndForgetGuardBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FireAndForgetGuardBehaviorTests>());
        var provider = services.BuildServiceProvider();

        var act = async () => await provider.GetRequiredService<ISender>().Send(new AccidentallySentFnFCommand());

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();

        // Verify the message matches the documented format exactly.
        var expectedTypeName = typeof(AccidentallySentFnFCommand).FullName ?? typeof(AccidentallySentFnFCommand).Name;
        thrown.Which.Message.Should().Contain(expectedTypeName);
        thrown.Which.Message.Should().Contain("IFireAndForgetCommand");
        thrown.Which.Message.Should().Contain("ISender.Send");
        thrown.Which.Message.Should().Contain("IFireAndForgetDispatcher.EnqueueAsync");

        // Handler must never have been invoked.
        handler.WasInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task Send_PlainCommand_GuardDoesNotInterfere_HandlerIsInvoked()
    {
        var handler = new PlainCommandHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<PlainCommand, Result>>(handler);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(FireAndForgetGuardBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FireAndForgetGuardBehaviorTests>());
        var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ISender>().Send(new PlainCommand());

        result.IsSuccess.Should().BeTrue();
        handler.WasInvoked.Should().BeTrue();
    }
}
