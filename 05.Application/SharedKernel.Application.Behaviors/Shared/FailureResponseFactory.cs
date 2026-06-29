using System.Collections.Concurrent;
using System.Linq.Expressions;
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
/// only known through the open generic <c>TResponse</c> parameter — there is no shared interface
/// linking the two types, and this package must not modify <c>SharedKernel.Primitives</c> to add
/// one.
/// </para>
/// <para>
/// <b>Why a cached compiled delegate, not <see langword="dynamic"/> or
/// <see cref="System.Reflection.MethodInfo.MakeGenericMethod(System.Type[])"/>:</b> both
/// <see cref="Result"/> and <see cref="Result{T}"/> declare a
/// <c>public static implicit operator</c> from <see cref="Error"/>. For each distinct closed
/// <c>TResponse</c> seen at runtime, a small <see cref="Expression"/> tree that performs the
/// implicit conversion is built once and compiled into a <see cref="Func{Error,Object}"/>, then
/// cached in a static <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by <see cref="Type"/>
/// — mirroring the exact caching shape already approved for
/// <c>MediatRDomainEventDispatcher</c>'s per-event-type dispatch, except built via
/// <see cref="Expression"/> compilation rather than <c>MakeGenericMethod</c>. This avoids both the
/// platform-wide <c>MakeGenericMethod</c>/reflection prohibition and the AOT/trim risk of
/// <see langword="dynamic"/> (which would also require a <c>Microsoft.CSharp</c> package
/// reference this domain has no other reason to carry).
/// </para>
/// </remarks>
internal static class FailureResponseFactory
{
    private static readonly ConcurrentDictionary<Type, Func<Error, object>> Factories = new();

    /// <summary>Creates a failed <typeparamref name="TResponse"/> from <paramref name="error"/>.</summary>
    /// <typeparam name="TResponse">
    /// Either <see cref="Result"/> or a closed <see cref="Result{T}"/>.
    /// </typeparam>
    /// <param name="error">The error describing the failure.</param>
    public static TResponse Create<TResponse>(Error error)
    {
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        Func<Error, object> factory = Factories.GetOrAdd(typeof(TResponse), BuildFactory);
        return (TResponse)factory(error);
    }

    private static Func<Error, object> BuildFactory(Type responseType)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(Error), "error");
        UnaryExpression converted = Expression.Convert(parameter, responseType);
        UnaryExpression boxed = Expression.Convert(converted, typeof(object));
        return Expression.Lambda<Func<Error, object>>(boxed, parameter).Compile();
    }
}
