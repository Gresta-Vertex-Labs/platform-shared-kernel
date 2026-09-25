using System.Reflection;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Single, shared classification of a pipeline response's success/failure outcome, consumed by
/// <c>TracingBehavior</c>, <c>LoggingBehavior</c>, and <c>MetricsBehavior</c> so their outcome
/// taxonomies can never silently diverge.
/// </summary>
/// <remarks>
/// A response is classified as a failure only when it implements <see cref="IHasSuccessFlag"/> and
/// <see cref="IHasSuccessFlag.IsSuccess"/> is <see langword="false"/>. A response that does not
/// participate in the <c>Result</c> railway at all is treated as a success — there is no failure
/// signal to observe.
/// </remarks>
internal static class ResponseOutcome
{
    /// <summary>The <c>outcome</c> tag/log-branch value for a successful response.</summary>
    internal const string Success = "success";

    /// <summary>The <c>outcome</c> tag/log-branch value for a <c>Result.Failure</c>/<c>Result&lt;T&gt;.Failure</c> response.</summary>
    internal const string Failure = "failure";

    /// <summary>The <c>outcome</c> tag value recorded when the inner pipeline throws before producing a response.</summary>
    internal const string Exception = "exception";

    /// <summary>
    /// Determines whether <paramref name="response"/> represents a successful outcome.
    /// </summary>
    internal static bool IsSuccess<TResponse>(TResponse response)
        => response is not IHasSuccessFlag flag || flag.IsSuccess;

    /// <summary>
    /// Classifies <paramref name="response"/> as <see cref="Success"/> or <see cref="Failure"/>.
    /// </summary>
    internal static string Classify<TResponse>(TResponse response)
        => IsSuccess(response) ? Success : Failure;

    /// <summary>
    /// Reads the <see cref="Error"/> carried by a failed <paramref name="response"/>, or
    /// <see langword="null"/> when <paramref name="response"/> represents a success or does not
    /// participate in the <c>Result</c> railway at all.
    /// </summary>
    /// <remarks>
    /// The non-generic <see cref="Result"/> fast path is a direct pattern match. A closed
    /// <c>Result&lt;T&gt;</c> is read through a per-closed-<typeparamref name="TResponse"/> cached
    /// reflection-bound delegate over its public <c>Error</c> property getter, built once and
    /// reused for every subsequent call — the same "compile once per closed type" discipline used
    /// throughout this domain for a value only reachable at runtime through an open generic
    /// parameter.
    /// </remarks>
    internal static Error? TryGetError<TResponse>(TResponse response)
    {
        if (response is Result result)
            return result.IsFailure ? result.Error : null;

        if (response is IHasSuccessFlag { IsSuccess: false })
            return ErrorReader<TResponse>.Read(response);

        return null;
    }

    private static class ErrorReader<TResponse>
    {
        private static readonly Func<TResponse, Error>? Getter = BuildGetter();

        internal static Error? Read(TResponse response) => Getter is null ? null : Getter(response);

        private static Func<TResponse, Error>? BuildGetter()
        {
            var property = typeof(TResponse).GetProperty("Error", typeof(Error));
            if (property?.GetGetMethod() is not { } getMethod)
                return null;

            return (Func<TResponse, Error>)getMethod.CreateDelegate(typeof(Func<TResponse, Error>));
        }
    }
}
