using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0007 — Fires when a <see cref="TypeSyntax"/> whose simple name is
/// <c>IRedisChannelService</c> (declared in <c>SharedKernel.Caching.Redis.PubSub</c>; matched
/// whether written bare, namespace-qualified, or <c>global::</c>-qualified) appears as a
/// constructor parameter, field declaration, or property
/// declaration inside a <see cref="ClassDeclarationSyntax"/> whose own name, or any enclosing
/// namespace identifier, contains one of the case-sensitive substrings <c>Command</c>,
/// <c>Event</c>, <c>DomainEvent</c>, or <c>IntegrationEvent</c>.
/// </summary>
/// <remarks>
/// <para>
/// Signals inappropriate use of Redis pub/sub as a substitute for the durable
/// <c>IMessageBus</c> from <c>SharedKernel.Messaging.Abstractions</c>. Redis pub/sub is
/// ephemeral and non-durable — it must not be used for commands, domain events, or integration
/// events that require guaranteed delivery.
/// </para>
/// <para>
/// <strong>Registration and scope.</strong> Registers only on
/// <see cref="SyntaxKind.ClassDeclaration"/> — a record or struct with the same shape is out of
/// scope. Inside a matched class, all three declaration shapes (constructor parameters, fields,
/// properties) are inspected independently and each reports its own diagnostic location, so a
/// class with e.g. two offending constructor parameters produces two diagnostics.
/// </para>
/// <para>
/// <strong>Syntax-only, substring-based context match.</strong> Both the "is this class in a
/// messaging context" check and the "is this type IRedisChannelService" check are pure text/name
/// comparisons — no <see cref="SemanticModel"/> is consulted anywhere in this rule. The context
/// check in particular is a SUBSTRING match (<c>string.Contains</c>), not an exact match, so a
/// class or namespace merely containing one of the forbidden terms anywhere in its name (e.g. an
/// unrelated <c>EventSourcingBackfillJob</c>) also matches — an accepted, documented trade-off for
/// keeping the check cheap and free of any assembly reference to the real interface.
/// </para>
/// <para>
/// <strong>Suppression:</strong> Diagnostics are suppressed inside namespaces that start with
/// <c>SharedKernel.Caching</c> or <c>SharedKernel.Caching.Redis</c> — the service's own
/// definition may reference <c>IRedisChannelService</c> freely. The suppression check is itself a
/// <c>StartsWith</c> prefix match on the namespace name, mirroring the substring-match style used
/// for the context check above.
/// </para>
/// <para>
/// <strong>Pass case:</strong> a class such as <c>CacheInvalidationService</c>, in a namespace
/// carrying none of the forbidden substrings, injecting <c>IRedisChannelService</c> freely — the
/// context check never matches, so the type check inside the class is never even reached.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedisChannelServiceMessagingSubstituteAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0007";

    /// <summary>Simple name of the Redis channel service interface.</summary>
    private const string RedisChannelServiceTypeName = "IRedisChannelService";

    /// <summary>
    /// Forbidden context terms — case-sensitive substrings that signal durable-messaging intent.
    /// </summary>
    private static readonly string[] ForbiddenContextTerms =
    [
        "Command",
        "Event",
        "DomainEvent",
        "IntegrationEvent",
    ];

    /// <summary>Namespace prefix inside which the rule is suppressed.</summary>
    private const string SuppressedNamespacePrefix = "SharedKernel.Caching";

    /// <summary>The diagnostic descriptor for SK0007.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "IRedisChannelService used as a messaging substitute",
        messageFormat: "'{0}' injects IRedisChannelService in a messaging-context class — inject IMessageBus (SharedKernel.Messaging.Abstractions) for durable command/event delivery instead",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0007-redischannelservicemessagingsubstitute"
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
            AnalyzeClassDeclaration,
            SyntaxKind.ClassDeclaration
        );
    }

    private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;

        // Suppress inside SharedKernel.Caching* namespaces
        if (IsInsideCachingNamespace(classDecl))
            return;

        // Check whether the class name or any ancestor namespace matches a forbidden context term
        if (!IsInMessagingContext(classDecl))
            return;

        // Inspect constructor parameters
        foreach (var ctor in classDecl.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var param in ctor.ParameterList.Parameters)
            {
                if (IsRedisChannelServiceType(param.Type))
                {
                    var location = param.Type?.GetLocation() ?? param.GetLocation();
                    context.ReportDiagnostic(
                        Diagnostic.Create(Rule, location, classDecl.Identifier.Text)
                    );
                }
            }
        }

        // Inspect field declarations
        foreach (var field in classDecl.Members.OfType<FieldDeclarationSyntax>())
        {
            if (IsRedisChannelServiceType(field.Declaration.Type))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, field.Declaration.Type.GetLocation(), classDecl.Identifier.Text)
                );
            }
        }

        // Inspect property declarations
        foreach (var property in classDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (IsRedisChannelServiceType(property.Type))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, property.Type.GetLocation(), classDecl.Identifier.Text)
                );
            }
        }
    }

    private static bool IsRedisChannelServiceType(TypeSyntax? typeSyntax)
    {
        if (typeSyntax is null)
            return false;

        // Simple name match on the type identifier text
        return GetTypeName(typeSyntax) == RedisChannelServiceTypeName;
    }

    private static string GetTypeName(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            NullableTypeSyntax nullable => GetTypeName(nullable.ElementType),
            // SharedKernel.Caching.Redis.PubSub.IRedisChannelService and global::... forms
            QualifiedNameSyntax qualified => GetTypeName(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => GetTypeName(aliasQualified.Name),
            _ => typeSyntax.ToString(),
        };
    }

    private static bool IsInMessagingContext(ClassDeclarationSyntax classDecl)
    {
        // Check the class name itself
        var className = classDecl.Identifier.Text;
        foreach (var term in ForbiddenContextTerms)
        {
            if (className.Contains(term, System.StringComparison.Ordinal))
                return true;
        }

        // Walk ancestor namespace identifiers
        var current = classDecl.Parent;
        while (current is not null)
        {
            string? nsPart = current switch
            {
                NamespaceDeclarationSyntax ns => ns.Name.ToString(),
                FileScopedNamespaceDeclarationSyntax fsns => fsns.Name.ToString(),
                _ => null,
            };

            if (nsPart is not null)
            {
                foreach (var term in ForbiddenContextTerms)
                {
                    if (nsPart.Contains(term, System.StringComparison.Ordinal))
                        return true;
                }
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool IsInsideCachingNamespace(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is NamespaceDeclarationSyntax ns &&
                ns.Name.ToString().StartsWith(SuppressedNamespacePrefix, System.StringComparison.Ordinal))
            {
                return true;
            }

            if (current is FileScopedNamespaceDeclarationSyntax fsns &&
                fsns.Name.ToString().StartsWith(SuppressedNamespacePrefix, System.StringComparison.Ordinal))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }
}
