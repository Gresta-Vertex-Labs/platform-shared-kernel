using OpenFeature.Model;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.FeatureManagement;

/// <summary>
/// Who a flag is evaluated for: a user, their tenant and the groups they belong to. Percentage rollouts,
/// user and group targeting, and variant allocation all read these values.
/// </summary>
/// <remarks>
/// <para>
/// The targeting key, which percentage rollouts hash, is the user id; when there is no user (a background
/// job working for one tenant) it is the tenant id (<see cref="TenantId.ToString()"/>), so a rollout then applies
/// to whole tenants.
/// </para>
/// <para>
/// The tenant id, as <see cref="TenantId.ToString()"/> (a lowercase GUID), is also added to the groups, so <c>Microsoft.Targeting</c> can target tenants by listing them
/// under <c>Groups</c>, and variant allocation can list them under <c>group</c>.
/// </para>
/// </remarks>
public sealed class FeatureTargetingContext
{
    /// <summary>Creates a targeting context.</summary>
    /// <param name="userId">The user's stable id, or <see langword="null"/> when there is no user.</param>
    /// <param name="tenantId">The tenant, or <see langword="null"/>; <see langword="default"/> is treated as <see langword="null"/>.</param>
    /// <param name="groups">The groups the caller belongs to (roles, plans, cohorts).</param>
    public FeatureTargetingContext(string? userId, TenantId? tenantId = null, IEnumerable<string>? groups = null)
    {
        UserId = string.IsNullOrWhiteSpace(userId) ? null : userId;
        TenantId = tenantId is { IsDefault: false } ? tenantId : null;
        Groups = groups?.Where(static g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    }

    /// <summary>The user's stable id, or <see langword="null"/>.</summary>
    public string? UserId { get; }

    /// <summary>The tenant, or <see langword="null"/>.</summary>
    public TenantId? TenantId { get; }

    /// <summary>The groups the caller belongs to, without blanks or duplicates.</summary>
    public IReadOnlyList<string> Groups { get; }

    /// <summary>The value percentage rollouts hash: <see cref="UserId"/>, or <see cref="TenantId"/> when there is no user.</summary>
    public string? TargetingKey => UserId ?? TenantId?.ToString();

    /// <summary>Targets a tenant as a whole, with no user. Percentage rollouts then apply per tenant.</summary>
    /// <param name="tenantId">The tenant. Must not be <see langword="default"/>.</param>
    /// <returns>The targeting context.</returns>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> is <see langword="default"/>.</exception>
    public static FeatureTargetingContext ForTenant(TenantId tenantId)
    {
        if (tenantId.IsDefault)
        {
            throw new ArgumentException("The tenant identifier must not be default(TenantId).", nameof(tenantId));
        }

        return new FeatureTargetingContext(null, tenantId);
    }

    /// <summary>
    /// Converts this context to an OpenFeature <see cref="EvaluationContext"/>, using the attribute names in
    /// <see cref="FeatureContextKeys"/>.
    /// </summary>
    /// <returns>The evaluation context.</returns>
    public EvaluationContext ToEvaluationContext()
    {
        EvaluationContextBuilder builder = EvaluationContext.Builder();
        if (TargetingKey is not null)
        {
            builder.SetTargetingKey(TargetingKey);
        }

        if (TenantId is { } tenantId)
        {
            builder.Set(FeatureContextKeys.TenantId, tenantId.ToString());
        }

        if (Groups.Count > 0)
        {
            builder.Set(FeatureContextKeys.Groups, new Value(Groups.Select(static g => new Value(g)).ToList()));
        }

        return builder.Build();
    }
}

/// <summary>
/// The OpenFeature evaluation-context attribute names this package reads and writes, in addition to the
/// standard targeting key.
/// </summary>
public static class FeatureContextKeys
{
    /// <summary>The tenant id attribute (a string, <see cref="SharedKernel.Execution.Tenancy.TenantId.ToString()"/>).</summary>
    public const string TenantId = "tenantId";

    /// <summary>The groups attribute (a list of strings).</summary>
    public const string Groups = "groups";
}
