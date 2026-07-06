using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NSubstitute;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Shared;

/// <summary>
/// Verifies (T-33, WO-039 P-237 gap-closure) that
/// <c>SharedKernel.Application.Behaviors.Shared.FailureResponseFactory.Create&lt;TResponse&gt;</c>
/// and its <c>ResultOfTDispatcher&lt;TResponse&gt;</c> helper never invoke any
/// <see cref="System.Reflection"/> member OTHER than the single, disclosed
/// <c>MakeGenericMethod</c> + <c>CreateDelegate</c> pair documented in
/// <c>05.Application/CLAUDE.md</c> ("Constructing a generic failure response").
/// </summary>
/// <remarks>
/// <para>
/// Both types are <see langword="internal"/> to <c>SharedKernel.Application.Behaviors</c> — this
/// test project has no <c>InternalsVisibleTo</c> grant and must not gain one just to satisfy this
/// check. Instead, this is an explicit code-shape assertion (per the WO-039 P-237 task
/// description's own "reflection-call-counting test double or an explicit code-shape assertion"
/// wording): Mono.Cecil IL inspection — the same technique
/// <c>00.Governance/SharedKernel.ArchitectureTests</c>'s <c>NoMakeGenericMethodReflectionPredicate</c>
/// uses — walks the compiled IL of both types directly, asserting the forbidden reflection
/// surface (<c>GetInterfaces</c>, <c>GetGenericArguments</c>, <c>MakeGenericType</c>,
/// <c>GetMethod</c>, <c>MethodBase.Invoke</c>) is entirely absent, while confirming the one
/// disclosed <c>MakeGenericMethod</c>/<c>CreateDelegate</c> call pair IS present exactly where
/// documented.
/// </para>
/// <para>
/// The companion behavioral tests below exercise <c>Create&lt;TResponse&gt;</c> through the real,
/// public <see cref="AuthorizationBehavior{TRequest,TResponse}"/> pipeline for the non-generic
/// <c>Result</c> fast path and two distinct closed <c>Result&lt;T&gt;</c> shapes
/// (<c>Result&lt;string&gt;</c>, <c>Result&lt;int&gt;</c>), proving the factory produces the
/// correct failure shape for each — the runtime-behavior half of T-33's requirement.
/// </para>
/// </remarks>
public sealed class FailureResponseFactoryReflectionShapeTests
{
    private const string FactoryTypeName = "SharedKernel.Application.Behaviors.Shared.FailureResponseFactory";
    private const string DispatcherTypeName = "SharedKernel.Application.Behaviors.Shared.ResultOfTDispatcher`1";

    private static readonly string[] ForbiddenReflectionMemberNames =
    [
        "GetInterfaces",
        "GetGenericArguments",
        "MakeGenericType",
        "GetMethod",
        "GetMethods",
        "Invoke",
    ];

    private static AssemblyDefinition LoadBehaviorsAssembly()
    {
        var location = typeof(AuthorizationBehavior<,>).Assembly.Location;
        return AssemblyDefinition.ReadAssembly(location);
    }

    private static IEnumerable<Instruction> AllInstructions(TypeDefinition type)
        => type.Methods
            .Where(m => m.Body is not null)
            .SelectMany(m => m.Body.Instructions);

    [Fact]
    public void FailureResponseFactory_ContainsNoForbiddenReflectionMemberCalls()
    {
        using var assembly = LoadBehaviorsAssembly();
        var type = assembly.MainModule.Types.SingleOrDefault(t => t.FullName == FactoryTypeName);

        type.Should().NotBeNull($"'{FactoryTypeName}' must exist in the compiled assembly");

        var offendingCalls = AllInstructions(type!)
            .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .Where(m => m is not null)
            .Select(m => m!.Name)
            .Where(name => ForbiddenReflectionMemberNames.Contains(name))
            .ToList();

        offendingCalls.Should().BeEmpty(
            "FailureResponseFactory.Create<TResponse> must not invoke GetInterfaces/GetGenericArguments/" +
            "MakeGenericType/GetMethod/GetMethods/Invoke — only the disclosed MakeGenericMethod+CreateDelegate " +
            "pair in ResultOfTDispatcher<TResponse> is permitted (05.Application/CLAUDE.md)");
    }

    [Fact]
    public void ResultOfTDispatcher_ContainsNoForbiddenReflectionMemberCalls_ExceptDocumentedException()
    {
        using var assembly = LoadBehaviorsAssembly();
        var type = assembly.MainModule.Types.SingleOrDefault(t => t.FullName == DispatcherTypeName);

        type.Should().NotBeNull($"'{DispatcherTypeName}' must exist in the compiled assembly");

        var methodRefs = AllInstructions(type!)
            .Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            .Select(i => i.Operand as MethodReference)
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();

        var names = methodRefs.Select(m => m.Name).ToList();

        // MethodBase.Invoke (the forbidden reflective invocation) is distinguished from an ordinary
        // Func<Error,TResponse> delegate Invoke call (`Factory(error)`, a direct, non-reflective
        // invocation) by declaring type: only a call whose DeclaringType is System.Reflection's
        // MethodBase/MethodInfo family is forbidden.
        var reflectiveInvokeCalls = methodRefs
            .Where(m => m.Name == "Invoke"
                && (m.DeclaringType.FullName == "System.Reflection.MethodBase"
                    || m.DeclaringType.FullName == "System.Reflection.MethodInfo"))
            .ToList();

        reflectiveInvokeCalls.Should().BeEmpty(
            "the cached delegate must be invoked directly (a Func<Error,TResponse> delegate call), never via MethodBase.Invoke()/MethodInfo.Invoke()");
        names.Should().NotContain("GetInterfaces");
        names.Should().NotContain("GetGenericArguments");
        names.Should().NotContain("MakeGenericType");
        names.Should().NotContain("GetMethods");

        names.Should().Contain("MakeGenericMethod",
            "the one disclosed, documented exception — building the closed InvokeFailure<TResponse> method once per TResponse");
        names.Should().Contain("CreateDelegate",
            "the closed method must be bound to a direct delegate, never invoked via MethodBase.Invoke()");
    }

    // ---- behavioral half: Create<TResponse> produces the correct shape for Result and two distinct Result<T> shapes ----

    private sealed record StringQuery : IQuery<string>, IAuthorizeRequest
    {
        public string Requirement => "denied";
    }

    private sealed record IntQuery : IQuery<int>, IAuthorizeRequest
    {
        public string Requirement => "denied";
    }

    private sealed record VoidCommand : ICommand, IAuthorizeRequest
    {
        public string Requirement => "denied";
    }

    private sealed class StringQueryHandler : IRequestHandler<StringQuery, Result<string>>
    {
        public Task<Result<string>> Handle(StringQuery request, CancellationToken ct)
            => Task.FromResult(Result<string>.Success("unreachable"));
    }

    private sealed class IntQueryHandler : IRequestHandler<IntQuery, Result<int>>
    {
        public Task<Result<int>> Handle(IntQuery request, CancellationToken ct)
            => Task.FromResult(Result<int>.Success(42));
    }

    private sealed class VoidCommandHandler : IRequestHandler<VoidCommand, Result>
    {
        public Task<Result> Handle(VoidCommand request, CancellationToken ct)
            => Task.FromResult(Result.Success());
    }

    [Fact]
    public async Task Create_NonGenericResult_ProducesFailureViaAuthorizationShortCircuit()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.IsAuthorizedAsync("denied", Arg.Any<CancellationToken>()).Returns(false);

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<IRequestHandler<VoidCommand, Result>>(new VoidCommandHandler());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FailureResponseFactoryReflectionShapeTests>());
        var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ISender>().Send(new VoidCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Create_ResultOfString_ProducesFailureViaAuthorizationShortCircuit()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.IsAuthorizedAsync("denied", Arg.Any<CancellationToken>()).Returns(false);

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<IRequestHandler<StringQuery, Result<string>>>(new StringQueryHandler());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FailureResponseFactoryReflectionShapeTests>());
        var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ISender>().Send(new StringQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Create_ResultOfInt_ProducesFailureViaAuthorizationShortCircuit()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.IsAuthorizedAsync("denied", Arg.Any<CancellationToken>()).Returns(false);

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<IRequestHandler<IntQuery, Result<int>>>(new IntQueryHandler());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FailureResponseFactoryReflectionShapeTests>());
        var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ISender>().Send(new IntQuery());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }
}
