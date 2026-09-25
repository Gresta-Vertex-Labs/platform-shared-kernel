using Microsoft.CodeAnalysis;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// Shared semantic-model interface-closure matching helper reused by SK0017 and SK0018.
/// </summary>
/// <remarks>
/// Interface matching uses <see cref="INamedTypeSymbol.OriginalDefinition"/> (so both open-generic
/// interfaces such as <c>ICacheableQuery&lt;TResponse&gt;</c> and non-generic markers such as
/// <c>ICommandBase</c> are matched uniformly by simple name plus arity) combined with a
/// containing-namespace prefix check. The prefix check is deliberately loose (a
/// <c>"SharedKernel.Application"</c> prefix rather than an exact assembly identity comparison) so
/// analyzer test fixtures can declare a fixture-local marker interface inside a matching-namespace
/// code block within the same compilation, with no <c>ProjectReference</c> to the real
/// <c>SharedKernel.Application</c>/<c>SharedKernel.Application.Pipeline</c> assemblies required.
/// </remarks>
internal static class MarkerInterfaceHelpers
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/>'s full interface closure
    /// (<see cref="INamedTypeSymbol.AllInterfaces"/>) contains an interface whose
    /// <see cref="INamedTypeSymbol.OriginalDefinition"/> simple name is <paramref name="simpleName"/>,
    /// whose arity matches <paramref name="arity"/>, and whose containing namespace starts with
    /// <paramref name="namespacePrefix"/>.
    /// </summary>
    public static bool HasInterface(
        INamedTypeSymbol type,
        string simpleName,
        int arity,
        string namespacePrefix
    )
    {
        foreach (var iface in type.AllInterfaces)
        {
            var original = iface.OriginalDefinition;

            if (original.Arity != arity || original.Name != simpleName)
                continue;

            if (HasNamespacePrefix(original, namespacePrefix))
                return true;
        }

        return false;
    }

    private static bool HasNamespacePrefix(INamedTypeSymbol type, string namespacePrefix)
    {
        var ns = type.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
            return false;

        return ns.ToDisplayString().StartsWith(namespacePrefix, StringComparison.Ordinal);
    }
}
