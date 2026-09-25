using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Streaming;

namespace SharedKernel.Application.Mediator.MediatR;

/// <summary>
/// The kernel <see cref="ISender"/>, sending through MediatR.
/// </summary>
/// <remarks>
/// Registered transient, so <paramref name="services"/> is the provider of the scope the sender was
/// resolved from and a request sent from inside a handler runs in that same scope.
/// </remarks>
/// <param name="mediator">MediatR's sender.</param>
/// <param name="services">The provider of the current scope.</param>
internal sealed class MediatRSender(global::MediatR.ISender mediator, IServiceProvider services) : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dispatcher = services.GetKeyedService<RequestDispatcher<TResponse>>(request.GetType())
            ?? throw NoHandler(request.GetType(), "request handler (ICommandHandler, IQueryHandler or IRequestHandler)");

        return dispatcher.SendAsync(request, mediator, cancellationToken);
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamQuery<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dispatcher = services.GetKeyedService<StreamDispatcher<TResponse>>(request.GetType())
            ?? throw NoHandler(request.GetType(), "IStreamQueryHandler");

        return dispatcher.CreateStream(request, mediator, cancellationToken);
    }

    private static InvalidOperationException NoHandler(Type requestType, string handlerKind) =>
        new($"No {handlerKind} is registered for '{requestType.FullName}'. Handlers are discovered by "
            + "AddSharedKernelMediatR(assemblies): pass the assembly that declares the handler.");
}
