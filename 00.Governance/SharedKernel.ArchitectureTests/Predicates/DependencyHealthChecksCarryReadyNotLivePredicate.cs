using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that inspects dependency-specific health-check registration
/// methods (Redis, database, RabbitMQ, Azure Service Bus, cache, etc.) declared in
/// <c>SharedKernel.ServiceDefaults</c> and fails any such method whose collected string-literal
/// tag set does not contain <c>"ready"</c>, or does contain <c>"live"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.HealthCheckTagIntegrityRules"/> to enforce the platform's
/// dependency-health-check tagging contract documented in <c>13.ServiceDefaults/CLAUDE.md</c>:
/// dependency-specific checks (Redis/database/RabbitMQ/Azure Service Bus/cache) always carry
/// <c>"ready"</c> and never carry <c>"live"</c> — a dependency outage should never take a pod
/// out of the liveness rotation (which would trigger a pointless restart loop), only out of
/// the readiness rotation (which correctly stops new traffic).
/// </para>
/// <para>
/// <strong>Scope (caller-supplied method-name-prefix filter).</strong> Unlike
/// <see cref="NoConflictingLivenessReadinessTagsPredicate"/>, which scopes to the generic
/// <c>Add*HealthCheck</c>/<c>Add*ReadinessCheck</c> naming convention, this predicate is
/// constructed with an explicit list of dependency-check method-name prefixes (e.g.
/// <c>"AddRedis"</c>, <c>"AddDatabase"</c>, <c>"AddRabbitMq"</c>, <c>"AddAzureServiceBus"</c>,
/// <c>"AddCache"</c>) supplied by the consuming test project. This keeps the predicate generic
/// — the governance layer does not guess at extension method names that may not exist yet
/// (<c>SharedKernel.ServiceDefaults</c> is not buildable until P-170 ships).
/// </para>
/// <para>
/// <strong>Detection technique (IL-literal collection, not full data-flow analysis).</strong>
/// Reuses the same <c>Ldstr</c> literal-collection technique as
/// <see cref="NoConflictingLivenessReadinessTagsPredicate"/>. If a tag value is computed
/// dynamically rather than passed as a literal, this rule cannot see it and the gate is
/// advisory only for that call site.
/// </para>
/// </remarks>
public sealed class DependencyHealthChecksCarryReadyNotLivePredicate : ICustomRule
{
    private const string LiveTag = "live";
    private const string ReadyTag = "ready";

    private readonly string[] _dependencyCheckMethodNamePrefixes;

    /// <summary>
    /// Constructs the predicate with the dependency-check method-name prefixes recognized
    /// by the consuming test project.
    /// </summary>
    /// <param name="dependencyCheckMethodNamePrefixes">
    /// Method-name prefixes identifying dependency-specific health-check registration
    /// extensions (e.g. <c>"AddRedis"</c>, <c>"AddDatabase"</c>, <c>"AddRabbitMq"</c>,
    /// <c>"AddAzureServiceBus"</c>, <c>"AddCache"</c>). A method is in scope when its name
    /// starts with any of these prefixes.
    /// </param>
    public DependencyHealthChecksCarryReadyNotLivePredicate(
        params string[] dependencyCheckMethodNamePrefixes)
    {
        _dependencyCheckMethodNamePrefixes = dependencyCheckMethodNamePrefixes;
    }

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when every in-scope dependency-check method
    /// in <paramref name="type"/> carries <c>"ready"</c> and does not carry <c>"live"</c> in
    /// its collected tag literal set. Returns <see langword="false"/> (rule violated) when
    /// <c>"ready"</c> is absent, or <c>"live"</c> is present.
    /// </summary>
    /// <param name="type">
    /// The Mono.Cecil <see cref="TypeDefinition"/> to inspect. Supplied by NetArchTest's
    /// <c>MeetCustomRule</c> evaluation loop.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every in-scope method's tag literal set carries
    /// <c>"ready"</c> and not <c>"live"</c>; <see langword="false"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (!IsDependencyCheckMethod(method.Name))
                continue;

            if (method.Body is null)
                continue;

            var literals = NoConflictingLivenessReadinessTagsPredicate.CollectStringLiterals(
                method.Body);

            if (!literals.Contains(ReadyTag))
                return false;

            if (literals.Contains(LiveTag))
                return false;
        }

        return true;
    }

    private bool IsDependencyCheckMethod(string methodName)
    {
        foreach (var prefix in _dependencyCheckMethodNamePrefixes)
        {
            if (methodName.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
