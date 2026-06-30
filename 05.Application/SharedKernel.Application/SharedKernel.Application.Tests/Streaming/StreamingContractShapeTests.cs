using System.Runtime.CompilerServices;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Streaming;

namespace SharedKernel.Application.Tests.Streaming;

/// <summary>
/// Verifies <see cref="IStreamQuery{TResponse}"/>/<see cref="IStreamQueryHandler{TQuery,TResponse}"/>
/// compile and resolve through MediatR's <c>IStreamMediator</c>/<c>ISender.CreateStream</c> exactly
/// as a hand-written <see cref="IStreamRequestHandler{TRequest,TResponse}"/> would. No behavior
/// test is needed since zero pipeline behaviors apply to this shape — a documented fact, not
/// something to test for absence.
/// </summary>
public sealed class StreamingContractShapeTests
{
    private sealed record ExportRowsStreamQuery(int Count) : IStreamQuery<int>;

    private sealed class ExportRowsStreamQueryHandler : IStreamQueryHandler<ExportRowsStreamQuery, int>
    {
        public async IAsyncEnumerable<int> Handle(
            ExportRowsStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < request.Count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }
    }

    [Fact]
    public void IStreamQuery_ImplementsIStreamRequestOfTResponse()
    {
        typeof(IStreamQuery<int>).Should().BeAssignableTo<IStreamRequest<int>>();
    }

    [Fact]
    public void IStreamQueryHandler_IsPureAliasOverIStreamRequestHandler()
    {
        typeof(IStreamQueryHandler<ExportRowsStreamQuery, int>).Should()
            .BeAssignableTo<IStreamRequestHandler<ExportRowsStreamQuery, int>>();
    }

    [Fact]
    public async Task ISenderCreateStream_ResolvesHandlerAndYieldsRawItems_NoResultWrapping()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStreamRequestHandler<ExportRowsStreamQuery, int>, ExportRowsStreamQueryHandler>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamingContractShapeTests>());
        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var items = new List<int>();
        await foreach (var item in sender.CreateStream(new ExportRowsStreamQuery(3)))
        {
            items.Add(item);
        }

        items.Should().Equal(0, 1, 2);
    }
}
