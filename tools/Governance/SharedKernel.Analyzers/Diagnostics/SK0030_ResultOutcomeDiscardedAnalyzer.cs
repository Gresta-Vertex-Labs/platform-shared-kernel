using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0030 — Fires when a bare <see cref="ExpressionStatementSyntax"/> wraps an
/// <see cref="InvocationExpressionSyntax"/> or an <see cref="AwaitExpressionSyntax"/> whose
/// <see cref="SemanticModel"/>-resolved type implements
/// <c>SharedKernel.Primitives.Results.IHasSuccessFlag</c> (i.e. is <c>Result</c> or
/// <c>Result&lt;T&gt;</c>) — the outcome is produced and immediately discarded.
/// </summary>
/// <remarks>
/// <para>
/// <c>Result</c>/<c>Result&lt;T&gt;</c> is the platform's sole sanctioned error-handling
/// primitive: its entire value proposition is that callers must explicitly branch on outcome
/// instead of exceptions silently unwinding the stack. That proposition is completely defeated
/// the moment a caller invokes a <c>Result</c>-returning member as a bare statement and never
/// inspects <c>.IsSuccess</c>/<c>.IsFailure</c> — it compiles cleanly, produces no compiler
/// warning, and the failure path is simply gone, exactly as dangerous as a fire-and-forgotten
/// <c>Task</c> (CS4014), one level further down the stack.
/// </para>
/// <para>
/// <strong>Registration.</strong> Registers on exactly two <see cref="SyntaxKind"/>s — the
/// <see cref="ExpressionStatementSyntax"/>'s <see cref="InvocationExpressionSyntax"/>/
/// <see cref="AwaitExpressionSyntax"/> child shapes — and deliberately never inspects
/// <see cref="AssignmentExpressionSyntax"/> at all. This single structural exclusion is what
/// makes BOTH the "assigned to a variable/field" pass case (<c>result = Foo();</c>) and the
/// "explicit discard" pass case (<c>_ = Foo();</c>) fall out for free, with zero
/// <c>SemanticModel.GetSymbolInfo</c>/<c>IDiscardSymbol</c> check needed — cheaper and simpler
/// than a naive first design (which would otherwise need to distinguish a true compiler discard
/// from a real local variable literally named <c>_</c>) would produce.
/// </para>
/// <para>
/// <strong>Interface match.</strong> Uses the same "simple name + <c>ContainingNamespace</c>
/// prefix" discriminator established by SK0017–SK0019 (<c>"IHasSuccessFlag"</c> +
/// <c>"SharedKernel.Primitives"</c> prefix, matching the real type's actual home at
/// <c>SharedKernel.Primitives.Results.IHasSuccessFlag</c>) rather than an exact-assembly
/// <see cref="INamedTypeSymbol"/> identity check — analyzer test fixtures can declare a
/// fixture-local <c>IHasSuccessFlag</c>-named interface inside a matching-namespace code block
/// in the SAME test compilation, with no <c>ProjectReference</c> to the real
/// <c>SharedKernel.Primitives</c> assembly required. The check walks
/// <see cref="INamedTypeSymbol.AllInterfaces"/> — plus the resolved type itself, in case the
/// resolved static type IS the marker interface — never a <c>BaseList</c>/syntax-only check,
/// since <c>Result</c>/<c>Result&lt;T&gt;</c> and any consumer-defined <c>IHasSuccessFlag</c>
/// implementor may satisfy the interface transitively.
/// </para>
/// <para>
/// <strong>Await unwrapping.</strong> For the <see cref="AwaitExpressionSyntax"/> shape,
/// <c>SemanticModel.GetTypeInfo(awaitExpressionSyntax)</c> already returns Roslyn's own
/// unwrapped "what does <c>await x</c> evaluate to" type — no manual <c>Task&lt;T&gt;</c>/
/// <c>ValueTask&lt;T&gt;</c> unwrapping logic is written or needed. This is what makes
/// <c>await FooAsync();</c> as a bare statement (where <c>FooAsync</c> returns
/// <c>Task&lt;Result&lt;T&gt;&gt;</c>/<c>ValueTask&lt;Result&lt;T&gt;&gt;</c>) resolve directly
/// to <c>Result&lt;T&gt;</c> and correctly FIRE — the CS4014 analogy one level further down: an
/// awaited-but-unobserved <c>Result</c> outcome is exactly as dangerous as an unawaited
/// <c>Task</c>.
/// </para>
/// <para>
/// <strong>Outermost-type-only.</strong> Only ever inspects the OUTERMOST
/// <see cref="ExpressionStatementSyntax.Expression"/>'s resolved type — never a nested
/// sub-expression's type. This single design choice is what makes "passed as an argument"
/// (<c>Bar(Foo());</c>, outer type is <c>Bar</c>'s return type) and "receiver of a
/// member-access/method chain" (<c>Foo().Match(...);</c>, outer type is <c>Match</c>'s return
/// type) both pass without any special-case logic — while a fluent chain whose OUTERMOST call
/// still resolves to an <c>IHasSuccessFlag</c>-implementing type (<c>Foo().Map(x =&gt; x +
/// 1);</c>, where <c>Map</c> itself returns <c>Result&lt;TNew&gt;</c>) correctly still FIRES,
/// since the chain's final produced <c>Result</c> is genuinely, separately discarded. This is
/// deliberate, not a false positive — do not "fix" the fire case.
/// </para>
/// <para>
/// <strong>Scope limitations (deliberate, not oversights).</strong> Does NOT register on
/// <see cref="ObjectCreationExpressionSyntax"/> — <c>Result</c>/<c>Result&lt;T&gt;</c> in this
/// platform are produced exclusively via static factory methods (<c>Result.Success()</c>/
/// <c>Result&lt;T&gt;.Failure(...)</c>), never a public constructor call — nor on
/// <see cref="ConditionalAccessExpressionSyntax"/> (<c>maybeService?.ReturnsResult();</c>). Both
/// are accepted false-negative risks, mirroring this domain's established
/// SK0708/HealthCheckTagIntegrityRules
/// "document the limitation, revisit only on a real finding" discipline.
/// </para>
/// <para>
/// <strong>Fix:</strong> assign the result to a variable and branch on
/// <c>IsSuccess</c>/<c>IsFailure</c>, return it to the caller, pass it into a consuming method,
/// or — when the outcome is genuinely irrelevant at this call site — make that explicit with
/// <c>_ = SomeMethodReturningResult();</c> instead of a bare statement.
/// </para>
/// <para>
/// <strong>Suppression:</strong> per-call-site via <c>#pragma warning disable SK0030</c>, or —
/// preferred, since it requires no suppression comment at all — rewrite the bare statement as an
/// explicit discard assignment (<c>_ = SomeMethodReturningResult();</c>).
/// </para>
/// <para>
/// Introduced WO-049 P-299. This domain's fourteenth semantic-model analyzer (after SK0011,
/// SK0015, SK0017–SK0019, SK0020, SK0022, SK0024, SK0025, SK0026, SK0027, SK0028, SK0029). Has
/// NO <c>SharedKernel.ArchitectureTests</c> counterpart — a bare-statement <c>Result</c> discard
/// is inherently a per-syntax-tree, per-compilation-unit concern this analyzer resolves
/// completely inside each consuming project's own build; there is no assembly-dependency-graph
/// or IL-level aspect this rule could usefully add.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ResultOutcomeDiscardedAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0030";

    private const string HasSuccessFlagSimpleName = "IHasSuccessFlag";
    private const string HasSuccessFlagNamespacePrefix = "SharedKernel.Primitives";

    /// <summary>The diagnostic descriptor for SK0030.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Result outcome discarded",
        messageFormat: "This Result's outcome is never checked — a failure will pass silently. "
            + "Assign it, branch on it, return it, pass it as an argument, or explicitly discard "
            + "it with '_ = ...'.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0030-resultoutcomediscarded"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Registers on exactly two SyntaxKinds — InvocationExpressionSyntax and
        // AwaitExpressionSyntax. AssignmentExpressionSyntax (covering both a real re-assignment
        // and an explicit "_ = ..." discard) is never registered at all — both pass structurally
        // with zero dedicated discard-detection logic.
        context.RegisterSyntaxNodeAction(
            AnalyzeInvocationOrAwait,
            SyntaxKind.InvocationExpression,
            SyntaxKind.AwaitExpression
        );
    }

    private static void AnalyzeInvocationOrAwait(SyntaxNodeAnalysisContext context)
    {
        // Only the OUTERMOST expression of a bare ExpressionStatementSyntax is ever inspected —
        // a nested invocation/await (e.g. the inner "Foo()" in "Foo().Map(...);", or the inner
        // "FooAsync()" in "await FooAsync();") has some other syntax kind as its parent, never
        // ExpressionStatementSyntax directly, and is skipped without a semantic-model call. This
        // single check is what makes "passed as an argument" (Bar(Foo());), "returned"
        // (return Foo();), and "receiver of a further member-access chain" (Foo().Match(...);)
        // all pass for the same structural reason, while a fluent chain whose OUTERMOST call
        // still resolves to an IHasSuccessFlag-implementing type still fires.
        if (context.Node.Parent is not ExpressionStatementSyntax)
            return;

        var resolvedType = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type;
        if (resolvedType is null)
            return;

        if (!ImplementsHasSuccessFlag(resolvedType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> itself, or any interface in its
    /// full interface closure (<see cref="INamedTypeSymbol.AllInterfaces"/>), is named
    /// <c>IHasSuccessFlag</c> and sits under a <c>SharedKernel.Primitives</c>-prefixed namespace.
    /// </summary>
    private static bool ImplementsHasSuccessFlag(ITypeSymbol type)
    {
        if (IsHasSuccessFlagType(type))
            return true;

        if (type is not INamedTypeSymbol namedType)
            return false;

        foreach (var iface in namedType.AllInterfaces)
        {
            if (IsHasSuccessFlagType(iface))
                return true;
        }

        return false;
    }

    private static bool IsHasSuccessFlagType(ITypeSymbol type)
    {
        if (type.Name != HasSuccessFlagSimpleName)
            return false;

        var ns = type.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
            return false;

        return ns.ToDisplayString().StartsWith(HasSuccessFlagNamespacePrefix, StringComparison.Ordinal);
    }
}
