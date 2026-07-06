using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Shared;

/// <summary>
/// Single, shared classification of a pipeline response's success/failure outcome, consumed by
/// both <see cref="Logging.LoggingBehavior{TRequest,TResponse}"/> and
/// <see cref="Metrics.MetricsBehavior{TRequest,TResponse}"/> so their outcome taxonomies can never
/// silently diverge (WO-039, P-239).
/// </summary>
/// <remarks>
/// A response is classified as a failure only when it implements <see cref="IHasSuccessFlag"/> and
/// <see cref="IHasSuccessFlag.IsSuccess"/> is <see langword="false"/>. A response that does not
/// participate in the <c>Result</c> railway at all (e.g. a raw <c>TResponse</c> from a streaming
/// handler, which never reaches this class since streaming has its own behaviors) is treated as a
/// success — there is no failure signal to observe.
/// </remarks>
internal static class ResponseOutcomeClassifier
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
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="response">The response instance to classify.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="response"/> does not implement
    /// <see cref="IHasSuccessFlag"/>, or does and <see cref="IHasSuccessFlag.IsSuccess"/> is
    /// <see langword="true"/>; otherwise <see langword="false"/>.
    /// </returns>
    internal static bool IsSuccess<TResponse>(TResponse response)
        => response is not IHasSuccessFlag flag || flag.IsSuccess;

    /// <summary>
    /// Classifies <paramref name="response"/> as <see cref="Success"/> or <see cref="Failure"/>.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="response">The response instance to classify.</param>
    internal static string Classify<TResponse>(TResponse response)
        => IsSuccess(response) ? Success : Failure;
}
