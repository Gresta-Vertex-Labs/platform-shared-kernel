using Microsoft.CodeAnalysis;

namespace SharedKernel.Presentation.WebApi.Generators;

/// <summary>
/// The endpoint-module generator's diagnostics. The <c>SKEP</c> prefix keeps them apart from 00.Governance's
/// <c>SK</c>-numbered analyzer register: they ship inside the WebApi package, not the analyzer package.
/// </summary>
internal static class EndpointModuleDiagnostics
{
    private const string Category = "SharedKernel.Presentation.EndpointModules";

    public static readonly DiagnosticDescriptor AbstractModule = new(
        id: "SKEP001",
        title: "An endpoint module cannot be abstract",
        messageFormat: "Endpoint module '{0}' is abstract, so MapEndpoints() does not map it. Make it a concrete type, or remove IEndpointModule from it and implement the interface on the concrete modules.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "MapEndpoints() calls Map on every concrete module of the assembly. An abstract type is a base for other types, and mapping it would map its routes once for itself and again for every module that derives from it.");

    public static readonly DiagnosticDescriptor GenericModule = new(
        id: "SKEP002",
        title: "An endpoint module cannot be generic",
        messageFormat: "Endpoint module '{0}' {1}, so MapEndpoints() cannot call its Map without a type argument. Declare the module as a non-generic type.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "MapEndpoints() calls Map on each module by name, which needs a closed, non-generic type that is not nested in a generic type.");

    public static readonly DiagnosticDescriptor InaccessibleModule = new(
        id: "SKEP003",
        title: "An endpoint module must be reachable from the assembly",
        messageFormat: "Endpoint module '{0}' cannot be reached by the generated MapEndpoints(): {1}. Make the module, and every type it is nested in, public or internal.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "MapEndpoints() is generated as a separate internal class in the same assembly, so it can call only modules that are public or internal and nested only in public or internal types.");

    public static readonly DiagnosticDescriptor InheritedMap = new(
        id: "SKEP004",
        title: "An endpoint module inherits Map from another module",
        messageFormat: "'{0}' inherits Map from the endpoint module '{1}', so MapEndpoints() does not map it a second time. Declare its own static Map to map it as a module.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Calling Map on a type that inherits it would run the base module's Map again and map the same routes twice.");
}
