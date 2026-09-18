using OpenFeature.Model;

namespace SharedKernel.FeatureManagement;

/// <summary>
/// Who a flag is evaluated for: a user, their tenant and the groups they belong to. Percentage rollouts,
/// user and group targeting, and variant allocation all read these values.
/// </summary>
/// <remarks>
/// <para>
/// The targeting key, which percentage rollouts hash, is the user id; when there is no user (a background
/// job working for one tenant) it is the tenant id, so a rollout then applies to whole tenants.
/// </para>
/// <para>
/// The tenant id is also added to the groups, so <c>Microsoft.Targeting</c> can target tenants by listing them
/// under <c>Groups</c>, and variant allocation can list them under <c>group</c>.
/// </para>
/// </remarks>
public sealed class FeatureTargetingContext
{
    /// <summary>Creates a targeting context.</summary>
    /// <param name="userId">The user's stable id, or <see langword="null"/> when there is no user.</param>
    /// <param name="tenantId">The tenant's stable id, or <see langword="null"/>.</param>
    /// <param name="groups">The groups the caller belongs to (roles, plans, cohorts).</param>
    public FeatureTargetingContext(string? userId, string? tenantId = null, IEnumerable<string>? groups = null)
    {
        UserId = string.IsNullOrWhiteSpace(userId) ? null : userId;
        TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
        Groups = groups?.Where(static g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    }

    /// <summary>The user's stable id, or <see langword="null"/>.</summary>
    public string? UserId { get; }

    /// <summary>The tenant's stable id, or <see langword="null"/>.</summary>
    public string? TenantId { get; }

    /// <summary>The groups the caller belongs to, without blanks or duplicates.</summary>
    public IReadOnlyList<string> Groups { get; }

    /// <summary>The value percentage rollouts hash: <see cref="UserId"/>, or <see cref="TenantId"/> when there is no user.</summary>
    public string? TargetingKey => UserId ?? TenantId;

    /// <summary>Targets a tenant as a whole, with no user. Percentage rollouts then apply per tenant.</summary>
    /// <param name="tenantId">The tenant's stable id.</param>
    /// <returns>The targeting context.</returns>
    public static FeatureTargetingContext ForTenant(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
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

        if (TenantId is not null)
        {
            builder.Set(FeatureContextKeys.TenantId, TenantId);
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
    /// <summary>The tenant id attribute (a string).</summary>
    public const string TenantId = "tenantId";

    /// <summary>The groups attribute (a list of strings).</summary>
    public const string Groups = "groups";
}
