using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0009 — Fires when a non-<see langword="abstract"/> <see cref="ClassDeclarationSyntax"/> or
/// <see cref="RecordDeclarationSyntax"/> declares a base-list entry simply named
/// <c>IDomainEvent</c> and carries no attribute simply named <c>DomainEventVersion</c> or
/// <c>DomainEventVersionAttribute</c>.
/// </summary>
/// <remarks>
/// <para>
/// Schema versioning discipline is required for all concrete domain event types. Versioning
/// allows infrastructure (messaging, outbox) to route to the correct deserializer on schema
/// changes. Abstract types are exempt — abstract base event classes do not need a version
/// attribute.
/// </para>
/// <para>
/// Both the class name check (<c>ClassDeclarationSyntax</c>) and the record check
/// (<c>RecordDeclarationSyntax</c>) are handled by the same <c>AnalyzeTypeDeclaration</c> callback,
/// registered against both <see cref="SyntaxKind.ClassDeclaration"/> and
/// <see cref="SyntaxKind.RecordDeclaration"/>. Simple name matching is used throughout for both the
/// base-list interface check and the attribute check; no semantic model is required anywhere in
/// this rule, so a test fixture (or a consuming service) can declare its own local
/// <c>IDomainEvent</c> interface and <c>DomainEventVersionAttribute</c> with no reference to the
/// real <c>SharedKernel.Primitives</c>/<c>SharedKernel.Domain</c> assemblies.
/// </para>
/// <para>
/// <strong>Abstract exemption.</strong> Checked first, via
/// <c>typeDecl.Modifiers.Any(SyntaxKind.AbstractKeyword)</c> — an abstract type implementing
/// <c>IDomainEvent</c> short-circuits before either the base-list or attribute check runs, so an
/// abstract base event class never needs the attribute, regardless of whether any of its concrete
/// subclasses declare it either (each concrete subclass is still independently checked on its own
/// merits).
/// </para>
/// <para>
/// <strong>Attribute-name flexibility.</strong> Both the short form (<c>[DomainEventVersion(1)]</c>)
/// and the fully-suffixed form (<c>[DomainEventVersionAttribute(1)]</c>) are accepted as satisfying
/// the rule — C#'s own attribute-name-suffix convention is honored on the checking side without
/// requiring the consumer to spell out <c>Attribute</c> explicitly.
/// </para>
/// <para>
/// <strong>Pass case:</strong> a type that does not implement <c>IDomainEvent</c> at all is never
/// flagged, regardless of whether it happens to carry a <c>[DomainEventVersion]</c> attribute — the
/// base-list check gates everything else.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DomainEventMissingVersionAttributeAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0009";

    private const string IDomainEventSimpleName = "IDomainEvent";
    private const string DomainEventVersionAttributeName = "DomainEventVersion";
    private const string DomainEventVersionAttributeFullName = "DomainEventVersionAttribute";

    /// <summary>The diagnostic descriptor for SK0009.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Domain event missing version attribute",
        messageFormat: "Type '{0}' implements IDomainEvent but is missing the [DomainEventVersion] attribute — add [DomainEventVersion(N)] to declare the schema version",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0009-domaineventmissingversionattribute"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeTypeDeclaration,
            SyntaxKind.ClassDeclaration
        );

        context.RegisterSyntaxNodeAction(
            AnalyzeTypeDeclaration,
            SyntaxKind.RecordDeclaration
        );
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;

        // Skip abstract types — abstract bases are exempt
        if (typeDecl.Modifiers.Any(SyntaxKind.AbstractKeyword))
            return;

        // Check whether the type implements IDomainEvent via base list
        if (!ImplementsIDomainEvent(typeDecl))
            return;

        // Check whether [DomainEventVersion] attribute is present
        if (HasDomainEventVersionAttribute(typeDecl))
            return;

        var typeName = typeDecl.Identifier.Text;
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, typeDecl.Identifier.GetLocation(), typeName)
        );
    }

    private static bool ImplementsIDomainEvent(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl.BaseList is null)
            return false;

        foreach (var baseType in typeDecl.BaseList.Types)
        {
            var typeName = GetSimpleTypeName(baseType.Type);
            if (typeName == IDomainEventSimpleName)
                return true;
        }

        return false;
    }

    private static bool HasDomainEventVersionAttribute(TypeDeclarationSyntax typeDecl)
    {
        foreach (var attributeList in typeDecl.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attrName = GetSimpleTypeName(attribute.Name);
                if (attrName == DomainEventVersionAttributeName ||
                    attrName == DomainEventVersionAttributeFullName)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetSimpleTypeName(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            NullableTypeSyntax nullable => GetSimpleTypeName(nullable.ElementType),
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => typeSyntax.ToString(),
        };
    }

    private static string GetSimpleTypeName(NameSyntax nameSyntax)
    {
        return nameSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => nameSyntax.ToString(),
        };
    }
}
