using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Transaction;

/// <summary>
/// Verifies <see cref="TransactionBehavior{TRequest,TResponse}"/>'s cancellation semantics (T-20):
/// when <c>next()</c> throws <see cref="OperationCanceledException"/>,
/// <see cref="IUnitOfWork.SaveChangesAsync"/> is never called and the exception propagates unchanged.
/// </summary>
public sealed class TransactionBehaviorCancellationTests
{
    private sealed record CancellingCommand : ICommand;

    private sealed class CancellingHandler : IRequestHandler<CancellingCommand, Result>
    {
        public Task<Result> Handle(CancellingCommand request, CancellationToken cancellationToken)
            => throw new OperationCanceledException("cancellation requested");
    }

    private static ServiceProvider BuildProvider(IUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        services.AddTransient<IRequestHandler<CancellingCommand, Result>, CancellingHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<TransactionBehaviorCancellationTests>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_NextThrowsOperationCanceledException_SaveChangesAsyncIsNeverCalledAndExceptionPropagates()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var provider = BuildProvider(unitOfWork);
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new CancellingCommand());

        // The OperationCanceledException must propagate unchanged.
        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Message.Should().Be("cancellation requested");

        // SaveChangesAsync must never have been called.
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
