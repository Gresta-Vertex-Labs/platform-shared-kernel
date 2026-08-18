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
/// anti-pattern a Roslyn analyzer could target — the motivating P-385/P-386 defect
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
/// <strong>Real-assembly status.</strong> Confirmed on disk at implementation time (2026-08-18):
/// <c>12.Security</c> shipped its full WO-060 scope before this phase's implementation session —
/// <c>SharedKernel.Security.Mtls</c>'s <c>MtlsAuthenticationOptions.AllowedCertificateTypes</c>
/// defaults to <c>CertificateTypes.Chained</c> and <c>.RevocationMode</c> defaults to
/// <c>X509RevocationMode.Offline</c> (C-39); <c>SharedKernel.Security.Oidc</c>'s
/// <c>SecurityOptions.JwtOptions.ValidAlgorithms</c> and <c>DpopOptions.ValidAlgorithms</c> both
/// default to <c>["PS256", "ES256"]</c> (C-40/C-41). Both real-assembly checks are wired directly
/// in <c>SecureDefaultsAssertionTests</c> as GATING tests (T-309/T-310) rather than deferred as a
/// Cross-Domain Dependency follow-up, mirroring the
/// <c>SK.00.SenderConstrainedCredentialGuard</c>/<c>SK.00.StorageTopology</c>/
/// <c>SK.00.SearchTopology</c> precedent for this exact class of dependency-resolved-before-
/// implementation finding — this phase's own authoring-time prose and Cross-Domain Dependencies
/// section were stale.
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
    /// <see cref="TypeDefinition.FullName"/> directly — .NET reflection separates a nested type's
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
