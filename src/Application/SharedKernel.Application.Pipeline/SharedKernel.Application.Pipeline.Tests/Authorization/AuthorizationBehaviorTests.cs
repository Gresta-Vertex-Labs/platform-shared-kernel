using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Commands;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Authorization;
using SharedKernel.Application.Pipeline.Tests.Support;
using SharedKernel.Application.Streaming;
using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Pipeline.Tests.Authorization;

public sealed class AuthorizationBehaviorTests
{
    private sealed record UnprotectedRequest : ICommand;

    [RequirePermission("orders.read")]
    private sealed record SinglePermissionRequest : ICommand;

    // Values of one attribute are alternatives.
    [RequirePermission("a", "b")]
    private sealed record AnyOfRequest : ICommand;

    // Several attributes all apply.
    [RequirePermission("a")]
    [RequirePermission("b")]
    private sealed record AllOfRequest : ICommand;

    // Any of (a, b) and c.
    [RequirePermission("a", "b")]
    [RequirePermission("c")]
    private sealed record MixedRequest : IQuery<int>;

    [RequirePermission("base.permission")]
    private abstract record ProtectedBase : ICommand;

    private sealed record DerivedRequest : ProtectedBase;

    private static Task<Result> Next(Action? onCalled = null)
    {
        onCalled?.Invoke();
        return Task.FromResult(Result.Success());
    }

    private static FakeRequestContext Caller(params string[] permissions)
        => new(isAuthenticated: true, new HashSet<string>(permissions));

    /// <summary>The behavior over a provider holding <paramref name="caller"/> as the only service.</summary>
    private static AuthorizationBehavior<TRequest, TResponse> Behavior<TRequest, TResponse>(IRequestContext caller)
        where TRequest : IRequest<TResponse>
        => new(new ServiceCollection().AddSingleton(caller).BuildServiceProvider());

    [Fact]
    public async Task Handle_NoAttribute_PassesWithoutResolvingTheCaller()
    {
        // Resolving IRequestContext would throw: an unmarked request must never touch it.
        var provider = new ServiceCollection()
            .AddSingleton<IRequestContext>(_ => throw new InvalidOperationException("IRequestContext was resolved."))
            .BuildServiceProvider();
        var behavior = new AuthorizationBehavior<UnprotectedRequest, Result>(provider);

        var result = await behavior.Handle(new UnprotectedRequest(), () => Next(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoAttribute_NoRequestContextRegistered_CallsNext()
    {
        var behavior = new AuthorizationBehavior<UnprotectedRequest, Result>(new ServiceCollection().BuildServiceProvider());

        var result = await behavior.Handle(new UnprotectedRequest(), () => Next(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AttributeButNoRequestContextRegistered_ThrowsNamingTheRequestAndTheFix()
    {
        var behavior = new AuthorizationBehavior<SinglePermissionRequest, Result>(new ServiceCollection().BuildServiceProvider());
        var nextCalled = false;

        var act = () => behavior.Handle(new SinglePermissionRequest(), () => Next(() => nextCalled = true), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should()
            .Contain(typeof(SinglePermissionRequest).FullName)
            .And.Contain("AddSharedKernelRequestContext()")
            .And.Contain(nameof(IRequestContext));
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsUnauthorizedWithoutCallingNext()
    {
        var behavior = Behavior<SinglePermissionRequest, Result>(new FakeRequestContext(isAuthenticated: false));
        var nextCalled = false;

        var result = await behavior.Handle(new SinglePermissionRequest(), () => Next(() => nextCalled = true), CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be("unauthorized.default");
    }

    [Fact]
    public async Task Handle_PermissionMissing_ReturnsForbiddenWithoutNamingThePermission()
    {
        var behavior = Behavior<SinglePermissionRequest, Result>(Caller("other"));
        var nextCalled = false;

        var result = await behavior.Handle(new SinglePermissionRequest(), () => Next(() => nextCalled = true), CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be(ErrorCodes.Forbidden.InsufficientPermission);
        result.Error.Message.Should().NotContain("orders.read");
    }

    [Fact]
    public async Task Handle_PermissionHeld_CallsNext()
    {
        var behavior = Behavior<SinglePermissionRequest, Result>(Caller("orders.read"));

        var result = await behavior.Handle(new SinglePermissionRequest(), () => Next(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    public async Task Handle_ValuesOfOneAttribute_AnyOneSuffices(string held)
    {
        var behavior = Behavior<AnyOfRequest, Result>(Caller(held));

        var result = await behavior.Handle(new AnyOfRequest(), () => Next(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValuesOfOneAttribute_NoneHeld_ReturnsForbidden()
    {
        var behavior = Behavior<AnyOfRequest, Result>(Caller("c"));

        var result = await behavior.Handle(new AnyOfRequest(), () => Next(), CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_SeveralAttributes_AllAreRequired()
    {
        var both = await Behavior<AllOfRequest, Result>(Caller("a", "b"))
            .Handle(new AllOfRequest(), () => Next(), CancellationToken.None);
        var onlyA = await Behavior<AllOfRequest, Result>(Caller("a"))
            .Handle(new AllOfRequest(), () => Next(), CancellationToken.None);
        var onlyB = await Behavior<AllOfRequest, Result>(Caller("b"))
            .Handle(new AllOfRequest(), () => Next(), CancellationToken.None);

        both.IsSuccess.Should().BeTrue();
        onlyA.Error.Type.Should().Be(ErrorType.Forbidden);
        onlyB.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Theory]
    [InlineData(true, "a", "c")]
    [InlineData(true, "b", "c")]
    [InlineData(false, "a", "b")]
    [InlineData(false, "c")]
    public async Task Handle_AnyOfWithinAndAllOfAcross_Combine(bool allowed, params string[] held)
    {
        var behavior = Behavior<MixedRequest, Result<int>>(Caller(held));

        var result = await behavior.Handle(new MixedRequest(), () => Task.FromResult(Result<int>.Success(1)), CancellationToken.None);

        result.IsSuccess.Should().Be(allowed);
        if (!allowed)
            result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_AttributeOnABaseType_AppliesToTheDerivedRequest()
    {
        var behavior = Behavior<DerivedRequest, Result>(Caller());

        var result = await behavior.Handle(new DerivedRequest(), () => Next(), CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public void RequirePermission_NoValue_Throws()
    {
        var act = () => new RequirePermissionAttribute();

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void RequirePermission_BlankValue_Throws(string? permission)
    {
        var act = () => new RequirePermissionAttribute("ok", permission!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequirePermission_KeepsItsValues()
        => new RequirePermissionAttribute("a", "b").Permissions.Should().Equal("a", "b");

    // ---- Through the registered pipeline ----

    private sealed class SinglePermissionHandler : ICommandHandler<SinglePermissionRequest>
    {
        public Task<Result> Handle(SinglePermissionRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed class UnprotectedHandler : ICommandHandler<UnprotectedRequest>
    {
        public Task<Result> Handle(UnprotectedRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    // Sent only by the test that does not scan this assembly, so this is its only handler.
    [RequirePermission("orders.read")]
    private sealed record UnscannedRequest : ICommand;

    private sealed class CountingHandler : ICommandHandler<UnscannedRequest>
    {
        public int Calls { get; private set; }

        public Task<Result> Handle(UnscannedRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result.Success());
        }
    }

    // SharedKernel.Application itself declares no [RequirePermission] request, so scanning it
    // stands for "a service whose scanned assemblies declare no permission".
    private static readonly System.Reflection.Assembly AssemblyWithoutMarkedRequests = typeof(ICommandScope).Assembly;

    private static void ValidateOnStart(IServiceProvider provider)
        => provider.GetRequiredService<IStartupValidator>().Validate();

    [Theory]
    [InlineData(false, new string[0], true, ErrorType.Unauthorized)]
    [InlineData(true, new string[0], true, ErrorType.Forbidden)]
    [InlineData(true, new[] { "orders.read" }, false, ErrorType.None)]
    public async Task Pipeline_AlwaysEnforcesTheAttribute_WithoutAnyOptIn(
        bool authenticated, string[] held, bool denied, ErrorType expected)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestContext>(new FakeRequestContext(authenticated, new HashSet<string>(held)));
        services.AddSharedKernelApplication(typeof(AuthorizationBehaviorTests).Assembly, app => app.UseMediatR());
        using var provider = services.BuildServiceProvider();
        ValidateOnStart(provider);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new SinglePermissionRequest());
        var unprotected = await sender.Send(new UnprotectedRequest());

        result.IsFailure.Should().Be(denied);
        if (denied)
            result.Error.Type.Should().Be(expected);
        unprotected.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Start_MarkedRequestScannedAndNoRequestContext_FailsNamingTheRequestTypesAndTheFix()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(typeof(AuthorizationBehaviorTests).Assembly);
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        // Ordered by name, the first three are named (DerivedRequest inherits the attribute from its
        // abstract base, which itself is not a request that can be sent); the rest are counted.
        var message = act.Should().Throw<OptionsValidationException>().Which.Message;
        message.Should().Contain(typeof(IRequestContext).FullName)
            .And.Contain("[RequirePermission] on ")
            .And.Contain(typeof(AllOfRequest).FullName)
            .And.Contain(typeof(AnyOfRequest).FullName)
            .And.Contain(typeof(DerivedRequest).FullName)
            .And.NotContain(typeof(ProtectedBase).FullName + ",")
            .And.MatchRegex(@"and \d+ more")
            .And.Contain("AddSharedKernelRequestContext()");
    }

    [Fact]
    public void Start_MarkedRequestScannedAndRequestContextRegisteredAfterTheCall_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(typeof(AuthorizationBehaviorTests).Assembly, app => app.UseMediatR());
        services.AddSingleton<IRequestContext>(AnonymousRequestContext.Instance);
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Pipeline_MarkedRequestFromAnUnscannedAssemblyAndNoRequestContext_ThrowsAndNeverRunsTheHandler()
    {
        var handler = new CountingHandler();
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(AssemblyWithoutMarkedRequests, app => app.UseMediatR());
        services.AddSingleton<IRequestHandler<UnscannedRequest, Result>>(handler);
        using var provider = services.BuildServiceProvider();

        // The scan saw no marked request, so the start passes; the send itself must still fail closed. The mediator
        // routes only the scanned assemblies, so the request is sent through its pipeline directly.
        ValidateOnStart(provider);
        var act = () => provider.GetRequiredService<RequestPipeline<UnscannedRequest, Result>>()
            .HandleAsync(new UnscannedRequest());

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should()
            .Contain(typeof(UnscannedRequest).FullName)
            .And.Contain("AddSharedKernelRequestContext()");
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Pipeline_UnmarkedRequestAndNoRequestContext_StartsAndDispatches()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(AssemblyWithoutMarkedRequests, app => app.UseMediatR());
        services.AddSingleton<IRequestHandler<UnprotectedRequest, Result>, UnprotectedHandler>();
        using var provider = services.BuildServiceProvider();

        ValidateOnStart(provider);
        var result = await provider.GetRequiredService<RequestPipeline<UnprotectedRequest, Result>>()
            .HandleAsync(new UnprotectedRequest());

        result.IsSuccess.Should().BeTrue();
    }

    // ---- Streams ----

    [RequirePermission("rows.export")]
    private sealed record ProtectedStream(int Count) : IStreamQuery<int>;

    private sealed record UnprotectedStream(int Count) : IStreamQuery<int>;

    private sealed class ProtectedStreamHandler(StreamJournal journal) : IStreamQueryHandler<ProtectedStream, int>
    {
        public IAsyncEnumerable<int> Handle(ProtectedStream request, CancellationToken cancellationToken)
        {
            journal.HandlerCalls++;
            return Rows(request.Count);
        }
    }

    private sealed class UnprotectedStreamHandler : IStreamQueryHandler<UnprotectedStream, int>
    {
        public IAsyncEnumerable<int> Handle(UnprotectedStream request, CancellationToken cancellationToken)
            => Rows(request.Count);
    }

    private sealed class StreamJournal
    {
        public int HandlerCalls { get; set; }
    }

    private static async IAsyncEnumerable<int> Rows(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }

    private static async Task<List<int>> ReadAll(IAsyncEnumerable<int> stream)
    {
        var items = new List<int>();
        await foreach (var item in stream)
            items.Add(item);
        return items;
    }

    [Fact]
    public async Task StreamBehavior_NoAttribute_PassesWithoutResolvingTheCaller()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IRequestContext>(_ => throw new InvalidOperationException("IRequestContext was resolved."))
            .BuildServiceProvider();
        var behavior = new StreamAuthorizationBehavior<UnprotectedStream, int>(provider);

        var items = await ReadAll(behavior.Handle(new UnprotectedStream(2), () => Rows(2), CancellationToken.None));

        items.Should().Equal(0, 1);
    }

    [Fact]
    public async Task StreamBehavior_NotAuthenticated_ThrowsUnauthorizedWhenReadingStarts_WithoutOpeningTheStream()
    {
        var behavior = new StreamAuthorizationBehavior<ProtectedStream, int>(
            new ServiceCollection().AddSingleton<IRequestContext>(new FakeRequestContext(isAuthenticated: false)).BuildServiceProvider());
        var opened = false;

        var stream = behavior.Handle(new ProtectedStream(2), () =>
        {
            opened = true;
            return Rows(2);
        }, CancellationToken.None);

        opened.Should().BeFalse("nothing runs until the caller starts reading");
        var act = () => ReadAll(stream);

        await act.Should().ThrowAsync<UnauthorizedException>();
        opened.Should().BeFalse();
    }

    [Fact]
    public async Task StreamBehavior_PermissionMissing_ThrowsForbidden()
    {
        var behavior = new StreamAuthorizationBehavior<ProtectedStream, int>(
            new ServiceCollection().AddSingleton<IRequestContext>(Caller("other")).BuildServiceProvider());

        var act = () => ReadAll(behavior.Handle(new ProtectedStream(2), () => Rows(2), CancellationToken.None));

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().NotContain("rows.export");
    }

    [Fact]
    public async Task StreamBehavior_PermissionHeld_YieldsTheItems()
    {
        var behavior = new StreamAuthorizationBehavior<ProtectedStream, int>(
            new ServiceCollection().AddSingleton<IRequestContext>(Caller("rows.export")).BuildServiceProvider());

        var items = await ReadAll(behavior.Handle(new ProtectedStream(3), () => Rows(3), CancellationToken.None));

        items.Should().Equal(0, 1, 2);
    }

    [Theory]
    [InlineData(false, new string[0], typeof(UnauthorizedException))]
    [InlineData(true, new string[0], typeof(ForbiddenException))]
    [InlineData(true, new[] { "rows.export" }, null)]
    public async Task Pipeline_Stream_AlwaysEnforcesTheAttribute(bool authenticated, string[] held, Type? expected)
    {
        var journal = new StreamJournal();
        var services = new ServiceCollection();
        services.AddSingleton(journal);
        services.AddSingleton<IRequestContext>(new FakeRequestContext(authenticated, new HashSet<string>(held)));
        services.AddSharedKernelApplication(typeof(AuthorizationBehaviorTests).Assembly, app => app.UseMediatR());
        using var provider = services.BuildServiceProvider();
        ValidateOnStart(provider);
        var sender = provider.GetRequiredService<ISender>();

        var act = () => ReadAll(sender.CreateStream(new ProtectedStream(2)));

        if (expected is null)
        {
            (await act()).Should().Equal(0, 1);
            journal.HandlerCalls.Should().Be(1);
        }
        else
        {
            (await act.Should().ThrowAsync<Exception>()).Which.Should().BeOfType(expected);
            journal.HandlerCalls.Should().Be(0);
        }

        (await ReadAll(sender.CreateStream(new UnprotectedStream(1)))).Should().Equal(0);
    }
}
