using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Application.Tests.Support;
using SharedKernel.Application.Transactions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Tests.Extensions;

public sealed class ApplicationPipelineBuilderTests
{
    private static readonly System.Reflection.Assembly TestAssembly = typeof(ApplicationPipelineBuilderTests).Assembly;

    private sealed record TestCommand : ICommand;

    private sealed class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private static void ValidateOnStart(IServiceProvider provider)
        => provider.GetRequiredService<IStartupValidator>().Validate();

    // ---- Seams: checked at host start, not at registration ----

    [Fact]
    public void AddSharedKernelApplication_SeamsMissing_DoesNotThrowAtRegistration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplication(TestAssembly, app => app
            .WithIdempotency()
            .WithTransactions()
            .WithAuditing());

        act.Should().NotThrow();
    }

    [Fact]
    public void Start_SeamsMissing_FailsWithOneMessageNamingEveryMissingService()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(TestAssembly, app => app
            .WithIdempotency()
            .WithTransactions()
            .WithAuditing());
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        var message = act.Should().Throw<OptionsValidationException>().Which.Message;
        message.Should().Contain(typeof(IRequestContext).FullName)
            .And.Contain(typeof(IRequestIdempotencyStore).FullName)
            .And.Contain(typeof(IUnitOfWork).FullName)
            .And.Contain(typeof(IAuditTrailWriter).FullName)
            .And.Contain("[RequirePermission] on ")
            .And.Contain("WithIdempotency()")
            .And.Contain("WithTransactions()")
            .And.Contain("WithAuditing()");
    }

    [Fact]
    public void Start_OnlyOneSeamMissing_NamesOnlyThatSeam()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestContext>(AnonymousRequestContext.Instance);
        services.AddSharedKernelApplication(TestAssembly, app => app.WithTransactions());
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        var message = act.Should().Throw<OptionsValidationException>().Which.Message;
        message.Should().Contain(typeof(IUnitOfWork).FullName).And.NotContain(typeof(IRequestContext).FullName);
    }

    [Fact]
    public void Start_SeamsRegisteredAfterTheCall_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(TestAssembly, app => app
            .WithIdempotency()
            .WithTransactions()
            .WithAuditing());

        // Registered after AddSharedKernelApplication — registration order must not matter.
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
        services.AddSingleton<IRequestContext>(new FakeRequestContext(true, new HashSet<string> { "p" }));
        services.AddSingleton<IRequestIdempotencyStore>(new FakeIdempotencyStore());
        services.AddSingleton<IAuditTrailWriter>(new FakeAuditTrailWriter());
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void Start_SeamsRegisteredBeforeTheCall_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
        services.AddSingleton<IRequestContext>(AnonymousRequestContext.Instance);
        services.AddSingleton<IRequestIdempotencyStore>(new FakeIdempotencyStore());
        services.AddSingleton<IAuditTrailWriter>(new FakeAuditTrailWriter());
        services.AddSharedKernelApplication(TestAssembly, app => app
            .WithIdempotency()
            .WithTransactions()
            .WithAuditing());
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Reservations are scoped per tenant and caller, so the behavior cannot run without knowing who is calling —
    /// falling back to "everyone is anonymous" would silently put every caller back into one shared scope.
    /// </summary>
    [Fact]
    public void Start_IdempotencyWithStoreButWithoutIRequestContext_Fails()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestIdempotencyStore>(new FakeIdempotencyStore());
        services.AddSharedKernelApplication(TestAssembly, app => app.WithIdempotency());
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{typeof(IRequestContext).FullName}*WithIdempotency()*");
    }

    [Fact]
    public void Start_NoOptInBehaviors_Succeeds()
    {
        var services = new ServiceCollection();
        // This assembly declares [RequirePermission] requests, which always need a caller identity.
        services.AddSingleton<IRequestContext>(AnonymousRequestContext.Instance);
        services.AddSharedKernelApplication(TestAssembly);
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        act.Should().NotThrow();
    }

    // ---- The call itself ----

    [Fact]
    public void AddSharedKernelApplication_CalledTwice_Throws()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(TestAssembly);

        var act = () => services.AddSharedKernelApplication(TestAssembly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been called*Call it once*");
    }

    [Fact]
    public void AddSharedKernelApplication_NoAssembly_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplication();

        act.Should().Throw<ArgumentException>().WithParameterName("assemblies");
    }

    [Fact]
    public void AddSharedKernelApplication_AlwaysRegistersTheCoreServicesAndBehaviors()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(TestAssembly);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<ISender>().Should().NotBeNull();
        scope.ServiceProvider.GetService<ICommandScope>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IDomainEventDispatcher>().Should().BeOfType<MediatRDomainEventDispatcher>();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<TestCommand, Result>>()
            .Select(b => b.GetType().GetGenericTypeDefinition())
            .ToList();

        behaviors.Should().Equal(
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(MetricsBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(ValidationBehavior<,>));
    }

    [Fact]
    public async Task AddSharedKernelApplication_RegistersTheHandlersOfTheAssembly()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(TestAssembly);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ISender>().Send(new TestCommand());

        result.IsSuccess.Should().BeTrue();
    }

    // ---- Validators are discovered from the same assemblies ----

    public sealed record RenameCustomer(string Name) : ICommand;

    private sealed class RenameCustomerHandler : ICommandHandler<RenameCustomer>
    {
        public Task<Result> Handle(RenameCustomer request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    public sealed class RenameCustomerValidator : AbstractValidator<RenameCustomer>
    {
        public RenameCustomerValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    internal sealed record CloseAccount(string Reason) : ICommand;

    private sealed class CloseAccountHandler : ICommandHandler<CloseAccount>
    {
        public Task<Result> Handle(CloseAccount request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    internal sealed class CloseAccountValidator : AbstractValidator<CloseAccount>
    {
        public CloseAccountValidator() => RuleFor(x => x.Reason).NotEmpty();
    }

    [Fact]
    public async Task AddSharedKernelApplication_DiscoversPublicValidatorsOfTheAssembly()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(TestAssembly);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IValidator<RenameCustomer>>().Should().ContainSingle()
            .Which.Should().BeOfType<RenameCustomerValidator>();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RenameCustomer(""));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task AddSharedKernelApplication_DiscoversInternalValidatorsOfTheAssembly()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(TestAssembly);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CloseAccount(" "));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    // ---- WithBehavior ----

    private sealed class NotAnOpenGeneric : IPipelineBehavior<TestCommand, Result>
    {
        public Task<Result> Handle(TestCommand request, RequestHandlerDelegate<Result> next, CancellationToken ct) => next();
    }

    private sealed class NotAPipelineBehavior<TRequest, TResponse>;

    [Fact]
    public void WithBehavior_UndefinedPipelineStage_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplication(
            TestAssembly,
            app => app.WithBehavior(typeof(FirstMarker<,>), (PipelineStage)99));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("stage");
    }

    [Fact]
    public void WithBehavior_ClosedGenericType_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplication(
            TestAssembly,
            app => app.WithBehavior(typeof(NotAnOpenGeneric), PipelineStage.Command));

        act.Should().Throw<ArgumentException>().WithMessage("*open generic*");
    }

    [Fact]
    public void WithBehavior_NotImplementingIPipelineBehavior_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplication(
            TestAssembly,
            app => app.WithBehavior(typeof(NotAPipelineBehavior<,>), PipelineStage.Command));

        act.Should().Throw<ArgumentException>().WithMessage("*IPipelineBehavior*");
    }

    private sealed class RequiresMarkerServiceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        // Constructor dependency only — proves DI can satisfy the required-service declaration;
        // the behavior itself has no other use for it.
        public RequiresMarkerServiceBehavior(MarkerService marker) => _ = marker;

        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct) => next();
    }

    private sealed class MarkerService;

    [Fact]
    public void Start_WithBehaviorMissingRequiredService_FailsNamingTheServiceAndTheBehavior()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(
            TestAssembly,
            app => app.WithBehavior(typeof(RequiresMarkerServiceBehavior<,>), PipelineStage.Command, typeof(MarkerService)));
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*MarkerService*WithBehavior(typeof(RequiresMarkerServiceBehavior<,>))*");
    }

    [Fact]
    public void Start_WithBehaviorRequiredServiceRegisteredAfterTheCall_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(
            TestAssembly,
            app => app.WithBehavior(typeof(RequiresMarkerServiceBehavior<,>), PipelineStage.Command, typeof(MarkerService)));
        services.AddSingleton<MarkerService>();
        services.AddSingleton<IRequestContext>(AnonymousRequestContext.Instance);
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void WithBehavior_SameTypeTwice_IsRegisteredOnce()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new List<string>());
        services.AddSharedKernelApplication(TestAssembly, app => app
            .WithBehavior(typeof(FirstMarker<,>), PipelineStage.Query)
            .WithBehavior(typeof(FirstMarker<,>), PipelineStage.Query));
        using var provider = services.BuildServiceProvider();

        var behaviors = provider.GetServices<IPipelineBehavior<TestCommand, Result>>().ToList();

        behaviors.Count(b => b.GetType().Name.StartsWith("FirstMarker", StringComparison.Ordinal)).Should().Be(1);
    }

    // ---- Multiple custom behaviors in the same stage run in the order they were added ----

    private class OrderedMarker<TRequest, TResponse>(List<string> sequence, string name) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            sequence.Add($"{name}.pre");
            var response = await next().ConfigureAwait(false);
            sequence.Add($"{name}.post");
            return response;
        }
    }

    private sealed class FirstMarker<TRequest, TResponse>(List<string> sequence) : OrderedMarker<TRequest, TResponse>(sequence, "first")
        where TRequest : notnull;

    private sealed class SecondMarker<TRequest, TResponse>(List<string> sequence) : OrderedMarker<TRequest, TResponse>(sequence, "second")
        where TRequest : notnull;

    private sealed record OrderedCommand : ICommand;

    private sealed class OrderedMarkerHandler(List<string> sequence) : ICommandHandler<OrderedCommand>
    {
        public Task<Result> Handle(OrderedCommand request, CancellationToken cancellationToken)
        {
            sequence.Add("handler");
            return Task.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task WithBehavior_TwoCustomBehaviorsInSameStage_RunInAdditionOrder()
    {
        var sequence = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(sequence);
        services.AddSharedKernelApplication(TestAssembly, app => app
            .WithBehavior(typeof(FirstMarker<,>), PipelineStage.Query)
            .WithBehavior(typeof(SecondMarker<,>), PipelineStage.Query));

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new OrderedCommand());

        sequence.Should().Equal("first.pre", "second.pre", "handler", "second.post", "first.post");
    }
}
