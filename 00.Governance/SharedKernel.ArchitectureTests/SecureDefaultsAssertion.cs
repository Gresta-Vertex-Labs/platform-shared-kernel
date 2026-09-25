using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharedKernel.ArchitectureTests;

/// <summary>
/// Mono.Cecil-based helper that asserts a caller-supplied options type's default-constructed
/// property value matches a caller-supplied hardened expectation.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="PipelineOrderAssertion"/>'s/<see cref="LoggingEventIdIntegrityAssertion"/>'s/
/// <see cref="WellKnownConstantOwnershipAssertion"/>'s precedent — a plain public helper, not a
/// NetArchTest <c>ConditionList</c>/<c>ICustomRule</c>, because "a specific options type's specific
/// property resolves to a specific default value" has no single-assembly "fire on a contrived
/// violating assembly" shape a <c>ConditionList</c> naturally expresses, and no source-level
/// anti-pattern a Roslyn analyzer could target — the motivating defect
/// (<c>MtlsAuthenticationOptions.AllowedCertificateTypes</c>/<c>.RevocationMode</c> shipped as
/// <c>CertificateTypes.All</c>/<c>X509RevocationMode.NoCheck</c>) was a correctly-shaped,
/// syntactically unremarkable property initializer carrying the wrong constant.
/// </para>
/// <para>
/// <strong>Caller-supplied everything.</strong> <c>00.Governance</c> never references
/// <c>SharedKernel.Security.Mtls</c>/<c>.Oidc</c> directly (<c>00.Governance</c> references
/// nothing in production code). The consuming test project supplies <c>optionsType</c> via
/// <c>typeof(MtlsAuthenticationOptions)</c>/the real <c>Jwt</c> sub-options type/
/// <c>typeof(DpopOptions)</c> (test-only <c>ProjectReference</c>, <c>PrivateAssets="all"</c>), and
/// <c>expectedEnumMemberName</c>/<c>forbiddenValues</c> from the real, shipped hardened-default
/// values — matching the same "caller supplies the assembly/values, never hard-code them here"
/// discipline established by every prior non-<c>ConditionList</c> helper in this file.
/// </para>
/// <para>
/// <strong>Technique 1 — <see cref="AssertEnumPropertyDefaultEquals"/>: constructor/property-
/// initializer enum-default-value resolution.</strong> Loads <c>optionsType</c>'s
/// <see cref="TypeDefinition"/> via Mono.Cecil, locates its parameterless instance constructor
/// (Roslyn emits every property-initializer assignment at the start of every constructor body, in
/// declaration order, before any explicit constructor logic — including the implicit
/// compiler-generated default constructor when no explicit constructor is declared), finds the
/// <see cref="OpCodes.Stfld"/> targeting the property's compiler-generated backing field
/// (<c>&lt;propertyName&gt;k__BackingField</c>), and reads the immediately-preceding
/// <c>Ldc_I4</c>-family opcode's loaded integral constant. The enum member NAME is then resolved
/// via ordinary reflection (<see cref="Enum.GetName(Type, object)"/>) against the property's own
/// live <see cref="PropertyInfo.PropertyType"/> — deliberately NOT via a second Mono.Cecil
/// <c>TypeReference.Resolve()</c> hop, since the property's enum type may be declared in a
/// framework-shared assembly (e.g. <c>Microsoft.AspNetCore.Authentication.Certificate</c>'s
/// <c>CertificateTypes</c>) that is not guaranteed to be resolvable as a standalone file on disk
/// the way Mono.Cecil's default resolver expects; the CLR has already loaded it as a real,
/// runnable <see cref="Type"/> by the time this helper runs, so reflection is the correct tool for
/// that half of the resolution and Mono.Cecil is only needed for the constant literal itself.
/// This is a DIFFERENT technique from <see cref="Predicates.StringConstantsClassDetector"/>'s
/// field-shape+literal-value resolution (which resolves <c>const</c>/<c>static readonly string</c>
/// FIELDS directly) — here the value is resolved from a property AUTO-INITIALIZER assigned inside
/// a constructor body, and the value type is an enum's underlying integral constant, not a string.
/// </para>
/// <para>
/// <strong>Technique 2 — <see cref="AssertStringCollectionPropertyDefaultExcludes"/>: constructor-
/// body array/collection-initializer literal collection.</strong> Extends the established
/// <c>Ldstr</c> literal-collection technique
/// (<see cref="Rules.HealthCheckTagIntegrityRules"/>/<see cref="Rules.MetricsInstrumentationRules"/>)
/// to a NEW call-site shape — a constructor-body array/collection-initializer feeding a property's
/// backing-field <see cref="OpCodes.Stfld"/>, rather than a method-name-prefix-scoped call site.
/// Collects every <see cref="OpCodes.Ldstr"/> operand appearing between the immediately-preceding
/// <see cref="OpCodes.Stfld"/>/<see cref="OpCodes.Stsfld"/> instruction (the tail of the PREVIOUS
/// property/field initializer segment, or the start of the constructor body when none precedes)
/// and the target <see cref="OpCodes.Stfld"/> — the same "each initializer is a contiguous IL
/// segment ending in its own <c>Stfld</c>" shape every C# property/field initializer compiles to,
/// regardless of whether the target property's backing collection is materialized as an array
/// (<c>T[]</c> — which implements <see cref="IReadOnlyCollection{T}"/> directly for
/// single-dimensional zero-based arrays, so no wrapping call is emitted) or a list-like collection
/// built via a sequence of <c>Add</c> calls.
/// </para>
/// <para>
/// <strong>Empty/null-default detection.</strong> Fails if the collected literal set is empty —
/// this covers BOTH a <c>null</c> default (no <see cref="OpCodes.Stfld"/> targeting the backing
/// field exists in the constructor at all, since an uninitialized auto-property emits no
/// assignment — the CLR zero-initializes the field to <see langword="null"/>) AND an explicit
/// empty-array default (<c>Array.Empty&lt;string&gt;()</c>/<c>[]</c>, which emits a
/// <see cref="OpCodes.Call"/>/<see cref="OpCodes.Newarr"/> with zero <see cref="OpCodes.Ldstr"/>
/// operands) — both shapes satisfy the "empty, null" acceptance-criterion wording via the same one
/// non-empty check, unless the caller opts out via <c>requireNonEmpty: false</c>.
/// </para>
/// <para>
/// <strong>Forbidden-value detection.</strong> Fails if the collected set's intersection with the
/// caller-supplied <c>forbiddenValues</c> is non-empty, compared case-insensitively — JWS
/// <c>alg</c> values are compared case-insensitively here defensively, even though RFC 7518
/// defines <c>"none"</c> as lowercase-exact, since this check validates a HARDENED DEFAULT rather
/// than parsing untrusted wire input. The exclusion set itself is entirely caller-supplied — this
/// helper never hard-codes a security policy (e.g. "always forbid HS256"); the consuming test
/// project decides what "forbidden" means for its own options type.
/// </para>
/// <para>
/// Both methods throw a single test-framework-agnostic <see cref="InvalidOperationException"/>
/// naming the property, the actual resolved default, and the expected/forbidden value(s) on
/// failure — the same aggregate-failure-message convention as <see cref="PipelineOrderAssertion"/>/
/// <see cref="LoggingEventIdIntegrityAssertion"/>/<see cref="WellKnownConstantOwnershipAssertion"/>.
/// </para>
/// <para>
/// <strong>Technique 3 — <see cref="AssertStringCollectionPropertyDefaultEquals"/>: order-
/// sensitive constructor-initializer string-sequence equality.</strong> Added by
/// Reuses the exact private literal-collection walk
/// Technique 2 already built (<c>ResolveConstructorStringLiteralCollectionDefault</c> — collect
/// every <see cref="OpCodes.Ldstr"/> operand walking BACKWARD from the target property's
/// backing-field <see cref="OpCodes.Stfld"/> to the previous initializer segment's boundary, then
/// reverse) rather than a forbidden-intersection check, because relative order is the security
/// property being locked: <c>TenantResolutionOptions.StrategyOrder</c>'s
/// <c>[Header, Claim, Database]</c> and <c>[Claim, Header, Database]</c> contain identical
/// elements but only the latter is secure under first-non-null-result-wins resolution semantics.
/// Confirmed against the real, compiled <c>TenantResolutionOptions</c> constructor that the
/// backward-walk-then-reverse technique correctly reconstructs forward declaration order even for
/// a C# collection-expression default (<c>[a, b, c]</c>), which the Roslyn compiler lowers to a
/// <c>newarr</c>/<c>dup</c>/<c>ldc.i4.N</c>/<c>ldstr</c>/<c>stelem.ref</c> sequence per element —
/// interleaved index-then-value IL, not a flat run of consecutive <c>Ldstr</c> instructions — yet
/// the existing backward walk still collects the three <c>Ldstr</c> operands in reverse emission
/// order and the final <see cref="List{T}.Reverse()"/> restores the correct forward sequence. Fails
/// if the collected sequence's length differs from <c>expectedValuesInOrder</c>'s length, or if
/// any positional element differs under ordinal (case-sensitive) comparison — platform
/// <c>StrategyName</c> values are compile-time constants, not user input, so no case-insensitive
/// leniency is warranted here unlike the JWS-algorithm check in
/// <see cref="AssertStringCollectionPropertyDefaultExcludes"/>.
/// </para>
/// <para>
/// <strong>Technique 4 — <see cref="AssertMethodBodyInvokesMethod"/>: method-body invocation-
/// presence assertion, including compiler-generated lambda closures.</strong> Added by
/// The first check in this file that fails on the
/// ABSENCE of a call site rather than the presence of an unwanted one. Loads
/// <c>declaringType</c>'s <see cref="TypeDefinition"/>, locates the single method
/// named <c>methodName</c> (a setup exception, never a silent false pass/fail, if zero or more
/// than one match), and scans its instruction body for a <see cref="OpCodes.Call"/>/
/// <see cref="OpCodes.Callvirt"/> instruction whose resolved <see cref="MethodReference"/> matches
/// <c>calleeDeclaringType</c>/<c>calleeMethodName</c> by name and declaring-type
/// <see cref="TypeReference.FullName"/>. <strong>Confirmed by direct Mono.Cecil inspection of the
/// real, shipped <c>MtlsForwardedHeaderExtensions.AddMtlsForwardedHeaderCertificate</c> IL that a
/// plain single-method-body scan is insufficient</strong>: the call to
/// <c>MtlsLog.ForwardedHeaderTrustBoundaryUnconfigured</c> lives inside the C#
/// <c>.PostConfigure&lt;ILoggerFactory&gt;((configuredOptions, loggerFactory) =&gt; { ... })</c>
/// lambda argument, which the Roslyn compiler lowers to its own method
/// (<c>&lt;AddMtlsForwardedHeaderCertificate&gt;b__0_0</c>) on a compiler-generated
/// <c>&lt;&gt;c</c> closure type nested inside <c>MtlsForwardedHeaderExtensions</c> — the
/// enclosing method's own IL contains only a delegate construction (<c>ldftn</c>/<c>newobj</c>
/// against a cached static field), never the callee call itself. Consequently, when the direct
/// body scan finds no match, this method additionally scans every method on every nested type of
/// <c>declaringType</c> whose name starts with <c>&lt;{methodName}&gt;b__</c> — the Roslyn-emitted
/// naming convention for a lambda declared inside <c>methodName</c>, regardless of whether the
/// compiler hosts it on the shared <c>&lt;&gt;c</c> cache type (no captured outer state) or a
/// per-call <c>&lt;&gt;c__DisplayClassN_M</c> closure type (captured state) — both are nested
/// types of <c>declaringType</c> either way. This mirrors how a source-generated
/// <c>[LoggerMessage]</c> partial-method call compiles to a perfectly ordinary
/// <see cref="OpCodes.Call"/> at whichever call site invokes it (Rule 4 of this phase's own
/// design) — the added complexity here is entirely about WHERE that call site's IL physically
/// lives when the call is made from inside a lambda, not about the source generator itself.
/// </para>
/// <para>
/// <strong>Technique 5 — <see cref="AssertMethodBodyRegistersSingleton"/>: closed-generic
/// DI-registration-presence assertion.</strong> Added by
/// Generalizes and INVERTS
/// <see cref="Rules.SecurityArchitectureRules"/>'s sibling predicate (the internal
/// <c>NoSecurityContextSingletonRegistrationPredicate</c>)
/// <see cref="GenericInstanceMethod.GenericArguments"/> inspection technique from "assert ABSENCE
/// of a singleton registration for a forbidden type" to "assert PRESENCE of a singleton
/// registration for exactly the given service→implementation pair." Locates
/// <c>declaringType</c>'s single method matching <c>methodName</c> (a setup
/// exception, never a silent false pass/fail, on zero or more than one match — the same
/// discipline as every other method-body-scanning technique in this class) and scans its
/// instruction body — plus, reusing <see cref="AssertMethodBodyInvokesMethod"/>'s proven
/// closure-scanning extension, every method on every nested type whose name starts
/// with <c>&lt;{methodName}&gt;b__</c> — for a <see cref="OpCodes.Call"/>/
/// <see cref="OpCodes.Callvirt"/> instruction whose operand is a closed
/// <see cref="GenericInstanceMethod"/> named either <c>"AddSingleton"</c> or
/// <c>"TryAddSingleton"</c> whose <see cref="GenericInstanceMethod.GenericArguments"/> equal
/// <c>[serviceType, implementationType]</c> in that order.
/// </para>
/// <para>
/// <strong>Both registration-method names are accepted, deliberately.</strong> This phase's own
/// authoring-time design prose (Implementation Rule 3) assumed the real registration would use
/// plain <c>AddSingleton&lt;TService,TImplementation&gt;()</c>, reasoning from
/// <c>WithUrlValidator&lt;T&gt;()</c>'s documented "last call wins" override semantics. Direct
/// inspection of the real, shipped
/// <c>SharedKernel.Integration.Webhooks.Extensions.ServiceCollectionExtensions.AddSharedKernelWebhooks</c>
/// before this method was written found it instead uses
/// <c>services.TryAddSingleton&lt;IWebhookUrlValidator, PrivateNetworkWebhookUrlValidator&gt;()</c>
/// (<c>Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions</c>)
/// — a deliberate, correct choice by <c>15.Integration</c>, not a defect: <c>TryAddSingleton</c>
/// is a no-op when a registration for <c>IWebhookUrlValidator</c> already exists, which is
/// exactly what lets a consuming service call <c>WithUrlValidator&lt;T&gt;()</c> either before or
/// after <c>AddSharedKernelWebhooks()</c> and still get its override (the "last call wins" prose
/// this phase's design read literally, but which <c>WithUrlValidator&lt;T&gt;()</c>'s own
/// implementation achieves via <c>RemoveAll&lt;IWebhookUrlValidator&gt;()</c> immediately followed
/// by a plain <c>AddSingleton</c>, not via registration-order-dependent <c>TryAdd</c> semantics on
/// the default side). Matching only <c>TryAddSingleton</c> would fit today's one real caller but
/// silently reject a legitimate future <c>AddSingleton</c>-based default elsewhere in the
/// platform; matching only <c>AddSingleton</c> would miss the one real caller this phase exists to
/// lock — so both literals are accepted rather than the class being narrowed to whichever shape
/// happened to match first.
/// </para>
/// <para>
/// <strong>Exact-pairing discipline, not mere call-presence.</strong> A registration for the same
/// <c>serviceType</c> with a DIFFERENT closed <c>implementationType</c>
/// argument does not satisfy this check — mirroring
/// <see cref="AssertStringCollectionPropertyDefaultEquals"/>'s order-sensitive (not merely
/// membership-sensitive) discipline, applied here to a registration pairing instead of a string
/// sequence. This is also why <c>WithUrlValidator&lt;T&gt;()</c>'s own
/// <c>services.AddSingleton&lt;IWebhookUrlValidator, TValidator&gt;()</c> call site can never be
/// accidentally satisfy a check pointed at <c>AddSharedKernelWebhooks</c>: even setting aside that
/// <see cref="AssertMethodBodyRegistersSingleton"/> is scoped to one named method, that call's
/// second generic argument is the OPEN generic method parameter <c>TValidator</c>, not a closed
/// <c>PrivateNetworkWebhookUrlValidator</c> reference — its
/// <see cref="GenericInstanceMethod.GenericArguments"/> element resolves to a
/// <see cref="GenericParameter"/>, whose <see cref="MemberReference.FullName"/> can never equal a
/// concrete implementation type's <see cref="Type.FullName"/>.
/// </para>
/// </remarks>
public static class SecureDefaultsAssertion
{
    /// <summary>
    /// Asserts that <paramref name="optionsType"/>'s parameterless-constructed instance resolves
    /// <paramref name="propertyName"/> to the enum member named <paramref name="expectedEnumMemberName"/>.
    /// </summary>
    /// <param name="optionsType">
    /// The options type to inspect (e.g. <c>typeof(MtlsAuthenticationOptions)</c>). Must declare a
    /// parameterless constructor and a public instance enum-typed property named
    /// <paramref name="propertyName"/>.
    /// </param>
    /// <param name="propertyName">The enum-typed property's name (e.g. <c>"RevocationMode"</c>).</param>
    /// <param name="expectedEnumMemberName">
    /// The expected default's enum member name (e.g. <c>"Offline"</c>), compared ordinally,
    /// case-sensitive.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="propertyName"/> does not resolve to a public instance
    /// enum-typed property on <paramref name="optionsType"/>, when no default-value assignment for
    /// it is found in the parameterless constructor, or when the resolved default does not match
    /// <paramref name="expectedEnumMemberName"/>.
    /// </exception>
    public static void AssertEnumPropertyDefaultEquals(
        Type optionsType,
        string propertyName,
        string expectedEnumMemberName)
    {
        var property = GetPublicInstanceProperty(optionsType, propertyName);

        if (!property.PropertyType.IsEnum)
        {
            throw new InvalidOperationException(
                $"SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals: property "
                    + $"'{optionsType.FullName}.{propertyName}' is not an enum-typed property "
                    + $"(actual type: '{property.PropertyType.FullName}').");
        }

        var resolvedValue = ResolveConstructorInt32BackingFieldDefault(optionsType, propertyName);

        if (resolvedValue is null)
        {
            throw new InvalidOperationException(
                "SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals found no default-value "
                    + $"assignment for '{optionsType.FullName}.{propertyName}' in its parameterless "
                    + "constructor — expected an enum property initializer.");
        }

        var actualMemberName =
            Enum.GetName(property.PropertyType, resolvedValue.Value)
            ?? $"<unnamed enum value {resolvedValue.Value}>";

        if (!string.Equals(actualMemberName, expectedEnumMemberName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"SecureDefaultsAssertion.AssertEnumPropertyDefaultEquals: "
                    + $"'{optionsType.FullName}.{propertyName}' default-constructs to "
                    + $"'{actualMemberName}', expected '{expectedEnumMemberName}'.");
        }
    }

    /// <summary>
    /// Asserts that <paramref name="optionsType"/>'s parameterless-constructed instance resolves
    /// <paramref name="propertyName"/> to a non-empty string collection (unless
    /// <paramref name="requireNonEmpty"/> is <see langword="false"/>) that contains none of
    /// <paramref name="forbiddenValues"/>.
    /// </summary>
    /// <param name="optionsType">
    /// The options type to inspect (e.g. the real <c>Jwt</c> sub-options type, or
    /// <c>typeof(DpopOptions)</c>). Must declare a parameterless constructor and a public instance
    /// string-collection-typed property named <paramref name="propertyName"/>.
    /// </param>
    /// <param name="propertyName">
    /// The string-collection-typed property's name (e.g. <c>"ValidAlgorithms"</c>).
    /// </param>
    /// <param name="forbiddenValues">
    /// The set of values the collected default MUST NOT contain, compared case-insensitively.
    /// Entirely caller-supplied — this helper never hard-codes a security policy.
    /// </param>
    /// <param name="requireNonEmpty">
    /// When <see langword="true"/> (the default), fails if the collected default is empty or
    /// <see langword="null"/> — a missing allowlist is treated as more dangerous than a wrong one.
    /// Pass <see langword="false"/> to opt out for a property where an empty default is legitimate.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="propertyName"/> does not resolve to a public instance property
    /// on <paramref name="optionsType"/>, when the collected default is empty/null and
    /// <paramref name="requireNonEmpty"/> is <see langword="true"/>, or when the collected default
    /// contains any value in <paramref name="forbiddenValues"/>.
    /// </exception>
    public static void AssertStringCollectionPropertyDefaultExcludes(
        Type optionsType,
        string propertyName,
        IReadOnlyCollection<string> forbiddenValues,
        bool requireNonEmpty = true)
    {
        // Validates the property exists on the type — the resolved PropertyInfo itself is not
        // otherwise needed, since the default value is read from IL rather than reflection (an
        // auto-property has no way to read its own compile-time-assigned default via reflection
        // alone without constructing an instance, which this helper deliberately avoids so it
        // never depends on the type being constructible with any particular DI/validation context).
        _ = GetPublicInstanceProperty(optionsType, propertyName);

        var collected = ResolveConstructorStringLiteralCollectionDefault(optionsType, propertyName);

        var violations = new List<string>();

        if (requireNonEmpty && collected.Count == 0)
        {
            violations.Add(
                $"'{optionsType.FullName}.{propertyName}' default-constructs to an empty or null "
                    + "collection — expected a non-empty hardened default.");
        }

        var forbiddenSet = new HashSet<string>(forbiddenValues, StringComparer.OrdinalIgnoreCase);
        var matched = collected.Where(forbiddenSet.Contains).ToList();

        if (matched.Count > 0)
        {
            violations.Add(
                $"'{optionsType.FullName}.{propertyName}' default-constructs to a collection "
                    + $"containing forbidden value(s): {string.Join(", ", matched)}.");
        }

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                "SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultExcludes found "
                    + $"{violations.Count} violation(s):"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, violations));
        }
    }

    /// <summary>
    /// Asserts that <paramref name="optionsType"/>'s parameterless-constructed instance resolves
    /// <paramref name="propertyName"/> to a string collection whose elements exactly match
    /// <paramref name="expectedValuesInOrder"/>, in the same order.
    /// </summary>
    /// <param name="optionsType">
    /// The options type to inspect (e.g. <c>typeof(TenantResolutionOptions)</c>). Must declare a
    /// parameterless constructor and a public instance string-collection-typed property named
    /// <paramref name="propertyName"/>.
    /// </param>
    /// <param name="propertyName">
    /// The string-collection-typed property's name (e.g. <c>"StrategyOrder"</c>).
    /// </param>
    /// <param name="expectedValuesInOrder">
    /// The exact expected sequence, compared ordinally (case-sensitive) and positionally. Order
    /// matters — this is an ORDER-SENSITIVE check, unlike
    /// <see cref="AssertStringCollectionPropertyDefaultExcludes"/>'s forbidden-intersection check.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="propertyName"/> does not resolve to a public instance property
    /// on <paramref name="optionsType"/>, when the collected default's length differs from
    /// <paramref name="expectedValuesInOrder"/>'s length, or when any positional element differs.
    /// </exception>
    public static void AssertStringCollectionPropertyDefaultEquals(
        Type optionsType,
        string propertyName,
        IReadOnlyList<string> expectedValuesInOrder)
    {
        _ = GetPublicInstanceProperty(optionsType, propertyName);

        var collected = ResolveConstructorStringLiteralCollectionDefault(optionsType, propertyName);

        if (collected.Count != expectedValuesInOrder.Count)
        {
            throw new InvalidOperationException(
                "SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals: "
                    + $"'{optionsType.FullName}.{propertyName}' default-constructs to a "
                    + $"{collected.Count}-element sequence ([{string.Join(", ", collected)}]), "
                    + $"expected a {expectedValuesInOrder.Count}-element sequence "
                    + $"([{string.Join(", ", expectedValuesInOrder)}]).");
        }

        var mismatches = new List<string>();

        for (var i = 0; i < collected.Count; i++)
        {
            if (!string.Equals(collected[i], expectedValuesInOrder[i], StringComparison.Ordinal))
            {
                mismatches.Add(
                    $"position {i}: actual '{collected[i]}', expected '{expectedValuesInOrder[i]}'");
            }
        }

        if (mismatches.Count > 0)
        {
            throw new InvalidOperationException(
                "SecureDefaultsAssertion.AssertStringCollectionPropertyDefaultEquals: "
                    + $"'{optionsType.FullName}.{propertyName}' default-constructs to "
                    + $"[{string.Join(", ", collected)}], expected "
                    + $"[{string.Join(", ", expectedValuesInOrder)}] — mismatch(es): "
                    + string.Join("; ", mismatches));
        }
    }

    /// <summary>
    /// Asserts that <paramref name="declaringType"/>'s method named <paramref name="methodName"/>
    /// — or a compiler-generated lambda closure it declares, or a sibling overload/helper on the
    /// same <paramref name="declaringType"/> that it delegates to — invokes
    /// <paramref name="calleeDeclaringType"/>'s method named <paramref name="calleeMethodName"/>.
    /// </summary>
    /// <param name="declaringType">
    /// The type declaring the method to inspect (e.g.
    /// <c>typeof(MtlsForwardedHeaderExtensions)</c>).
    /// </param>
    /// <param name="methodName">
    /// The name of the method to inspect (e.g. <c>"AddMtlsForwardedHeaderCertificate"</c>). Must
    /// resolve to exactly one method on <paramref name="declaringType"/> — an overloaded method
    /// name is rejected as ambiguous rather than silently checking only the first match, UNLESS
    /// <paramref name="parameterTypes"/> is supplied to disambiguate (see below).
    /// </param>
    /// <param name="calleeDeclaringType">
    /// The type declaring the expected callee (e.g. <c>typeof(ServiceDefaultsLog)</c>).
    /// </param>
    /// <param name="calleeMethodName">
    /// The expected callee's method name (e.g. <c>"ForwardedHeaderTrustBoundaryUnconfigured"</c>).
    /// </param>
    /// <param name="parameterTypes">
    /// Optional. When <paramref name="methodName"/> resolves to more than one method on
    /// <paramref name="declaringType"/> (a genuine C#-level overload set — e.g. an
    /// <see langword="internal"/> testing-only overload added alongside a pre-existing
    /// <see langword="public"/> one), supply the exact, in-order parameter types of the specific
    /// overload to inspect and the ambiguity is resolved by an exact positional
    /// parameter-type match instead of being rejected. Left <see langword="null"/> (the default)
    /// for every pre-existing call site — a <see langword="null"/> value preserves the original
    /// name-only resolution, including the original ambiguous-name rejection.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="methodName"/> resolves to zero methods on
    /// <paramref name="declaringType"/>; resolves to more than one method and
    /// <paramref name="parameterTypes"/> is <see langword="null"/>; resolves to more than one
    /// method and none (or more than one) matches <paramref name="parameterTypes"/> exactly; or
    /// when neither the resolved method's own body, nor any lambda closure it declares, nor any
    /// sibling method on <paramref name="declaringType"/> it transitively delegates to, contains a
    /// <c>Call</c>/<c>Callvirt</c> instruction targeting
    /// <paramref name="calleeDeclaringType"/>.<paramref name="calleeMethodName"/>.
    /// </exception>
    /// <remarks>
    /// <b>(P-current)</b> Two extensions beyond the original single-name/single-body design, both
    /// proven necessary rather than added speculatively:
    /// <list type="bullet">
    /// <item>
    /// <b>Signature disambiguation.</b> Resolving purely by name breaks the moment a locked method
    /// gains a same-named overload for any reason unrelated to the locked call site (the motivating
    /// case: <c>AesGcmEncryptionService.EncryptToString(string, byte[], Action&lt;byte[]&gt;?)</c>,
    /// an <c>internal</c>, <c>InternalsVisibleTo</c>-gated testing seam
    /// alongside the pre-existing <c>public EncryptToString(string, byte[])</c>). Renaming the
    /// locked production method to satisfy this helper would be the tail wagging the dog — the
    /// helper gained a way to disambiguate instead.
    /// </item>
    /// <item>
    /// <b>Sibling-delegation follow-through.</b> A thin overload that only forwards to a sibling
    /// overload/helper on the SAME <paramref name="declaringType"/> (e.g. the public
    /// <c>EncryptToString(string, byte[])</c> above, whose entire body is
    /// <c>=&gt; EncryptToString(plaintext, associatedData, captureIntermediatePlaintextForTesting: null)</c>)
    /// never contains the eventual callee's <c>Call</c>/<c>Callvirt</c> instruction directly — only
    /// the sibling it forwards to does. Without following that call graph, locking the assertion to
    /// the specific public overload (via <paramref name="parameterTypes"/> above) would report the
    /// guard as unwired even though every real caller of the public entry point genuinely reaches
    /// it. The follow-through is bounded to calls whose declaring type is the SAME
    /// <paramref name="declaringType"/> (never crosses into a different type's implementation, so
    /// it can never be satisfied by an unrelated type happening to also call the guard) and is
    /// guarded against infinite recursion via a visited-method set (self- or mutually-recursive
    /// delegation resolves to "not found" rather than looping).
    /// </item>
    /// </list>
    /// </remarks>
    public static void AssertMethodBodyInvokesMethod(
        Type declaringType,
        string methodName,
        Type calleeDeclaringType,
        string calleeMethodName,
        Type[]? parameterTypes = null)
    {
        using var assemblyDefinition = AssemblyDefinition.ReadAssembly(declaringType.Assembly.Location);
        var typeDefinition = ResolveTypeDefinition(assemblyDefinition.MainModule, declaringType);

        var namedMethods = typeDefinition.Methods.Where(m => m.Name == methodName).ToList();

        if (namedMethods.Count == 0)
        {
            throw new InvalidOperationException(
                "SecureDefaultsAssertion.AssertMethodBodyInvokesMethod found no method named "
                    + $"'{methodName}' on type '{declaringType.FullName}'.");
        }

        MethodDefinition method;

        if (namedMethods.Count == 1)
        {
            method = namedMethods[0];
        }
        else if (parameterTypes is null)
        {
            throw new InvalidOperationException(
                "SecureDefaultsAssertion.AssertMethodBodyInvokesMethod found "
                    + $"{namedMethods.Count} methods named '{methodName}' on type "
                    + $"'{declaringType.FullName}' — ambiguous; this helper requires a uniquely "
                    + "named method, or an explicit parameterTypes argument to disambiguate.");
        }
        else
        {
            var signatureMatches = namedMethods.Where(m => ParametersMatch(m, parameterTypes)).ToList();

            if (signatureMatches.Count != 1)
            {
                throw new InvalidOperationException(
                    "SecureDefaultsAssertion.AssertMethodBodyInvokesMethod found "
                        + $"{namedMethods.Count} methods named '{methodName}' on type "
                        + $"'{declaringType.FullName}', of which {signatureMatches.Count} match the "
                        + $"supplied parameterTypes ({DescribeParameterTypes(parameterTypes)}) — "
                        + "expected exactly one match.");
            }

            method = signatureMatches[0];
        }

        var visited = new HashSet<MethodDefinition>();

        if (MethodBodyInvokes(method, calleeDeclaringType, calleeMethodName, visited))
            return;

        // The direct method body (and any sibling method it transitively delegates to — see
        // MethodBodyInvokes) contains no matching call — check every compiler-generated lambda
        // closure declared inside declaringType. A lambda argument passed to a method call (e.g.
        // the Action<TOptions,TDep> handed to OptionsBuilder<T>.PostConfigure<TDep>) compiles to a
        // method on a nested closure type named "<{methodName}>b__{classIndex}_{lambdaIndex}" —
        // hosted on the shared "<>c" cache type when the lambda captures no outer state, or a
        // per-declaration "<>c__DisplayClassN_M" type when it does. Either way, the enclosing
        // method's own IL contains only a delegate-construction sequence (ldftn/newobj), never
        // the callee call itself, so the call site must be located inside the nested type.
        var lambdaNamePrefix = $"<{methodName}>b__";

        foreach (var nestedType in typeDefinition.NestedTypes)
        {
            foreach (var candidateMethod in nestedType.Methods)
            {
                if (!candidateMethod.Name.StartsWith(lambdaNamePrefix, StringComparison.Ordinal))
                    continue;

                if (MethodBodyInvokes(candidateMethod, calleeDeclaringType, calleeMethodName, visited))
                    return;
            }
        }

        throw new InvalidOperationException(
            "SecureDefaultsAssertion.AssertMethodBodyInvokesMethod: "
                + $"'{declaringType.FullName}.{methodName}' (including any lambda closures or "
                + "same-type sibling delegation it involves) does not call "
                + $"'{calleeDeclaringType.FullName}.{calleeMethodName}'.");
    }

    private static bool ParametersMatch(MethodDefinition method, Type[] parameterTypes)
    {
        if (method.Parameters.Count != parameterTypes.Length)
            return false;

        for (var i = 0; i < parameterTypes.Length; i++)
        {
            if (method.Parameters[i].ParameterType.FullName != CecilStyleFullName(parameterTypes[i]))
                return false;
        }

        return true;
    }

    private static string DescribeParameterTypes(Type[] parameterTypes) =>
        $"[{string.Join(", ", parameterTypes.Select(t => t.Name))}]";

    /// <summary>
    /// Renders <paramref name="type"/> the way Mono.Cecil renders a
    /// <see cref="TypeReference.FullName"/> — used to match a reflection <see cref="Type"/>
    /// supplied by a caller against a Mono.Cecil-resolved parameter type. Handles arrays
    /// (<c>System.Byte[]</c>) and closed generic types (<c>System.Action`1&lt;System.Byte[]&gt;</c>)
    /// in addition to the plain-type case a bare <see cref="Type.FullName"/> already covers.
    /// </summary>
    private static string CecilStyleFullName(Type type)
    {
        if (type.IsArray)
        {
            var elementType = type.GetElementType()
                ?? throw new InvalidOperationException(
                    $"SecureDefaultsAssertion could not resolve the element type of array type "
                        + $"'{type}'.");

            return $"{CecilStyleFullName(elementType)}[]";
        }

        if (type.IsGenericType)
        {
            var genericTypeDefinition = type.GetGenericTypeDefinition();
            var openName = genericTypeDefinition.Namespace is null
                ? genericTypeDefinition.Name
                : $"{genericTypeDefinition.Namespace}.{genericTypeDefinition.Name}";
            var argumentNames = string.Join(",", type.GetGenericArguments().Select(CecilStyleFullName));

            return $"{openName}<{argumentNames}>";
        }

        return type.FullName ?? type.Name;
    }

    private static bool MethodBodyInvokes(
        MethodDefinition method,
        Type calleeDeclaringType,
        string calleeMethodName,
        HashSet<MethodDefinition> visited)
    {
        if (!method.HasBody || !visited.Add(method))
            return false;

        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                continue;

            if (instruction.Operand is not MethodReference calleeReference)
                continue;

            if (calleeReference.Name == calleeMethodName
                && calleeReference.DeclaringType.FullName == calleeDeclaringType.FullName)
            {
                return true;
            }

            // DELEGATING-OVERLOAD FOLLOW-THROUGH (see this method's caller's XML doc remarks): this
            // call site is not the target callee itself, but if it targets a sibling method on the
            // SAME declaring type, that sibling may be the one that actually reaches the target —
            // resolve and recurse into it. Never crosses into a different type's implementation.
            if (calleeReference.DeclaringType.FullName != method.DeclaringType.FullName)
                continue;

            var resolvedSibling = calleeReference.Resolve();

            if (resolvedSibling is not null
                && MethodBodyInvokes(resolvedSibling, calleeDeclaringType, calleeMethodName, visited))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Asserts that <paramref name="declaringType"/>'s method named <paramref name="methodName"/>
    /// — or a compiler-generated lambda closure it declares — constructs and throws
    /// <paramref name="expectedExceptionType"/>.
    /// </summary>
    /// <param name="declaringType">
    /// The type declaring the method to inspect (e.g. the type whose registration method guards a
    /// dangerous configuration by throwing).
    /// </param>
    /// <param name="methodName">
    /// The name of the method to inspect. Must resolve to exactly one method on
    /// <paramref name="declaringType"/> — an overloaded method name is rejected as ambiguous
    /// rather than silently checking only the first match.
    /// </param>
    /// <param name="expectedExceptionType">
    /// The exception type a matching <c>Newobj</c>-then-<c>Throw</c> sequence must construct.
    /// </param>
    /// <remarks>
    /// Introduces this class's METHOD-BODY THROW-PRESENCE ASSERTION technique — a sibling to
    /// <see cref="AssertMethodBodyInvokesMethod"/>'s invocation-presence-assertion technique (both
    /// fail on the ABSENCE of an expected element, not the presence of an unwanted one), but scans
    /// for a <see cref="OpCodes.Newobj"/> instruction constructing <paramref name="expectedExceptionType"/>
    /// followed ANYWHERE LATER in the same method body's instruction stream by a
    /// <see cref="OpCodes.Throw"/> opcode, rather than a <see cref="OpCodes.Call"/>/
    /// <see cref="OpCodes.Callvirt"/> invocation. Reuses <see cref="AssertMethodBodyInvokesMethod"/>'s
    /// closure-method-scanning extension: when the direct method body contains no matching
    /// <c>Newobj</c>-then-<c>Throw</c> sequence, every method on every nested type of
    /// <paramref name="declaringType"/> whose name starts with <c>&lt;{methodName}&gt;b__</c> is
    /// additionally scanned — a startup validation guard is just as plausibly registered via a
    /// <c>PostConfigure</c>/<c>Validate</c>-style lambda as
    /// <c>AddMtlsForwardedHeaderCertificate</c>'s own shape (the motivating case for that
    /// extension) was.
    /// <para>
    /// <strong>Documented limitation</strong> (intentional, consistent with every presence-based
    /// technique in this file): a whole-method-body-plus-closures presence check, not a
    /// reachability/control-flow check tied to the specific dangerous-configuration branch — it
    /// cannot distinguish "throws only when the dangerous combination is detected" from "throws
    /// unconditionally for every configuration" or "throws for an unrelated reason elsewhere in the
    /// same method." This proves "the guard exists and constructs+throws the expected exception
    /// type," not "the guard is provably correct for every input" — the latter remains the
    /// producing domain's own unit-test responsibility, not this one's.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="methodName"/> resolves to zero or more than one method on
    /// <paramref name="declaringType"/>, or when neither the method's own body nor any lambda
    /// closure it declares contains a <c>Newobj</c>-then-<c>Throw</c> sequence constructing
    /// <paramref name="expectedExceptionType"/>.
    /// </exception>
    public static void AssertMethodBodyThrowsExceptionType(
        Type declaringType,
        string methodName,
        Type expectedExceptionType)
    {
        using var assemblyDefinition = AssemblyDefinition.ReadAssembly(declaringType.Assembly.Location);
        var typeDefinition = ResolveTypeDefinition(assemblyDefinition.MainModule, declaringType);

        var matchingMethods = typeDefinition.Methods.Where(m => m.Name == methodName).ToList();

        if (matchingMethods.Count == 0)
        {
            throw new InvalidOperationException(
                "SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType found no method named "
                    + $"'{methodName}' on type '{declaringType.FullName}'.");
        }

        if (matchingMethods.Count > 1)
        {
            throw new InvalidOperationException(
                "SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType found "
                    + $"{matchingMethods.Count} methods named '{methodName}' on type "
                    + $"'{declaringType.FullName}' — ambiguous; this helper requires a uniquely "
                    + "named method.");
        }

        var method = matchingMethods[0];

        if (MethodBodyThrows(method, expectedExceptionType))
            return;

        // The direct method body contains no matching Newobj-then-Throw sequence — check every
        // compiler-generated lambda closure declared inside it, same rationale/naming convention as
        // AssertMethodBodyInvokesMethod.
        var lambdaNamePrefix = $"<{methodName}>b__";

        foreach (var nestedType in typeDefinition.NestedTypes)
        {
            foreach (var candidateMethod in nestedType.Methods)
            {
                if (!candidateMethod.Name.StartsWith(lambdaNamePrefix, StringComparison.Ordinal))
                    continue;

                if (MethodBodyThrows(candidateMethod, expectedExceptionType))
                    return;
            }
        }

        throw new InvalidOperationException(
            "SecureDefaultsAssertion.AssertMethodBodyThrowsExceptionType: "
                + $"'{declaringType.FullName}.{methodName}' (including any lambda closures it "
                + "declares) does not construct and throw "
                + $"'{expectedExceptionType.FullName}'.");
    }

    private static bool MethodBodyThrows(MethodDefinition method, Type expectedExceptionType)
    {
        if (!method.HasBody)
            return false;

        var instructions = method.Body.Instructions;
        var constructedExpectedException = false;

        foreach (var instruction in instructions)
        {
            if (instruction.OpCode == OpCodes.Newobj
                && instruction.Operand is MethodReference constructorReference
                && constructorReference.DeclaringType.FullName == expectedExceptionType.FullName)
            {
                constructedExpectedException = true;
                continue;
            }

            if (constructedExpectedException && instruction.OpCode == OpCodes.Throw)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The two <c>Microsoft.Extensions.DependencyInjection(.Extensions)</c> method names that
    /// register a closed-generic singleton service→implementation pair —
    /// <c>AddSingleton&lt;TService,TImplementation&gt;()</c> and
    /// <c>TryAddSingleton&lt;TService,TImplementation&gt;()</c>. Both are accepted by
    /// <see cref="AssertMethodBodyRegistersSingleton"/> — see that method's remarks for why
    /// neither name alone is sufficient.
    /// </summary>
    private static readonly HashSet<string> SingletonRegistrationMethodNames =
        new(StringComparer.Ordinal) { "AddSingleton", "TryAddSingleton" };

    /// <summary>
    /// Asserts that <paramref name="declaringType"/>'s method named <paramref name="methodName"/>
    /// — or a compiler-generated lambda closure it declares — registers
    /// <paramref name="implementationType"/> as a singleton implementation of
    /// <paramref name="serviceType"/>, via either <c>AddSingleton&lt;TService,TImplementation&gt;()</c>
    /// or <c>TryAddSingleton&lt;TService,TImplementation&gt;()</c>.
    /// </summary>
    /// <param name="declaringType">
    /// The type declaring the method to inspect (e.g. the real, shipped DI extension class
    /// hosting <c>AddSharedKernelWebhooks</c>).
    /// </param>
    /// <param name="methodName">
    /// The name of the method to inspect (e.g. <c>"AddSharedKernelWebhooks"</c>). Must resolve to
    /// exactly one method on <paramref name="declaringType"/> — an overloaded method name is
    /// rejected as ambiguous rather than silently checking only the first match.
    /// </param>
    /// <param name="serviceType">
    /// The expected registered service interface (e.g.
    /// <c>typeof(IWebhookUrlValidator)</c>).
    /// </param>
    /// <param name="implementationType">
    /// The expected registered implementation type (e.g.
    /// <c>typeof(PrivateNetworkWebhookUrlValidator)</c>).
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="methodName"/> resolves to zero or more than one method on
    /// <paramref name="declaringType"/>, or when neither the method's own body nor any lambda
    /// closure it declares registers <paramref name="implementationType"/> as a singleton
    /// implementation of <paramref name="serviceType"/>.
    /// </exception>
    public static void AssertMethodBodyRegistersSingleton(
        Type declaringType,
        string methodName,
        Type serviceType,
        Type implementationType)
    {
        using var assemblyDefinition = AssemblyDefinition.ReadAssembly(declaringType.Assembly.Location);
        var typeDefinition = ResolveTypeDefinition(assemblyDefinition.MainModule, declaringType);

        var matchingMethods = typeDefinition.Methods.Where(m => m.Name == methodName).ToList();

        if (matchingMethods.Count == 0)
        {
            throw new InvalidOperationException(
                "SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton found no method named "
                    + $"'{methodName}' on type '{declaringType.FullName}'.");
        }

        if (matchingMethods.Count > 1)
        {
            throw new InvalidOperationException(
                "SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton found "
                    + $"{matchingMethods.Count} methods named '{methodName}' on type "
                    + $"'{declaringType.FullName}' — ambiguous; this helper requires a uniquely "
                    + "named method.");
        }

        var method = matchingMethods[0];

        if (MethodBodyRegistersSingleton(method, serviceType, implementationType))
            return;

        // The direct method body contains no matching registration — check every
        // compiler-generated lambda closure declared inside it, same rationale/naming convention
        // as AssertMethodBodyInvokesMethod/AssertMethodBodyThrowsExceptionType.
        var lambdaNamePrefix = $"<{methodName}>b__";

        foreach (var nestedType in typeDefinition.NestedTypes)
        {
            foreach (var candidateMethod in nestedType.Methods)
            {
                if (!candidateMethod.Name.StartsWith(lambdaNamePrefix, StringComparison.Ordinal))
                    continue;

                if (MethodBodyRegistersSingleton(candidateMethod, serviceType, implementationType))
                    return;
            }
        }

        throw new InvalidOperationException(
            "SecureDefaultsAssertion.AssertMethodBodyRegistersSingleton: "
                + $"'{declaringType.FullName}.{methodName}' (including any lambda closures it "
                + $"declares) does not register '{implementationType.FullName}' as a singleton "
                + $"implementation of '{serviceType.FullName}' (via AddSingleton or "
                + "TryAddSingleton).");
    }

    private static bool MethodBodyRegistersSingleton(
        MethodDefinition method,
        Type serviceType,
        Type implementationType)
    {
        if (!method.HasBody)
            return false;

        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                continue;

            if (instruction.Operand is not GenericInstanceMethod genericInstanceMethod)
                continue;

            if (!SingletonRegistrationMethodNames.Contains(genericInstanceMethod.ElementMethod.Name))
                continue;

            var genericArguments = genericInstanceMethod.GenericArguments;

            if (genericArguments.Count != 2)
                continue;

            if (
                genericArguments[0].FullName == serviceType.FullName
                && genericArguments[1].FullName == implementationType.FullName
            )
            {
                return true;
            }
        }

        return false;
    }

    private static PropertyInfo GetPublicInstanceProperty(Type optionsType, string propertyName) =>
        optionsType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "SecureDefaultsAssertion could not find a public instance property named "
                    + $"'{propertyName}' on type '{optionsType.FullName}'.");

    private static int? ResolveConstructorInt32BackingFieldDefault(Type optionsType, string propertyName)
    {
        using var assemblyDefinition = AssemblyDefinition.ReadAssembly(optionsType.Assembly.Location);
        var typeDefinition = ResolveTypeDefinition(assemblyDefinition.MainModule, optionsType);
        var ctor = FindParameterlessConstructor(typeDefinition);

        if (ctor?.Body is null)
            return null;

        var backingFieldName = BackingFieldName(propertyName);
        var instructions = ctor.Body.Instructions;

        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];

            if (
                instruction.OpCode != OpCodes.Stfld
                || instruction.Operand is not FieldReference fieldReference
                || fieldReference.Name != backingFieldName
            )
            {
                continue;
            }

            if (i == 0)
                return null;

            return TryGetLdcI4Value(instructions[i - 1], out var value) ? value : null;
        }

        return null;
    }

    private static IReadOnlyList<string> ResolveConstructorStringLiteralCollectionDefault(
        Type optionsType,
        string propertyName)
    {
        using var assemblyDefinition = AssemblyDefinition.ReadAssembly(optionsType.Assembly.Location);
        var typeDefinition = ResolveTypeDefinition(assemblyDefinition.MainModule, optionsType);
        var ctor = FindParameterlessConstructor(typeDefinition);

        if (ctor?.Body is null)
            return Array.Empty<string>();

        var backingFieldName = BackingFieldName(propertyName);
        var instructions = ctor.Body.Instructions;

        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];

            if (
                instruction.OpCode != OpCodes.Stfld
                || instruction.Operand is not FieldReference fieldReference
                || fieldReference.Name != backingFieldName
            )
            {
                continue;
            }

            var literals = new List<string>();

            for (var j = i - 1; j >= 0; j--)
            {
                var preceding = instructions[j];

                // A preceding Stfld/Stsfld marks the tail of the PREVIOUS property/field
                // initializer segment — stop before crossing into it, so a different property's
                // string-collection default is never attributed to this one.
                if (preceding.OpCode == OpCodes.Stfld || preceding.OpCode == OpCodes.Stsfld)
                    break;

                if (preceding.OpCode == OpCodes.Ldstr && preceding.Operand is string literal)
                    literals.Add(literal);
            }

            literals.Reverse();
            return literals;
        }

        return Array.Empty<string>();
    }

    private static bool TryGetLdcI4Value(Instruction instruction, out int value)
    {
        if (instruction.OpCode == OpCodes.Ldc_I4 && instruction.Operand is int operandValue)
        {
            value = operandValue;
            return true;
        }

        if (instruction.OpCode == OpCodes.Ldc_I4_S && instruction.Operand is sbyte sbyteOperandValue)
        {
            value = sbyteOperandValue;
            return true;
        }

        int? shortFormValue = instruction.OpCode.Code switch
        {
            Code.Ldc_I4_M1 => -1,
            Code.Ldc_I4_0 => 0,
            Code.Ldc_I4_1 => 1,
            Code.Ldc_I4_2 => 2,
            Code.Ldc_I4_3 => 3,
            Code.Ldc_I4_4 => 4,
            Code.Ldc_I4_5 => 5,
            Code.Ldc_I4_6 => 6,
            Code.Ldc_I4_7 => 7,
            Code.Ldc_I4_8 => 8,
            _ => null,
        };

        if (shortFormValue is not null)
        {
            value = shortFormValue.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static string BackingFieldName(string propertyName) => $"<{propertyName}>k__BackingField";

    private static MethodDefinition? FindParameterlessConstructor(TypeDefinition type) =>
        type.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);

    /// <summary>
    /// Resolves <paramref name="reflectionType"/>'s Mono.Cecil <see cref="TypeDefinition"/> inside
    /// <paramref name="module"/> by walking the declaring-type chain and matching on simple names
    /// at each level, rather than comparing <see cref="Type.FullName"/> against
    /// <c>TypeDefinition.FullName</c> directly — .NET reflection separates a nested type's
    /// declaring type with <c>+</c> (e.g. <c>Outer+Inner</c>) while Mono.Cecil separates it with
    /// <c>/</c> (e.g. <c>Outer/Inner</c>), so a direct string comparison would silently fail to
    /// resolve a nested options type such as <c>SecurityOptions.JwtOptions</c>.
    /// </summary>
    private static TypeDefinition ResolveTypeDefinition(ModuleDefinition module, Type reflectionType) =>
        FindTypeDefinitionRecursive(module, reflectionType)
        ?? throw new InvalidOperationException(
            $"SecureDefaultsAssertion could not resolve a Mono.Cecil TypeDefinition for "
                + $"'{reflectionType.FullName}' in module '{module.Name}'.");

    private static TypeDefinition? FindTypeDefinitionRecursive(ModuleDefinition module, Type reflectionType)
    {
        if (reflectionType.DeclaringType is null)
        {
            var fullName = string.IsNullOrEmpty(reflectionType.Namespace)
                ? reflectionType.Name
                : $"{reflectionType.Namespace}.{reflectionType.Name}";

            return EnumerateAllTypes(module.Types).FirstOrDefault(t => t.FullName == fullName);
        }

        var declaringTypeDefinition = FindTypeDefinitionRecursive(module, reflectionType.DeclaringType);

        return declaringTypeDefinition?.NestedTypes.FirstOrDefault(nested =>
            nested.Name == reflectionType.Name);
    }

    private static IEnumerable<TypeDefinition> EnumerateAllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;

            foreach (var nested in EnumerateAllTypes(type.NestedTypes))
                yield return nested;
        }
    }
}
