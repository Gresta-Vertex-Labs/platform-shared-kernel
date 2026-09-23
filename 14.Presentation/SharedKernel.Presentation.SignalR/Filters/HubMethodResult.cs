using System.Collections.Concurrent;
using System.Linq.Expressions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>Reads what a hub method returned when it is a <see cref="Result"/> or a <see cref="Result{T}"/>.</summary>
/// <remarks>
/// <see cref="Result{T}"/> exposes its value and error only on the closed type: <see cref="IHasSuccessFlag"/> carries
/// the outcome alone, and the covariant <see cref="IResultOfT{T}"/> does not match a value-type <c>T</c>. Each closed
/// type therefore gets two accessors, compiled once from expression trees and cached; nothing is invoked through
/// reflection (the platform forbids <c>MakeGenericMethod</c>).
/// </remarks>
internal static class HubMethodResult
{
    private static readonly ConcurrentDictionary<Type, Accessors> Cache = new();

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="returned"/> is a <see cref="Result"/> or a
    /// <see cref="Result{T}"/>, with its success value (always <see langword="null"/> for <see cref="Result"/>) or its
    /// error.
    /// </summary>
    /// <param name="returned">What the hub method returned, after SignalR awaited it.</param>
    /// <param name="value">The success value; <see langword="null"/> on failure.</param>
    /// <param name="error">The error of a failure; <see langword="null"/> on success.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="returned"/> is an uninitialized <c>default(Result)</c>, which is neither a success nor a failure.
    /// </exception>
    public static bool TryRead(object? returned, out object? value, out Error? error)
    {
        value = null;
        error = null;

        switch (returned)
        {
            case Result result:
                error = result.IsSuccess ? null : result.Error;
                return true;

            case IHasSuccessFlag flagged when IsResultOfT(flagged.GetType()):
                var accessors = Cache.GetOrAdd(flagged.GetType(), Accessors.Create);
                if (flagged.IsSuccess)
                {
                    value = accessors.GetValue(flagged);
                }
                else
                {
                    error = accessors.GetError(flagged);
                }

                return true;

            default:
                return false;
        }
    }

    private static bool IsResultOfT(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>);

    /// <summary>The compiled readers of one closed <see cref="Result{T}"/>.</summary>
    private sealed class Accessors
    {
        private Accessors(Func<object, object?> getValue, Func<object, Error> getError)
        {
            GetValue = getValue;
            GetError = getError;
        }

        /// <summary>Gets <c>(object)((Result&lt;T&gt;)result).Value</c>.</summary>
        public Func<object, object?> GetValue { get; }

        /// <summary>Gets <c>((Result&lt;T&gt;)result).Error</c>.</summary>
        public Func<object, Error> GetError { get; }

        public static Accessors Create(Type resultType)
        {
            var boxed = Expression.Parameter(typeof(object), "result");
            var result = Expression.Convert(boxed, resultType);

            var getValue = Expression.Lambda<Func<object, object?>>(
                    Expression.Convert(Expression.Property(result, nameof(Result<object>.Value)), typeof(object)),
                    boxed)
                .Compile();

            var getError = Expression.Lambda<Func<object, Error>>(
                    Expression.Property(result, nameof(Result<object>.Error)),
                    boxed)
                .Compile();

            return new Accessors(getValue, getError);
        }
    }
}
