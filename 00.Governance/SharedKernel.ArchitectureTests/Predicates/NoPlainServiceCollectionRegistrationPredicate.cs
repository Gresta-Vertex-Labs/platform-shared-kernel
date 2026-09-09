using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (no SK diagnostic ID) that fails any type whose method bodies
/// contain a <c>Call</c>/<c>Callvirt</c> IL instruction invoking
/// <c>Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions</c>'s plain
/// <c>AddSingleton</c>, <c>AddScoped</c>, or <c>AddTransient</c> — the "always registers, even if
/// something already registered this service type" verbs that <c>01.Core</c>'s own DI extension
/// methods standardized away from, in favor of
/// <c>Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions</c>'s
/// <c>TryAddSingleton</c>/<c>TryAddScoped</c>/<c>TryAddTransient</c>/<c>TryAddEnumerable</c>
/// (P-518/WO-083).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Motivating defect (P-518/WO-083).</strong> Before that phase, roughly two-thirds of
/// <c>01.Core</c>'s own registration call sites used the plain <c>Add*</c> verb. This meant
/// <c>AddSharedKernelCryptography()</c> called twice (a realistic shape for a consuming service
/// composed from more than one internal extension method, each of which happens to also pull in
/// cryptography) silently double-registered every one of its nine services, and a consumer
/// registering its own <c>ISymmetricEncryptionService</c> implementation BEFORE calling
/// <c>AddSharedKernelCryptography()</c> would have that registration silently overwritten instead
/// of honored — the opposite of the "first registration wins" override convention library code is
/// expected to respect. <c>TryAdd*</c> makes both failure modes structurally impossible: it is a
/// no-op whenever a registration for the exact service type already exists.
/// </para>
/// <para>
/// <strong>Name-only detection, deliberately not also declaring-type-narrowed to the two
/// candidate detection strategies considered.</strong> <c>AddSingleton</c>/<c>AddScoped</c>/
/// <c>AddTransient</c> and their <c>TryAdd*</c> counterparts are entirely disjoint method names —
/// no BCL overload of either family ever shares a name with the other — so matching on
/// <see cref="MemberReference.Name"/> alone (mirroring
/// <see cref="NoSecurityContextSingletonRegistrationPredicate"/>'s "AddSingleton" name-only match)
/// would already be unambiguous. This predicate ADDITIONALLY requires the resolved callee's
/// <see cref="MemberReference.DeclaringType"/> to equal the real, well-known BCL type
/// (<c>Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions</c>, confirmed
/// via direct reflection against the platform's own pinned
/// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> package version at authoring
/// time) — the extra precision costs nothing (both facts are already on the same resolved
/// <see cref="MethodReference"/> operand) and forecloses the theoretical false positive of some
/// unrelated type in a future <c>01.Core</c> package declaring its own, unrelated method that
/// happens to also be named <c>AddSingleton</c>.
/// </para>
/// <para>
/// <strong>Non-generic overload note (documented limitation, consistent with this assembly's other
/// call-site-detection techniques — see <see cref="NoSecurityContextSingletonRegistrationPredicate"/>'s
/// own equivalent note).</strong> Every real, shipped <c>01.Core</c> registration call site uses
/// the closed-generic <c>AddX&lt;TService[,TImplementation]&gt;(...)</c>/
/// <c>TryAddX&lt;TService[,TImplementation]&gt;(...)</c> shape. The non-generic
/// <c>Add(Type,Type)</c>/<c>Add(Type,Func&lt;IServiceProvider,object&gt;)</c> overloads compile to
/// the identical <c>Call</c>-to-a-<see cref="MethodReference"/>-named-"AddSingleton" IL shape this
/// predicate already matches (a closed-generic method's operand is a
/// <see cref="Mono.Cecil.GenericInstanceMethod"/>, itself a <see cref="MethodReference"/> whose
/// <see cref="MemberReference.Name"/>/<see cref="MemberReference.DeclaringType"/> resolve exactly
/// like the non-generic overload's) — so both shapes are already covered without special-casing
/// either.
/// </para>
/// <para>
/// <strong>Scope (P-523/WO-083).</strong> Scoped to <c>01.Core</c>'s own DI extension methods for
/// this phase — the caller supplies exactly the <c>01.Core</c> assemblies to scan via
/// <see cref="Rules.CoreArchitectureRules.DiExtensionsUseTryAddRegistrationConvention"/>. Extending
/// the identical predicate to every other domain's own DI extension methods is a documented future
/// follow-up, not attempted here; nothing about this predicate itself is <c>01.Core</c>-specific.
/// </para>
/// <para>
/// <strong>No exemption.</strong> Every <c>01.Core</c> DI extension method's own service
/// registrations must use <c>TryAdd*</c>/<c>TryAddEnumerable</c> — including a legitimate
/// multi-implementation collection registration (e.g.
/// <c>SharedKernel.Validation.AddNationalIdValidator&lt;TValidator&gt;()</c>'s
/// <c>TryAddEnumerable</c>), which this predicate already accepts since <c>TryAddEnumerable</c> is
/// not in the forbidden name set. A call into a THIRD-PARTY package's own registration entry point
/// (e.g. <c>SharedKernel.FeatureManagement</c>'s deliberate, untouched
/// <c>Microsoft.FeatureManagement.ServiceCollectionExtensions.AddFeatureManagement(...)</c> call)
/// is likewise never matched — its declaring type is that third-party package's own extension
/// class, not <c>ServiceCollectionServiceExtensions</c>, and its method name
/// (<c>"AddFeatureManagement"</c>) is not in the forbidden set either.
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
public sealed class NoPlainServiceCollectionRegistrationPredicate : ICustomRule
{
    private const string ServiceCollectionServiceExtensionsFullName =
        "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions";

    private static readonly HashSet<string> ForbiddenMethodNames = new(StringComparer.Ordinal)
    {
        "AddSingleton",
        "AddScoped",
        "AddTransient",
    };

    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when any method body declared on
    /// <paramref name="type"/> contains a <c>Call</c>/<c>Callvirt</c> instruction targeting a
    /// forbidden plain registration verb declared on
    /// <c>ServiceCollectionServiceExtensions</c>; <see langword="true"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a matching forbidden registration call is found in any method
    /// body of <paramref name="type"/>; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                    continue;

                if (instruction.Operand is not MethodReference calleeReference)
                    continue;

                if (!ForbiddenMethodNames.Contains(calleeReference.Name))
                    continue;

                if (calleeReference.DeclaringType.FullName == ServiceCollectionServiceExtensionsFullName)
                    return false;
            }
        }

        return true;
    }
}
