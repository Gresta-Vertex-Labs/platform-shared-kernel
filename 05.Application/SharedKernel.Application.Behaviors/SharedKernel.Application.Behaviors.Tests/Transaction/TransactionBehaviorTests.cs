using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Transaction;

/// <summary>
/// Verifies <see cref="TransactionBehavior{TRequest,TResponse}"/> commits exactly once after a
/// successful command handler and never commits when the handler throws; also verifies that a
/// query (no <see cref="ICommandBase"/>) never resolves this behavior into its pipeline.
/// </summary>
public sealed class TransactionBehaviorTests
{
    private sealed record TestCommand : ICommand;

    private sealed record TestQuery : IQuery<string>;

    private sealed class SucceedingCommandHandler : ICommandHandler<TestCommand>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed class ThrowingCommandHandler : ICommandHandler<TestCommand>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("handler blew up");
    }

    private sealed class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        public Task<Result<string>> Handle(TestQuery request, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Success("ok"));
    }

    private static ServiceProvider BuildProvider<THandler>(IUnitOfWork unitOfWork)
        where THandler : class, IRequestHandler<TestCommand, Result>
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        services.AddTransient<IRequestHandler<TestCommand, Result>, THandler>();
        services.AddTransient<IRequestHandler<TestQuery, Result<string>>, TestQueryHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<TransactionBehaviorTests>());

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_CommandSucceeds_CallsSaveChangesAsyncExactlyOnceAfterHandlerReturns()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var provider = BuildProvider<SucceedingCommandHandler>(unitOfWork);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand());

        result.IsSuccess.Should().BeTrue();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HandlerThrows_NeverCallsSaveChangesAsync()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var provider = BuildProvider<ThrowingCommandHandler>(unitOfWork);
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new TestCommand());

        await act.Should().ThrowAsync<InvalidOperationException>();
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public void TransactionBehavior_DoesNotResolveIntoQueryPipeline_DueToICommandBaseConstraint()
    {
        // TRequest : ICommandBase, IRequest<TResponse> — TestQuery never implements ICommandBase,
        // so TransactionBehavior<TestQuery, Result<string>> cannot be constructed: a DI-level
        // fact, not a runtime branch. This is a compile-time-shape assertion, mirroring the
        // documented "absent from a query's resolved pipeline" contract.
        typeof(TestQuery).Should().NotBeAssignableTo<ICommandBase>();

        var closesOverQuery = () => typeof(TransactionBehavior<,>)
            .MakeGenericType(typeof(TestQuery), typeof(Result<string>));

        closesOverQuery.Should().Throw<ArgumentException>();
    }
}
