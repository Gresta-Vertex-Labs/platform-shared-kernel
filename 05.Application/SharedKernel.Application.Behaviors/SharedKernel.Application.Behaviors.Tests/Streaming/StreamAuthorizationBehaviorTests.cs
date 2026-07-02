using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.Streaming;
using SharedKernel.Application.Streaming;
using System.Runtime.CompilerServices;

namespace SharedKernel.Application.Behaviors.Tests.Streaming;

/// <summary>
/// Verifies <see cref="StreamAuthorizationBehavior{TRequest,TResponse}"/> (T-31):
/// <list type="bullet">
///   <item>Authorized → stream opens and yields items.</item>
///   <item>Unauthorized → <see cref="UnauthorizedAccessException"/> thrown before stream opens.</item>
///   <item>AllOf all-pass → stream opens.</item>
///   <item>AllOf fails → <see cref="UnauthorizedAccessException"/> before stream opens.</item>
///   <item>AnyOf first-passes → stream opens.</item>
///   <item>AnyOf all-fail → <see cref="UnauthorizedAccessException"/> before stream opens.</item>
///   <item>DI-contract: request not implementing <see cref="IAuthorizeRequest"/> never resolves this behavior.</item>
/// </list>
/// </summary>
public sealed class StreamAuthorizationBehaviorTests
{
    // ---- request types ----

    private sealed record AuthorizedStreamQuery : IStreamQuery<string>, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AllOfRequirements => ["stream:read"];
    }

    private sealed record UnauthorizedStreamQuery : IStreamQuery<string>, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AllOfRequirements => ["stream:admin"];
    }

    private sealed record AllOfAllPassStreamQuery : IStreamQuery<string>, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AllOfRequirements => ["perm:A", "perm:B"];
    }

    private sealed record AllOfFailsStreamQuery : IStreamQuery<string>, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AllOfRequirements => ["perm:DENY"];
    }

    private sealed record AnyOfPassStreamQuery : IStreamQuery<string>, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AnyOfRequirements => ["perm:PASS", "perm:OTHER"];
    }

    private sealed record AnyOfAllFailStreamQuery : IStreamQuery<string>, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AnyOfRequirements => ["perm:D1", "perm:D2"];
    }

    private sealed record PlainStreamQuery : IStreamQuery<string>;

    // ---- handlers ----

    private sealed class StringStreamHandler<TQuery> : IStreamRequestHandler<TQuery, string>
        where TQuery : IStreamRequest<string>
    {
        public int OpenCount { get; private set; }

        public async IAsyncEnumerable<string> Handle(TQuery request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            OpenCount++;
            yield return "item1";
            yield return "item2";
            await Task.CompletedTask;
        }
    }

    // ---- helper ----

    private static ServiceProvider BuildProvider<TQuery>(
        IAuthorizationContext context,
        IStreamRequestHandler<TQuery, string> handler)
        where TQuery : class, IStreamRequest<string>
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(context);
        services.AddSingleton(handler);
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamAuthorizationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamAuthorizationBehaviorTests>());
        return services.BuildServiceProvider();
    }

    // ---- tests ----

    [Fact]
    public async Task Handle_Authorized_AllOf_StreamOpensAndYieldsItems()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.AllOf(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(true);

        var handler = new StringStreamHandler<AuthorizedStreamQuery>();
        var provider = BuildProvider(context, handler);

        var items = new List<string>();
        await foreach (var item in provider.GetRequiredService<ISender>().CreateStream(new AuthorizedStreamQuery()))
            items.Add(item);

        items.Should().HaveCount(2);
        handler.OpenCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Unauthorized_AllOf_ThrowsUnauthorizedAccessExceptionBeforeStreamOpens()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.AllOf(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(false);

        var handler = new StringStreamHandler<UnauthorizedStreamQuery>();
        var provider = BuildProvider(context, handler);

        var act = async () =>
        {
            await foreach (var _ in provider.GetRequiredService<ISender>().CreateStream(new UnauthorizedStreamQuery())) { }
        };

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        handler.OpenCount.Should().Be(0, "stream must never open when authorization fails");
    }

    [Fact]
    public async Task Handle_AllOf_AllPass_StreamOpens()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.AllOf(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(true);

        var handler = new StringStreamHandler<AllOfAllPassStreamQuery>();
        var provider = BuildProvider(context, handler);

        var items = new List<string>();
        await foreach (var item in provider.GetRequiredService<ISender>().CreateStream(new AllOfAllPassStreamQuery()))
            items.Add(item);

        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_AllOf_Fails_ThrowsUnauthorizedAccessException()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.AllOf(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(false);

        var handler = new StringStreamHandler<AllOfFailsStreamQuery>();
        var provider = BuildProvider(context, handler);

        var act = async () =>
        {
            await foreach (var _ in provider.GetRequiredService<ISender>().CreateStream(new AllOfFailsStreamQuery())) { }
        };

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        handler.OpenCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_AnyOf_FirstPasses_StreamOpens()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.AnyOf(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(true);

        var handler = new StringStreamHandler<AnyOfPassStreamQuery>();
        var provider = BuildProvider(context, handler);

        var items = new List<string>();
        await foreach (var item in provider.GetRequiredService<ISender>().CreateStream(new AnyOfPassStreamQuery()))
            items.Add(item);

        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_AnyOf_AllFail_ThrowsUnauthorizedAccessException()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.AnyOf(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(false);

        var handler = new StringStreamHandler<AnyOfAllFailStreamQuery>();
        var provider = BuildProvider(context, handler);

        var act = async () =>
        {
            await foreach (var _ in provider.GetRequiredService<ISender>().CreateStream(new AnyOfAllFailStreamQuery())) { }
        };

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        handler.OpenCount.Should().Be(0);
    }

    [Fact]
    public void StreamAuthorizationBehavior_NotApplicable_ToStreamQueryWithoutIAuthorizeRequest()
    {
        // StreamAuthorizationBehavior<TRequest,TResponse> requires TRequest : IAuthorizeRequest.
        // PlainStreamQuery does not implement IAuthorizeRequest → the generic type cannot be closed.
        typeof(PlainStreamQuery).Should().NotBeAssignableTo<IAuthorizeRequest>();

        var closesOverPlainStreamQuery = () =>
            typeof(StreamAuthorizationBehavior<,>).MakeGenericType(typeof(PlainStreamQuery), typeof(string));

        closesOverPlainStreamQuery.Should().Throw<ArgumentException>(
            "StreamAuthorizationBehavior must be constrained to IAuthorizeRequest types only");
    }
}
