using OpenFeature;
using OpenFeature.Model;

namespace SharedKernel.FeatureManagement;

/// <summary>
/// Evaluates a typed <see cref="FeatureFlag{T}"/> through OpenFeature's <see cref="IFeatureClient"/>.
/// </summary>
/// <remarks>
/// These methods never throw for a missing flag, a type mismatch or a provider failure: they return the
/// flag's <see cref="FeatureFlag{T}.DefaultValue"/>, and <see cref="GetDetailsAsync{T}(IFeatureClient, FeatureFlag{T}, CancellationToken)"/>
/// reports why in <see cref="FlagEvaluationDetails{T}.ErrorType"/> and <see cref="FlagEvaluationDetails{T}.Reason"/>.
/// Pass an <see cref="EvaluationContext"/> only to target someone other than the current caller; the
/// caller's own user, tenant and groups are already applied (see <see cref="IFeatureTargetingContextAccessor"/>).
/// </remarks>
public static class FeatureClientExtensions
{
    /// <summary>Returns whether <paramref name="flag"/> is on for the current caller.</summary>
    /// <param name="client">The feature client.</param>
    /// <param name="flag">The flag to evaluate.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns><see langword="true"/> when the flag is on; the flag's default when it cannot be evaluated.</returns>
    public static async Task<bool> IsEnabledAsync(
        this IFeatureClient client,
        FeatureFlag<bool> flag,
        CancellationToken cancellationToken = default) =>
        (await GetDetailsAsync(client, flag, null, cancellationToken).ConfigureAwait(false)).Value;

    /// <summary>Returns whether <paramref name="flag"/> is on for the target described by <paramref name="context"/>.</summary>
    /// <param name="client">The feature client.</param>
    /// <param name="flag">The flag to evaluate.</param>
    /// <param name="context">Who to evaluate for. Its values replace the ambient caller's.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns><see langword="true"/> when the flag is on; the flag's default when it cannot be evaluated.</returns>
    public static async Task<bool> IsEnabledAsync(
        this IFeatureClient client,
        FeatureFlag<bool> flag,
        EvaluationContext? context,
        CancellationToken cancellationToken = default) =>
        (await GetDetailsAsync(client, flag, context, cancellationToken).ConfigureAwait(false)).Value;

    /// <summary>Returns the value of <paramref name="flag"/> for the current caller.</summary>
    /// <typeparam name="T">The flag's value type.</typeparam>
    /// <param name="client">The feature client.</param>
    /// <param name="flag">The flag to evaluate.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns>The flag's value, or its default when it cannot be evaluated.</returns>
    public static async Task<T> GetValueAsync<T>(
        this IFeatureClient client,
        FeatureFlag<T> flag,
        CancellationToken cancellationToken = default) =>
        (await GetDetailsAsync(client, flag, null, cancellationToken).ConfigureAwait(false)).Value;

    /// <summary>Returns the value of <paramref name="flag"/> for the target described by <paramref name="context"/>.</summary>
    /// <typeparam name="T">The flag's value type.</typeparam>
    /// <param name="client">The feature client.</param>
    /// <param name="flag">The flag to evaluate.</param>
    /// <param name="context">Who to evaluate for. Its values replace the ambient caller's.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns>The flag's value, or its default when it cannot be evaluated.</returns>
    public static async Task<T> GetValueAsync<T>(
        this IFeatureClient client,
        FeatureFlag<T> flag,
        EvaluationContext? context,
        CancellationToken cancellationToken = default) =>
        (await GetDetailsAsync(client, flag, context, cancellationToken).ConfigureAwait(false)).Value;

    /// <summary>
    /// Evaluates <paramref name="flag"/> for the current caller and returns the value with the assigned
    /// variant, the reason and any error.
    /// </summary>
    /// <typeparam name="T">The flag's value type.</typeparam>
    /// <param name="client">The feature client.</param>
    /// <param name="flag">The flag to evaluate.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns>The evaluation details.</returns>
    public static Task<FlagEvaluationDetails<T>> GetDetailsAsync<T>(
        this IFeatureClient client,
        FeatureFlag<T> flag,
        CancellationToken cancellationToken = default) =>
        GetDetailsAsync(client, flag, null, cancellationToken);

    /// <summary>
    /// Evaluates <paramref name="flag"/> for the target described by <paramref name="context"/> and returns
    /// the value with the assigned variant, the reason and any error.
    /// </summary>
    /// <typeparam name="T">The flag's value type.</typeparam>
    /// <param name="client">The feature client.</param>
    /// <param name="flag">The flag to evaluate.</param>
    /// <param name="context">Who to evaluate for. Its values replace the ambient caller's.</param>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns>The evaluation details.</returns>
    public static Task<FlagEvaluationDetails<T>> GetDetailsAsync<T>(
        this IFeatureClient client,
        FeatureFlag<T> flag,
        EvaluationContext? context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(flag);
        return flag.EvaluateAsync(client, context, cancellationToken);
    }
}
