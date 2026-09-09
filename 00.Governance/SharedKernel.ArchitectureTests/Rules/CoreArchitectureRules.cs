using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates mechanizing <c>01.Core</c>-domain conventions. The platform's
/// first <c>01.Core</c>-domain architecture-rule class.
/// </summary>
/// <remarks>
/// <para>
/// Rule 1 — <see cref="DiExtensionsUseTryAddRegistrationConvention"/> (P-523/WO-083): mechanizes
/// P-518's corrected DI-registration idiom — every one of <c>01.Core</c>'s own DI extension
/// methods registers its own services via <c>TryAdd*</c>/<c>TryAddEnumerable</c>, never a plain
/// <c>Add*</c> — closing the door on the convention silently drifting back the next time someone
/// adds a registration line, mirroring this platform's established "follow a corrected default
/// with a structural lock" pattern (P-401, P-410, P-432, P-489, P-490).
/// </para>
/// <para>
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class CoreArchitectureRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied
    /// <c>01.Core</c> assemblies contains a <c>Call</c>/<c>Callvirt</c> instruction invoking
    /// <c>ServiceCollectionServiceExtensions</c>' plain <c>AddSingleton</c>, <c>AddScoped</c>, or
    /// <c>AddTransient</c> — every one of <c>01.Core</c>'s own registrations must go through
    /// <c>TryAddSingleton</c>/<c>TryAddScoped</c>/<c>TryAddTransient</c>/<c>TryAddEnumerable</c>
    /// instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// See <see cref="NoPlainServiceCollectionRegistrationPredicate"/> for the full detection
    /// technique, its deliberate name+declaring-type precision, and its documented non-generic-
    /// overload coverage note — no exemption is applied to any type inside the supplied
    /// assemblies.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>services.AddSingleton&lt;IOneWayHasher, Pbkdf2OneWayHasher&gt;();</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>services.TryAddSingleton&lt;IOneWayHasher, Pbkdf2OneWayHasher&gt;();</code>
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// The assemblies to evaluate — every <c>01.Core</c> package that declares its own DI
    /// extension method(s) (<c>SharedKernel.Primitives</c>, <c>SharedKernel.Configuration</c>,
    /// <c>SharedKernel.Compression</c>, <c>SharedKernel.Cryptography</c>,
    /// <c>SharedKernel.Cryptography.Argon2</c>, <c>SharedKernel.Cryptography.KeyVault.Azure</c>,
    /// <c>SharedKernel.FeatureManagement</c>, <c>SharedKernel.Localization</c>,
    /// <c>SharedKernel.Validation</c>).
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no supplied assembly contains a plain
    /// <c>AddSingleton</c>/<c>AddScoped</c>/<c>AddTransient</c> registration call.
    /// </returns>
    public static ConditionList DiExtensionsUseTryAddRegistrationConvention(
        params Assembly[] assemblies) =>
        Types
            .InAssemblies(assemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoPlainServiceCollectionRegistrationPredicate());
}
