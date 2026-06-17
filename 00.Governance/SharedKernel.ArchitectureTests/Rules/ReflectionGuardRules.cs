using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that enforce the platform-wide prohibition on
/// reflection-based generic method invocation (<c>GetMethod</c>/<c>GetMethods</c> +
/// <c>MakeGenericMethod</c> + <c>Invoke</c>) introduced by WO-024 P-153.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why an IL-level rule, not a Roslyn analyzer?</strong>
/// <c>MethodInfo.MakeGenericMethod</c> is called at runtime on a variable of type
/// <c>MethodInfo</c> returned from <c>GetMethod</c>/<c>GetMethods</c>. A Roslyn analyzer
/// would only fire on the literal string <c>"MakeGenericMethod"</c> in an invocation
/// expression, missing any case where the <c>MethodInfo</c> is obtained from a method call,
/// stored in a variable, and then <c>.MakeGenericMethod(...)</c> is called in a separate
/// statement. IL inspection (Mono.Cecil) is the only reliable detection mechanism.
/// </para>
/// <para>
/// <strong>Motivating incident (P-147 / WO-024):</strong>
/// <c>SharedKernel.Persistence.EfCore.EncryptionRotationService.LoadBatchAsync</c> shipped
/// a <c>GetMethod("LoadBatchAsync").MakeGenericMethod(entityType).Invoke(...)</c> pattern
/// while the same package's <c>CLAUDE.md</c> documented expression trees as the gold
/// standard. Documentation alone did not prevent the violation; this class makes it
/// mechanically impossible without an explicit governance review entry in
/// <see cref="ReflectionExemptionRegistry"/>.
/// </para>
/// <para>
/// <strong>Exemption mechanism:</strong> <see cref="ReflectionExemptionRegistry"/> is the
/// sole accepted exception path. No <c>#pragma warning disable</c>, no
/// <c>[SuppressMessage]</c>, no inline comments exempt a type from this rule.
/// </para>
/// <para>
/// <strong>Compliant alternatives:</strong>
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       Typed dispatch — call the generic method directly through a known interface or
///       delegate, or through a compiled expression tree.
///     </description>
///   </item>
///   <item>
///     <description>
///       Expression trees — <c>Expression.Call(...).Compile()</c> as used in the fixed
///       <c>EncryptionRotationService</c> (P-147) and in <c>TenantedDbContext</c>. This
///       is the platform gold standard.
///     </description>
///   </item>
/// </list>
/// </remarks>
public static class ReflectionGuardRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> that asserts no non-abstract type in
    /// <paramref name="assembly"/> contains a <c>Call</c> or <c>Callvirt</c> IL opcode
    /// whose target method name is <c>"MakeGenericMethod"</c>, unless the type+method
    /// combination is registered in <see cref="ReflectionExemptionRegistry"/>.
    /// </summary>
    /// <param name="assembly">
    /// The production assembly to scan. Must not be a test project, a benchmark project,
    /// or <c>SharedKernel.ArchitectureTests</c> itself — pass production assemblies only,
    /// supplied via <c>typeof(SomeProductionType).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> — call <c>.GetResult()</c> to obtain pass/fail
    /// information. When the rule fails, <c>FailingTypeNames</c> names every offending type.
    /// The predicate failure message also names the offending method.
    /// </returns>
    /// <remarks>
    /// The <c>.AreNotAbstract()</c> filter excludes compiler-generated abstract helper types
    /// (e.g. async state-machine types) that may contain unusual IL patterns, consistent
    /// with the same filter applied by
    /// <see cref="DomainGoldStandardRules.DomainServicesMustExtendAbstractBase"/>.
    /// </remarks>
    public static ConditionList NoMakeGenericMethodReflection(Assembly assembly)
        => Types
            .InAssembly(assembly)
            .That()
            .AreNotAbstract()
            .Should()
            .MeetCustomRule(new NoMakeGenericMethodReflectionPredicate());
}
