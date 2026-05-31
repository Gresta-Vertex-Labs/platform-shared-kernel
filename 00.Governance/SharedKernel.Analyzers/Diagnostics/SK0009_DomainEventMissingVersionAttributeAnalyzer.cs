using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0009 — Fires when a non-abstract class or record that declares <c>IDomainEvent</c>
/// in its base list does not carry a <c>[DomainEventVersion]</c> attribute.
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
/// (<c>RecordDeclarationSyntax</c>) are handled. Simple name matching is used throughout;
/// no semantic model is required.
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
