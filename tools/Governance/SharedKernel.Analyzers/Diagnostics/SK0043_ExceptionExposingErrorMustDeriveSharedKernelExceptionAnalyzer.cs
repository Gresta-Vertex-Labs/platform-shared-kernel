using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0043 — an exception that exposes a structured <c>Error</c> must derive <c>SharedKernelException</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why.</strong> The edges (HTTP, gRPC, SignalR, GraphQL, message consumers) classify a failure by reading the
/// <c>Error</c> a <c>SharedKernelException</c> carries. An exception that carries its own <c>Error</c> but does not derive
/// that base is invisible to them: it maps to a 500 and its stable code never reaches a caller or a dashboard. Deriving
/// the base is the one way to be classified; the constructors <c>(Error)</c> and <c>(Error, Exception)</c> are the
/// guidance, not a condition (a base-type check only; constructor shape is never inspected).
/// </para>
/// <para>
/// <strong>Trigger.</strong> A named type that derives <c>System.Exception</c> and (a) declares or inherits a non-private,
/// non-static property whose type is <c>SharedKernel.Primitives.Errors.Error</c>, or (b) implements
/// <c>SharedKernel.Primitives.Errors.IHasError</c>, and has no base type with the metadata name
/// <c>SharedKernel.Core.Exceptions.SharedKernelException</c>. <c>SharedKernelException</c> itself is exempt.
/// </para>
/// <para>
/// <strong>Matching.</strong> By metadata name through the semantic model (the analyzer references no kernel assembly).
/// Any accessibility, and no namespace exemption: a consuming service's own exception benefits from the same rule.
/// Exceptions that carry no <c>Error</c> at all are out of scope.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExceptionExposingErrorMustDeriveSharedKernelExceptionAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0043";

    private const string ExceptionMetadataName = "System.Exception";
    private const string ErrorMetadataName = "SharedKernel.Primitives.Errors.Error";
    private const string HasErrorMetadataName = "SharedKernel.Primitives.Errors.IHasError";
    private const string SharedKernelExceptionMetadataName = "SharedKernel.Core.Exceptions.SharedKernelException";

    /// <summary>The diagnostic descriptor for SK0043.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Exception exposing an Error must derive SharedKernelException",
        messageFormat: "Exception '{0}' exposes a structured Error but does not derive SharedKernelException, so the edges map "
            + "it to a 500 and its code is lost. Derive SharedKernel.Core.Exceptions.SharedKernelException and give it the "
            + "constructors (Error) and (Error, Exception).",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0043-exceptionexposingerrormustderivesharedkernelexception"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind != TypeKind.Class || !DerivesFrom(type, ExceptionMetadataName))
            return;

        if (GetMetadataName(type) == SharedKernelExceptionMetadataName || DerivesFrom(type, SharedKernelExceptionMetadataName))
            return;

        if (!ImplementsHasError(type) && !ExposesErrorProperty(type))
            return;

        var location = type.Locations.FirstOrDefault(static l => l.IsInSource);
        if (location is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
    }

    private static bool ImplementsHasError(INamedTypeSymbol type)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (GetMetadataName(iface) == HasErrorMetadataName)
                return true;
        }

        return false;
    }

    private static bool ExposesErrorProperty(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (
                    member is IPropertySymbol { IsStatic: false } property
                    && property.DeclaredAccessibility != Accessibility.Private
                    && GetMetadataName(property.Type) == ErrorMetadataName
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, string metadataName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (GetMetadataName(current) == metadataName)
                return true;
        }

        return false;
    }

    private static string GetMetadataName(ITypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns ? $"{ns.ToDisplayString()}.{type.MetadataName}" : type.MetadataName;
}
