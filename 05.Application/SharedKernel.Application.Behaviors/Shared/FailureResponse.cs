using System.Reflection;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Shared;

/// <summary>
/// Constructs a failed pipeline response from an <see cref="Error"/>, supporting both the
/// non-generic <see cref="Result"/> and any closed <see cref="Result{T}"/> response shape.
/// </summary>
/// <remarks>
/// Behaviors such as <c>AuthorizationBehavior</c>, <c>ValidationBehavior</c>, and
/// <c>IdempotencyBehavior</c> need to short-circuit with a failed response whose concrete shape is
/// only known through the open generic <c>TResponse</c> parameter. The non-generic
/// <see cref="Result"/> case is a direct cast; any other <c>TResponse</c> is expected to expose a
/// public static <c>Failure(Error)</c> factory method (which every <see cref="Result{T}"/> does) —
/// resolved once per closed <c>TResponse</c> type via reflection and bound to a delegate via
/// <see cref="MethodInfo.CreateDelegate(Type)"/>, cached in a generic nested class so every
/// subsequent call is a direct delegate invocation, never a per-call <c>MethodBase.Invoke</c>.
/// </remarks>
internal static class FailureResponse
{
    /// <summary>Creates a failed <typeparamref name="TResponse"/> from <paramref name="error"/>.</summary>
    /// <typeparam name="TResponse">
    /// Either <see cref="Result"/> or a closed <see cref="Result{T}"/> (or any other type exposing a
    /// public static <c>Failure(Error)</c> factory returning that same type).
    /// </typeparam>
    /// <param name="error">The error describing the failure.</param>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TResponse"/> is not <see cref="Result"/> and exposes no public static
    /// <c>Failure(Error)</c> factory method returning <typeparamref name="TResponse"/>.
    /// </exception>
    internal static TResponse Create<TResponse>(Error error)
    {
        // Fast path for the non-generic Result struct — zero reflection, a straight cast.
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        return Cache<TResponse>.Factory(error);
    }

    private static class Cache<TResponse>
    {
        internal static readonly Func<Error, TResponse> Factory = Build();

        private static Func<Error, TResponse> Build()
        {
            var method = typeof(TResponse).GetMethod(
                "Failure",
                BindingFlags.Public | BindingFlags.Static,
                [typeof(Error)]);

            if (method is null || method.ReturnType != typeof(TResponse))
            {
                throw new InvalidOperationException(
                    $"Cannot construct a failure response of type '{typeof(TResponse).FullName}'. " +
                    "The request type's response must be Result or Result<T> — a type exposing a " +
                    "public static Failure(Error) factory method returning that same type.");
            }

            return (Func<Error, TResponse>)method.CreateDelegate(typeof(Func<Error, TResponse>));
        }
    }
}
