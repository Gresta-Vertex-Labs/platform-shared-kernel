using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Authorization;

/// <summary>
/// Verifies <see cref="AuthorizationBehavior{TRequest,TResponse}"/>'s short-circuit, pass-through,
/// and no-marker-skip behavior, covering both the non-generic <see cref="Result"/> and the generic
/// <see cref="Result{T}"/> response shapes to exercise both branches of
/// <c>FailureResponseFactory</c>.
/// </summary>
public sealed class AuthorizationBehaviorTests
{
    private sealed record TestCommand : ICommand, IAuthorizeRequest
    {
        public string Requirement => "orders:create";
    }

    private sealed record TestQuery : IQuery<string>, IAuthorizeRequest
    {
        public string Requirement => "orders:view";
    }

    private sealed record PlainCommand : ICommand;

    private sealed class TestCommandHandler : IRequestHandler<TestCommand, Result>
    {
        public bool WasInvoked { get; private set; }

        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            WasInvoked = true;
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class TestQueryHandler : IRequestHandler<TestQuery, Result<string>>
    {
        public bool WasInvoked { get; private set; }

        public Task<Result<string>> Handle(TestQuery request, CancellationToken cancellationToken)
        {
            WasInvoked = true;
            return Task.FromResult(Result<string>.Success("ok"));
        }
    }

    private static ServiceProvider BuildCommandProvider(IAuthorizationContext context, TestCommandHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<TestCommand, Result>>(sp => sp.GetRequiredService<TestCommandHandler>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<AuthorizationBehaviorTests>());

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildQueryProvider(IAuthorizationContext context, TestQueryHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<TestQuery, Result<string>>>(sp => sp.GetRequiredService<TestQueryHandler>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<AuthorizationBehaviorTests>());

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_Unauthorized_NonGenericResult_HandlerNeverInvokedAndReturnsUnauthorizedFailure()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.IsAuthorizedAsync("orders:create", Arg.Any<CancellationToken>()).Returns(false);
        var handler = new TestCommandHandler();
        var provider = BuildCommandProvider(context, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand());

        handler.WasInvoked.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Handle_Unauthorized_GenericResultOfT_HandlerNeverInvokedAndReturnsUnauthorizedFailure()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.IsAuthorizedAsync("orders:view", Arg.Any<CancellationToken>()).Returns(false);
        var handler = new TestQueryHandler();
        var provider = BuildQueryProvider(context, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestQuery());

        handler.WasInvoked.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Handle_Authorized_InvokesNextAndReturnsItsResultUnchanged()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.IsAuthorizedAsync("orders:create", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new TestCommandHandler();
        var provider = BuildCommandProvider(context, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand());

        handler.WasInvoked.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void AuthorizationBehavior_DoesNotResolveIntoPipeline_ForRequestWithoutIAuthorizeRequestMarker()
    {
        // TRequest : IAuthorizeRequest, IRequest<TResponse> — PlainCommand never implements
        // IAuthorizeRequest, so AuthorizationBehavior<PlainCommand, Result> cannot be constructed:
        // a DI-level fact, not a runtime branch.
        typeof(PlainCommand).Should().NotBeAssignableTo<IAuthorizeRequest>();

        var closesOverPlainCommand = () => typeof(AuthorizationBehavior<,>)
            .MakeGenericType(typeof(PlainCommand), typeof(Result));

        closesOverPlainCommand.Should().Throw<ArgumentException>();
    }
}
