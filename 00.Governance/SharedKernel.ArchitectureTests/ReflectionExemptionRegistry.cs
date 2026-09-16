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
/// <strong>Motivating incident:</strong>
/// <c>SharedKernel.Persistence.EfCore.EncryptionRotationService.LoadBatchAsync</c> shipped
/// a <c>GetMethod("LoadBatchAsync").MakeGenericMethod(entityType).Invoke(...)</c> pattern
/// while the same package's <c>CLAUDE.md</c> documented expression trees as the platform
/// gold standard. Documentation alone did not prevent the violation from reaching the main
/// branch; this registry converts the convention into a mechanical build-time gate that
/// requires explicit governance review for every exception.
/// </para>
/// <para>
/// The fixed <c>EncryptionRotationService</c> now uses
/// <c>Expression.Call + Expression.Lambda.Compile()</c> and requires no entry in this
/// registry.
/// </para>
/// <para>
/// <strong>The registry is currently empty.</strong> Its one-time entry for
/// <c>SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher</c>'s former
/// <c>PublishSingle</c> method (WO-039/P-240) was retired at P-544: 05.Application's redesign
/// rewrote the dispatcher's runtime-type-dispatch technique from a cached
/// <c>MethodInfo.MakeGenericMethod</c> delegate to <see cref="Type.MakeGenericType"/> +
/// <see cref="Activator.CreateInstance(Type, object?[])"/> (see <c>DispatchAsync</c>/
/// <c>BuildNotification</c> in the current source) — a technique <see cref="Predicates.NoMakeGenericMethodReflectionPredicate"/>
/// does not match at all (it looks only for a call named exactly <c>"MakeGenericMethod"</c>), so
/// no exemption is needed for the current implementation. The "Closure-free static-lambda naming"
/// note below is kept as a reusable implementation note for the NEXT exemption request, not
/// because a current entry depends on it.
/// </para>
/// <para>
/// <strong>Closure-free static-lambda naming (reusable implementation note):</strong> when a
/// lambda expression captures no outer state and is declared with the <c>static</c> modifier
/// (e.g. <c>Dictionary.GetOrAdd(key, static t => {...})</c>), Roslyn does not compile it as a
/// method on the declaring type. Instead it is hoisted onto a compiler-generated, cached
/// singleton "display class" nested type — conventionally named <c>&lt;&gt;c</c> — nested
/// inside the declaring type. Mono.Cecil's <c>TypeDefinition.FullName</c> reports the nesting
/// separator as <c>/</c> (not <c>.</c>), e.g.
/// <c>SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher/&lt;&gt;c</c>. The
/// synthesized method itself is named <c>&lt;{EnclosingMethodName}&gt;b__{classToken}_{ordinal}</c>
/// — the ordinal is assigned by the compiler and is NOT derivable from source alone; it must be
/// read from the actual compiled IL (e.g. via a throwaway Mono.Cecil scan, or by reading the
/// <see cref="Rules.ReflectionGuardRules"/> failure message with the exemption temporarily
/// absent) before an <see cref="AllowList"/> entry can be added. Any future exemption request for
/// a closure-free static lambda must follow this same empirical, not assumed, verification step.
/// </para>
/// <para>
/// <strong>How to request an exception:</strong>
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       Open a governance review with a written rationale
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
/// <para>
/// <strong>Known open gap.</strong>
/// <c>SharedKernel.Messaging.MassTransit.MassTransitEventPublisher.BuildPublisher</c> — the
/// structurally identical lambda-closure <c>MakeGenericMethod</c> pattern cited as this
/// registry's own precedent — and <c>MessagingBusBuilder.AddActivity</c> (a second, differently-
/// shaped <c>MakeGenericMethod</c> call) are themselves UNREGISTERED in this allow-list today.
/// Neither is registered here; both remain a candidate follow-up against
/// <c>07.Messaging</c>'s real assembly. Running <see cref="Rules.ReflectionGuardRules.NoMakeGenericMethodReflection"/>
/// against the real <c>SharedKernel.Messaging.MassTransit</c> assembly will currently fail until
/// that follow-up work order registers both entries.
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
    /// reviewing team member. Individual tuple elements in a field initializer cannot carry a
    /// compiler-recognized <c>///</c> XML doc comment, so each entry is instead documented with
    /// a clearly demarcated block comment immediately above it (see below).
    /// </remarks>
    private static readonly HashSet<(string TypeFullName, string MethodName)> AllowList = new()
    {
        // No entries currently registered. The former WO-039/P-240 entry for
        // SharedKernel.Application.DomainEvents.MediatRDomainEventDispatcher's PublishSingle
        // method was retired at P-544 — the dispatcher no longer calls MakeGenericMethod at all
        // (see this file's class-level remarks). Add a new entry here, with the full governance
        // rationale documented per the "How to request an exception" steps above, the next time a
        // genuine MakeGenericMethod use needs an exemption.
    };

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
    /// This method exists to support tests that exercise the exemption path. In
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
