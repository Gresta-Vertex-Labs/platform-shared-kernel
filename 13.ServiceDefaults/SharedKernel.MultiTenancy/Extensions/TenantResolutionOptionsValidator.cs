using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.MultiTenancy.Resolution;

namespace SharedKernel.MultiTenancy.Extensions;

/// <summary>
/// Validates <see cref="TenantResolutionOptions.StrategyOrder"/> against the real DI-registered
/// <see cref="ITenantResolutionStrategy"/> set at startup.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <see cref="MultiTenancyExtensions.AddSharedKernelMultiTenancy"/> via
/// <c>.AddOptions&lt;TenantResolutionOptions&gt;().ValidateOnStart()</c> — a misconfigured host
/// fails at <c>IHost.StartAsync()</c>, not on first request.
/// </para>
/// <para>
/// <b>Deliberate registration/resolution shape:</b> this validator is registered as a
/// <c>Singleton</c> and captures <see cref="IServiceProvider"/> rather than constructor-injecting
/// <see cref="IEnumerable{T}"/> of <see cref="ITenantResolutionStrategy"/> directly. The platform's
/// registered strategies are <c>Scoped</c> (see <see cref="MultiTenancyExtensions.AddSharedKernelMultiTenancy"/>),
/// and <c>ValidateOnStart()</c>'s validation pass runs against the ROOT service provider — a
/// Scoped constructor dependency on a Singleton validator is the same captive-dependency pitfall
/// <c>SharedKernel.ServiceDefaults.Security.MtlsClientCertificateExtensions</c> documents for its
/// own <c>IMtlsCertificateValidator</c> resolution. <see cref="Validate"/> instead
/// creates a fresh <see cref="IServiceScope"/> per validation call — the same safe pattern that
/// precedent established — so the real, currently-registered strategy set (including any custom
/// strategy a consumer adds after calling <see cref="MultiTenancyExtensions.AddSharedKernelMultiTenancy"/>)
/// is genuinely cross-checked, not a stale registration-time snapshot.
/// </para>
/// </remarks>
public sealed class TenantResolutionOptionsValidator(IServiceProvider serviceProvider)
    : IValidateOptions<TenantResolutionOptions>
{
    /// <summary>
    /// Fails when any entry of the effective strategy order (<see cref="TenantResolutionOptions.StrategyOrder"/>,
    /// or <see cref="TenantResolutionOptions.DefaultStrategyOrder"/> when it is empty) names a strategy
    /// with no matching registered <see cref="ITenantResolutionStrategy.StrategyName"/>, or appears twice.
    /// </summary>
    /// <param name="name">The named options instance being validated (unused — this options type is not named).</param>
    /// <param name="options">The <see cref="TenantResolutionOptions"/> instance to validate.</param>
    /// <returns>
    /// <see cref="ValidateOptionsResult.Success"/> when every <see cref="TenantResolutionOptions.StrategyOrder"/>
    /// entry matches a registered strategy; otherwise a <see cref="ValidateOptionsResult.Fail(string)"/>
    /// naming the offending entry/entries and the currently-registered strategy names.
    /// </returns>
    public ValidateOptionsResult Validate(string? name, TenantResolutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var strategyOrder = options.EffectiveStrategyOrder;

        var duplicates = strategyOrder
            .GroupBy(strategyName => strategyName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(TenantResolutionOptions)}.{nameof(TenantResolutionOptions.StrategyOrder)} lists " +
                $"strategy name(s) more than once: {string.Join(", ", duplicates)}.");
        }

        using var scope = serviceProvider.CreateScope();
        var registeredNames = scope.ServiceProvider
            .GetServices<ITenantResolutionStrategy>()
            .Select(s => s.StrategyName)
            .ToHashSet(StringComparer.Ordinal);

        var unmatched = strategyOrder
            .Where(strategyName => !registeredNames.Contains(strategyName))
            .ToArray();

        if (unmatched.Length > 0)
        {
            var registeredNamesDisplay = registeredNames.Count == 0
                ? "(none)"
                : string.Join(", ", registeredNames);

            return ValidateOptionsResult.Fail(
                $"{nameof(TenantResolutionOptions)}.{nameof(TenantResolutionOptions.StrategyOrder)} references " +
                $"unregistered strategy name(s): {string.Join(", ", unmatched)}. Currently registered " +
                $"ITenantResolutionStrategy.StrategyName value(s): {registeredNamesDisplay}.");
        }

        return ValidateOptionsResult.Success;
    }
}
