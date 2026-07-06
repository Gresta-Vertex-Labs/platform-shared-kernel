using System.Reflection;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Shared;

/// <summary>
/// Constructs a failed pipeline response from an <see cref="Error"/>, supporting both the
/// non-generic <see cref="Result"/> and the generic <see cref="Result{T}"/> response shapes.
/// </summary>
/// <remarks>
/// <para>
/// Behaviors such as <c>AuthorizationBehavior&lt;TRequest,TResponse&gt;</c> and
/// <c>IdempotentCommandBehavior&lt;TRequest,TResponse&gt;</c> need to short-circuit with a failed
/// response whose concrete shape (<see cref="Result"/> or a closed <see cref="Result{T}"/>) is
/// only known through the open generic <c>TResponse</c> parameter.
/// </para>
/// <para>
/// <b>WO-039, P-237 rework:</b> <c>01.Core</c>'s <see cref="IFailureFactory{TSelf}"/> (a
/// self-referential/CRTP interface exposing <c>static abstract TSelf Failure(Error)</c>) replaces
/// the prior <see cref="IResultOfT{T}"/>-based approach. Because <see cref="IFailureFactory{TSelf}"/>
/// is parameterized directly on <c>TResponse</c> itself (never an inner value type), <c>BuildFactory</c>
/// no longer performs <c>Type.GetInterfaces()</c>, <c>Type.GetGenericArguments()</c>, or
/// <c>Type.MakeGenericType()</c> — there is no inner type to locate or a <c>Result&lt;T&gt;</c> to
/// reconstruct; <c>TResponse</c> already <em>is</em> the closed type to invoke.
/// </para>
/// <para>
/// <b>The remaining, irreducible constraint problem:</b> a C# static abstract interface member can
/// only be invoked through a generic type parameter itself constrained to that interface
/// (<c>where TSelf : IFailureFactory&lt;TSelf&gt;</c>) at the invoking method. <c>Create&lt;TResponse&gt;</c>
/// is called by <c>AuthorizationBehavior</c>/<c>IdempotentCommandBehavior</c> with <c>TResponse</c>
/// wholly unconstrained (it may be the non-generic <see cref="Result"/>, which deliberately does
/// <em>not</em> implement <see cref="IFailureFactory{TSelf}"/>) — so the constraint cannot be
/// propagated to this call site without either reflection or changing those two behaviors' own
/// generic shape (explicitly out of scope for this fix; see <c>05.Application/CLAUDE.md</c>).
/// <see cref="ResultOfTDispatcher{TResponse}"/> resolves this the same way
/// <c>MediatRDomainEventDispatcher</c> resolves the structurally identical
/// "publish/construct-by-runtime-type through a generic API" problem: a single
/// <c>MethodInfo.MakeGenericMethod</c> call, performed <em>once</em> per closed <c>TResponse</c> type
/// (cached forever after in this class's own static field — the CLR's per-closed-generic-type
/// instantiation guarantee, not a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>),
/// bound to a real delegate via <see cref="MethodInfo.CreateDelegate(Type)"/> so every call thereafter
/// is a direct delegate invocation — never a per-call <c>MethodBase.Invoke(object?, object?[]?)</c>.
/// The <c>MethodInfo</c> for the constrained bridge method is itself captured at type-load time via a
/// typed delegate instantiation (mirroring <c>MediatRDomainEventDispatcher</c>'s <c>BuildPublisherMethod</c>
/// capture, WO-038 P-231) rather than a string-based <c>Type.GetMethod(name)</c> lookup.
/// </para>
/// <para>
/// The <c>TResponse == typeof(<see cref="Result"/>)</c> fast path remains a zero-reflection direct
/// cast, unchanged.
/// </para>
/// </remarks>
internal static class FailureResponseFactory
{
    /// <summary>Creates a failed <typeparamref name="TResponse"/> from <paramref name="error"/>.</summary>
    /// <typeparam name="TResponse">
    /// Either <see cref="Result"/> or a closed <see cref="Result{T}"/>.
    /// </typeparam>
    /// <param name="error">The error describing the failure.</param>
    public static TResponse Create<TResponse>(Error error)
    {
        // Fast path for the non-generic Result struct — zero reflection, a straight cast.
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        // For Result<T>, TResponse implements IFailureFactory<TResponse> directly (CRTP) —
        // dispatch through the per-Type-cached delegate below.
        return ResultOfTDispatcher<TResponse>.Create(error);
    }
}

/// <summary>
/// Cached per-<typeparamref name="TResponse"/> delegate that invokes <c>TResponse</c>'s own
/// <see cref="IFailureFactory{TSelf}"/>-satisfying <c>Failure(Error)</c> static factory. Built once
/// per concrete <typeparamref name="TResponse"/> type via the CLR's generic-class instantiation
/// mechanism — a single static field, not a dictionary.
/// </summary>
/// <typeparam name="TResponse">A closed <see cref="Result{T}"/> type (always a reference type in practice).</typeparam>
internal static class ResultOfTDispatcher<TResponse>
{
    // Compile-time captured MethodInfo for the constrained InvokeFailure<TSelf> bridge below —
    // extracted from a typed delegate instantiation over a private witness type that trivially
    // satisfies IFailureFactory<TSelf>, exactly mirroring MediatRDomainEventDispatcher's
    // BuildPublisherMethod capture (WO-038, P-231): no string-based Type.GetMethod lookup, verified
    // at type-load time rather than deferred to first dispatch.
    // NOTE: declared BEFORE Factory below — static field initializers run in declaration order
    // within the type's static constructor, and Factory's initializer (BuildFactory()) depends on
    // this field already being assigned.
    private static readonly MethodInfo InvokeFailureMethod =
        ((Func<Error, FailureFactoryWitness>)InvokeFailure<FailureFactoryWitness>)
            .Method.GetGenericMethodDefinition();

    // Delegate cached once per TResponse via a static field in a generic class — the CLR
    // guarantees one static field instance per closed generic type, which is exactly the
    // "build once per concrete Type" invariant the platform requires for per-type caches. This is
    // NOT itself a reflective operation — it is ordinary CLR generic-type-instantiation semantics,
    // identical to how any other generic static field (e.g. a cached delegate, a compiled regex) is
    // scoped per closed type.
    private static readonly Func<Error, TResponse> Factory = BuildFactory();

    internal static TResponse Create(Error error) => Factory(error);

    private static Func<Error, TResponse> BuildFactory()
    {
        // TResponse implements IFailureFactory<TResponse> directly at runtime whenever this class
        // is actually instantiated over a real Result<T> (the platform's ICommand<T>/IQuery<T>
        // convention guarantees this). No GetInterfaces()/GetGenericArguments()/MakeGenericType()
        // are needed — TResponse itself is already the closed type to invoke; only the constrained
        // generic method bridge below needs to be closed over it, once, via MakeGenericMethod.
        MethodInfo closedMethod;
        try
        {
            closedMethod = InvokeFailureMethod.MakeGenericMethod(typeof(TResponse));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"FailureResponseFactory cannot construct a failure response of type '{typeof(TResponse).FullName}'. " +
                $"TResponse must implement IFailureFactory<TResponse> (i.e., be a closed Result<T>).",
                ex);
        }

        // CreateDelegate binds directly to the closed static method — every subsequent call goes
        // through this delegate, never through MethodBase.Invoke().
        return (Func<Error, TResponse>)closedMethod.CreateDelegate(typeof(Func<Error, TResponse>));
    }

    // The one generic bridge method in this domain (besides MediatRDomainEventDispatcher's
    // Publish<T>) that reaches a static-abstract-interface-member dispatch through a runtime-only-
    // known closed type. Once closed over the real TResponse (via MakeGenericMethod, cached above),
    // this body itself performs zero reflection — TSelf.Failure(error) is a direct static call.
    private static TSelf InvokeFailure<TSelf>(Error error)
        where TSelf : IFailureFactory<TSelf>
        => TSelf.Failure(error);

    // Private witness type solely to obtain a compile-time-valid closed instantiation of
    // InvokeFailure<TSelf> so its MethodInfo can be captured without a string lookup. Never
    // constructed for any real dispatch — TResponse is always the actual closed Result<T> at runtime.
    private sealed class FailureFactoryWitness : IFailureFactory<FailureFactoryWitness>
    {
        public static FailureFactoryWitness Failure(Error error) => new();
    }
}
