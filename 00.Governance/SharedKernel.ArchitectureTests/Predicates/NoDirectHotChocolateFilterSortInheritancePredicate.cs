using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type outside the
/// <c>SharedKernel.Communication.GraphQL</c> namespace that directly inherits from
/// <c>FilterInputType</c> or <c>SortInputType</c> (HotChocolate) without having
/// <c>FilterBase</c> or <c>SortBase</c> from <c>SharedKernel.Communication.GraphQL</c>
/// in its base type chain first.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.CommunicationLayeringRules"/> to enforce that all HotChocolate
/// filter and sort types in service assemblies extend <c>FilterBase&lt;T&gt;</c> or
/// <c>SortBase&lt;T&gt;</c>, which are thin platform wrappers that apply the snake_case naming
/// convention, allowed-field subset, and pagination shape automatically. Directly inheriting
/// <c>FilterInputType&lt;T&gt;</c> or <c>SortInputType&lt;T&gt;</c> exposes the full entity
/// field surface to GraphQL clients and bypasses platform access-control conventions.
/// </para>
/// <para>
/// <strong>Namespace exemption:</strong> types whose
/// <see cref="TypeDefinition.Namespace"/> starts with
/// <c>"SharedKernel.Communication.GraphQL"</c> return <see langword="true"/> unconditionally —
/// the platform GraphQL package is where <c>FilterBase&lt;T&gt;</c> and
/// <c>SortBase&lt;T&gt;</c> are defined and legitimately inherit from the HotChocolate base
/// types.
/// </para>
/// <para>
/// <strong>Detection:</strong> walks the <see cref="TypeDefinition.BaseType"/> chain
/// iteratively, evaluating each step:
/// <list type="bullet">
///   <item>
///     <description>
///       If a type whose name starts with <c>"FilterBase"</c> or <c>"SortBase"</c> is
///       encountered <em>before</em> any forbidden HotChocolate type, the type is compliant —
///       returns <see langword="true"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       If a type whose name starts with <c>"FilterInputType"</c> or
///       <c>"SortInputType"</c> is encountered <em>without</em> a preceding platform wrapper,
///       returns <see langword="false"/> (rule violated).
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>StartsWith</c> is used instead of exact match to handle generic IL names such as
///       <c>"FilterInputType`1"</c>, <c>"FilterBase`1"</c>, etc.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <strong>Fail-open policy:</strong> if <c>BaseType.Resolve()</c> returns
/// <see langword="null"/> at any step (the base type lives in an assembly that was not loaded),
/// the predicate returns <see langword="true"/> to avoid false positives.
/// </para>
/// <para>
/// <strong>Failure message:</strong>
/// <c>"{FullName} inherits from {FilterInputType/SortInputType} directly. Use FilterBase&lt;T&gt;
/// or SortBase&lt;T&gt; from SharedKernel.Communication.GraphQL to apply platform naming and
/// exposure conventions."</c>
/// </para>
/// </remarks>
public sealed class NoDirectHotChocolateFilterSortInheritancePredicate : ICustomRule
{
    private const string ExemptedNamespacePrefix = "SharedKernel.Communication.GraphQL";
    private const string ObjectTypeName = "Object";

    // Forbidden HotChocolate base-type name prefixes
    private const string FilterInputTypePrefix = "FilterInputType";
    private const string SortInputTypePrefix = "SortInputType";

    // Platform-wrapper base-type name prefixes (signal compliance)
    private const string FilterBasePrefix = "FilterBase";
    private const string SortBasePrefix = "SortBase";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when the type is exempt or its base type chain
    /// reaches a platform wrapper (<c>FilterBase</c> or <c>SortBase</c>) before encountering a
    /// raw HotChocolate type (<c>FilterInputType</c> or <c>SortInputType</c>);
    /// <see langword="false"/> when the chain reaches a HotChocolate base without a preceding
    /// platform wrapper.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when the type (outside <c>SharedKernel.Communication.GraphQL</c>)
    /// directly or indirectly inherits from <c>FilterInputType</c> or <c>SortInputType</c>
    /// without going through <c>FilterBase</c> or <c>SortBase</c> first;
    /// <see langword="true"/> otherwise, including the fail-open case.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Namespace exemption — types inside SharedKernel.Communication.GraphQL are always permitted.
        if (type.Namespace is not null &&
            type.Namespace.StartsWith(ExemptedNamespacePrefix, System.StringComparison.Ordinal))
        {
            return true;
        }

        var baseType = type.BaseType;

        while (baseType is not null && baseType.Name != ObjectTypeName)
        {
            if (IsPlatformWrapper(baseType.Name))
                return true; // compliant — platform wrapper encountered before forbidden type

            if (IsForbiddenHotChocolateBase(baseType.Name))
                return false; // violation — direct HotChocolate base without platform wrapper

            var resolved = baseType.Resolve();
            if (resolved is null)
                return true; // fail-open: unresolved assembly dependency

            baseType = resolved.BaseType;
        }

        return true;
    }

    private static bool IsPlatformWrapper(string typeName)
    {
        return typeName.StartsWith(FilterBasePrefix, System.StringComparison.Ordinal) ||
               typeName.StartsWith(SortBasePrefix, System.StringComparison.Ordinal);
    }

    private static bool IsForbiddenHotChocolateBase(string typeName)
    {
        return typeName.StartsWith(FilterInputTypePrefix, System.StringComparison.Ordinal) ||
               typeName.StartsWith(SortInputTypePrefix, System.StringComparison.Ordinal);
    }
}
