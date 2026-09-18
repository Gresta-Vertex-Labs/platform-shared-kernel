namespace SharedKernel.Application.Behaviors.Caching;

/// <summary>
/// Carries the outcome of one cache execution back out of the type-erased
/// <see cref="ICacheableQuery"/> dispatch, which returns only the response.
/// </summary>
/// <remarks>
/// A mutable holder rather than a tuple return because the dispatch crosses a generic interface
/// member whose return type is fixed to <c>TResponse</c>. One instance per request, never shared.
/// </remarks>
internal sealed class CacheExecution
{
    /// <summary>Gets or sets what the behavior did with this execution.</summary>
    internal CacheOutcome Outcome { get; set; } = CacheOutcome.Hit;
}
