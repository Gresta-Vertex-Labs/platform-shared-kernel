namespace SharedKernel.ArchitectureTests;

/// <summary>
/// Governance allow-list for SK0012 <c>MakeGenericMethodReflection</c> exceptions.
/// </summary>
/// <remarks>
/// <para>
/// This registry is the <strong>sole</strong> mechanism for authorising a
/// <c>MakeGenericMethod</c> call in production platform code. No other suppression
/// mechanism is accepted: <c>#pragma warning disable SK0012</c>,
/// <c>[SuppressMessage]</c>, or inline comments do not exempt a type from the IL-level
/// rule enforced by <see cref="Predicates.NoMakeGenericMethodReflectionPredicate"/>.
/// </para>
/// <para>
/// <strong>Motivating incident (P-147 / WO-024):</strong>
/// <c>SharedKernel.Persistence.EfCore.EncryptionRotationService.LoadBatchAsync</c> shipped
/// a <c>GetMethod("LoadBatchAsync").MakeGenericMethod(entityType).Invoke(...)</c> pattern
/// while the same package's <c>CLAUDE.md</c> documented expression trees as the platform
/// gold standard. Documentation alone did not prevent the violation from reaching the main
/// branch; this registry converts the convention into a mechanical build-time gate that
/// requires explicit governance review for every exception.
/// </para>
/// <para>
/// The fixed <c>EncryptionRotationService</c> (P-147) now uses
/// <c>Expression.Call + Expression.Lambda.Compile()</c> and requires no entry in this
/// registry. <strong>The registry therefore ships empty.</strong>
/// </para>
/// <para>
/// <strong>How to request an exception:</strong>
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       Open a governance review in the root <c>state-map.md</c> with a written rationale
///       explaining why typed dispatch or expression trees cannot be used for the specific
///       call site.
///     </description>
///   </item>
///   <item>
///     <description>
///       Add the <c>(typeFullName, methodName)</c> pair to <see cref="AllowList"/> inside
///       this file, wrapped in an XML <c>&lt;remarks&gt;</c> comment that states: the
///       governance rationale, the approving work order and date, and the reviewing team
///       member.
///     </description>
///   </item>
///   <item>
///     <description>
///       Reference the work order in both the XML doc and the registration call site.
///     </description>
///   </item>
/// </list>
/// <para>
/// <c>typeFullName</c> is the CLR full name including namespace and any enclosing types
/// (e.g. <c>"MyService.Persistence.EfCore.EncryptionRotationService"</c>).
/// <c>methodName</c> is the simple method name (e.g. <c>"LoadBatchAsync"</c>). If the
/// method is overloaded, all overloads with the same name are covered by a single entry —
/// the registry is method-name-scoped, not signature-scoped.
/// </para>
/// </remarks>
public static class ReflectionExemptionRegistry
{
    /// <summary>
    /// The set of approved <c>(typeFullName, methodName)</c> pairs that are exempt from SK0012.
    /// </summary>
    /// <remarks>
    /// Each entry must carry an XML <c>&lt;remarks&gt;</c> doc comment (on the registration
    /// call) stating: the governance rationale, the approving work order and date, and the
    /// reviewing team member. Ships empty — no pre-populated exemptions.
    /// </remarks>
    private static readonly HashSet<(string TypeFullName, string MethodName)> AllowList = new();

    // NOTE: The allow-list is intentionally empty. See class-level remarks for the
    // governance process required before adding any entry.

    /// <summary>
    /// Returns <see langword="true"/> when the combination of <paramref name="typeFullName"/>
    /// and <paramref name="methodName"/> is registered as an approved exception to the
    /// SK0012 <c>MakeGenericMethod</c> prohibition.
    /// </summary>
    /// <param name="typeFullName">
    /// The CLR full type name, as reported by <c>TypeDefinition.FullName</c> (Mono.Cecil).
    /// Includes namespace and any enclosing-type separators (e.g. <c>"Outer/Inner"</c>).
    /// Matched case-sensitively.
    /// </param>
    /// <param name="methodName">
    /// The simple method name, as reported by <c>MethodDefinition.Name</c> (Mono.Cecil).
    /// Matched case-sensitively. All overloads sharing this name are covered by a single
    /// entry.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the pair is in the allow-list; <see langword="false"/>
    /// otherwise.
    /// </returns>
    public static bool IsExempt(string typeFullName, string methodName)
        => AllowList.Contains((typeFullName, methodName));

    /// <summary>
    /// Registers a <c>(typeFullName, methodName)</c> pair as an approved exception to SK0012.
    /// </summary>
    /// <remarks>
    /// This method exists to support tests that exercise the exemption path (T-115). In
    /// production, entries should be added directly to <see cref="AllowList"/> in source
    /// with the required governance XML doc comment.
    /// </remarks>
    /// <param name="typeFullName">The CLR full type name (case-sensitive).</param>
    /// <param name="methodName">The simple method name (case-sensitive).</param>
    internal static void Register(string typeFullName, string methodName)
        => AllowList.Add((typeFullName, methodName));

    /// <summary>
    /// Removes a previously registered <c>(typeFullName, methodName)</c> pair from the allow-list.
    /// </summary>
    /// <remarks>
    /// Used by tests to restore the empty state after exercising the exemption path.
    /// </remarks>
    /// <param name="typeFullName">The CLR full type name (case-sensitive).</param>
    /// <param name="methodName">The simple method name (case-sensitive).</param>
    internal static void Unregister(string typeFullName, string methodName)
        => AllowList.Remove((typeFullName, methodName));
}
