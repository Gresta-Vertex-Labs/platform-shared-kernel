using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0041 — Fires when two or more non-abstract types implementing
/// <c>SharedKernel.Application.Caching.ICacheableQuery&lt;TValue&gt;</c> share a simple
/// type name within one compilation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The gap.</strong> The caching behavior namespaces every entry it writes by the query's
/// simple type name: the key is <c>{service}:{QueryType}:{CacheKey}</c>. That namespace is what stops
/// two unrelated queries that happen to choose the same <c>CacheKey</c> from reading each other's
/// entries. Two query types with the same simple name in different namespaces collapse back into one
/// namespace and reintroduce exactly the collision it exists to prevent.
/// </para>
/// <para>
/// <strong>Why the collision is silent.</strong> An entry holds the bare <c>TValue</c> as JSON, not
/// the <c>Result</c> and not a type discriminator. When <c>Orders.GetSummaryQuery : ICacheableQuery&lt;OrderSummary&gt;</c>
/// and <c>Billing.GetSummaryQuery : ICacheableQuery&lt;InvoiceSummary&gt;</c> both write key
/// <c>"42"</c>, the second reads the first's payload and <c>System.Text.Json</c> deserializes it into
/// its own type on a best-effort basis — unmatched members are left at their defaults and no
/// exception is raised. The failure surfaces as a partially-populated object far from its cause,
/// which is why this is worth a compile-time rule rather than a documentation note.
/// </para>
/// <para>
/// <strong>Why the simple name, not the full name.</strong> The entity segment appears in every key;
/// a namespace-qualified name makes keys unreadable in a cache browser and bloats every Redis key for
/// a collision that is rare and trivially fixed by renaming. The platform takes the shorter key and
/// pays for it with this rule.
/// </para>
/// <para>
/// <strong>Generic query types.</strong> Grouping is by simple name <i>and</i> arity, so
/// <c>GetQuery</c> and <c>GetQuery&lt;T&gt;</c> never collide with each other. Two generic
/// declarations sharing a name and arity are reported even though they collide only when a consumer
/// closes them over the same type arguments — the runtime entity name includes the arguments. That
/// is a deliberate over-report: the rule is a Warning, and renaming one of them is cheaper than
/// reasoning about which closures are reachable.
/// </para>
/// <para>
/// <strong>Compilation-scoped, not solution-scoped.</strong> Only types compiled together are
/// compared. Two services that each define a <c>GetOrderQuery</c> are not in conflict — they are
/// different services with different cache service names, and <c>ICacheKeyProvider</c> already
/// prefixes every key with the owning service.
/// </para>
/// <para>
/// <strong>Exemption.</strong> Types carrying the <see langword="abstract"/> modifier are excluded —
/// the same exemption already applied by SK0009/SK0017/SK0018/SK0040. An abstract base declaring the
/// marker is never dispatched and never writes an entry.
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0041</c> at the type declaration with an
/// inline comment documenting the rationale; fires globally, no suppression namespace. Suppress it
/// only when the two queries genuinely share one cache namespace on purpose and return the same
/// <c>TValue</c>.
/// </para>
/// <para>Introduced as a governance companion to <c>05.Application</c>'s pre-publish pass on
/// <c>SharedKernel.Application.Caching</c>.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateCacheableQueryNameAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0041";
    private const string NamespacePrefix = "SharedKernel.Application";
    private const string CacheableQuerySimpleName = "ICacheableQuery";

    /// <summary>The diagnostic descriptor for SK0041.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Two cacheable queries share a simple type name",
        messageFormat: "'{0}' shares its simple type name with {1}, and both implement "
            + "ICacheableQuery<TValue>. Cache entries are namespaced by the query's simple type "
            + "name, so these queries share one namespace: if they ever produce the same CacheKey, "
            + "one is served the other's cached value, deserialized into the wrong type without an "
            + "error. Rename one of them.",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0041-duplicatecacheablequeryname"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // A collision is a property of the compilation, not of one declaration, so the check cannot
        // run per-node: declarations are collected as they are visited and compared once at the end.
        context.RegisterCompilationStartAction(start =>
        {
            var declarations = new ConcurrentBag<CacheableQueryDeclaration>();

            start.RegisterSyntaxNodeAction(
                node => Collect(node, declarations),
                SyntaxKind.ClassDeclaration,
                SyntaxKind.RecordDeclaration,
                SyntaxKind.StructDeclaration
            );

            start.RegisterCompilationEndAction(end => Report(end, declarations));
        });
    }

    private static void Collect(SyntaxNodeAnalysisContext context, ConcurrentBag<CacheableQueryDeclaration> declarations)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;

        if (typeDecl.Modifiers.Any(SyntaxKind.AbstractKeyword))
            return;

        if (context.SemanticModel.GetDeclaredSymbol(typeDecl, context.CancellationToken)
                is not INamedTypeSymbol symbol)
        {
            return;
        }

        if (!MarkerInterfaceHelpers.HasInterface(symbol, CacheableQuerySimpleName, arity: 1, NamespacePrefix))
            return;

        declarations.Add(
            new CacheableQueryDeclaration(
                symbol.Name,
                symbol.Arity,
                symbol.ToDisplayString(),
                typeDecl.Identifier.GetLocation()
            )
        );
    }

    private static void Report(CompilationAnalysisContext context, ConcurrentBag<CacheableQueryDeclaration> declarations)
    {
        var groups = new Dictionary<(string Name, int Arity), List<CacheableQueryDeclaration>>();

        foreach (var declaration in declarations)
        {
            var key = (declaration.Name, declaration.Arity);
            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups[key] = group;
            }

            group.Add(declaration);
        }

        foreach (var group in groups.Values)
        {
            if (group.Count < 2)
                continue;

            // A partial type declared across several files produces one symbol per declaration; the
            // display string is the identity that matters, so identical ones are not a collision.
            var distinct = group
                .GroupBy(d => d.DisplayName, StringComparer.Ordinal)
                .ToList();

            if (distinct.Count < 2)
                continue;

            foreach (var declaration in group)
            {
                var others = distinct
                    .Select(g => g.Key)
                    .Where(name => !string.Equals(name, declaration.DisplayName, StringComparison.Ordinal))
                    .OrderBy(name => name, StringComparer.Ordinal);

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        Rule,
                        declaration.Location,
                        declaration.DisplayName,
                        string.Join(", ", others)
                    )
                );
            }
        }
    }

    // A plain class, not a record: this project targets netstandard2.0 for analyzer loading, which
    // has no IsExternalInit, so init-only positional records do not compile here.
    private sealed class CacheableQueryDeclaration(string name, int arity, string displayName, Location location)
    {
        public string Name { get; } = name;

        public int Arity { get; } = arity;

        public string DisplayName { get; } = displayName;

        public Location Location { get; } = location;
    }
}
