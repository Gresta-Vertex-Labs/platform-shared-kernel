using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0028 — Fires when a <c>[Workflow]</c>-attributed type, or a type whose base-type chain
/// contains <c>SharedKernel.Workflows.Temporal.Authoring.WorkflowBase</c>, uses one of seven
/// forbidden non-deterministic or side-effecting APIs anywhere in its own member bodies or
/// constructor parameter list.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Scope is type ATTRIBUTION/INHERITANCE, not namespace</strong> — the first analyzer in
/// this domain to scope its trigger this way instead of the established <see cref="SyntaxNode.Parent"/>
/// namespace-ancestor walk used by SK0001/SK0007/SK0013/etc. A type is IN SCOPE when its declared
/// symbol either carries a <c>[Workflow]</c> attribute (resolved to
/// <c>Temporalio.Workflows.WorkflowAttribute</c>) or has <c>WorkflowBase</c>
/// (<c>SharedKernel.Workflows.Temporal.Authoring.WorkflowBase</c>) anywhere in its base-type chain.
/// </para>
/// <para>
/// <strong>Hard, positive exclusion checked FIRST:</strong> a type carrying <c>ActivityBase</c>
/// (<c>SharedKernel.Workflows.Temporal.Authoring.ActivityBase</c>) anywhere in its base-type chain,
/// or carrying a <c>[Activity]</c> attribute, is unconditionally OUT OF SCOPE — checked before, and
/// independent of, the workflow-scope check above. Inside an activity every one of the seven
/// forbidden shapes is ordinary, correct code (<c>IClock</c>/<c>ILogger&lt;T&gt;</c> injection is in
/// fact MANDATORY there, per SK0001) — see <c>17.Workflows/CLAUDE.md</c>'s own "ACTIVITIES ARE
/// ORDINARY CODE" note. A type carrying both a <c>[Workflow]</c> attribute AND an <c>[Activity]</c>
/// attribute (or both base types, however contrived) is excluded — the Activity check always wins.
/// </para>
/// <para>
/// The seven forbidden shapes, each resolved to its exact declaring type/member via
/// <see cref="SemanticModel"/> (never a syntax-only simple-name match, to avoid collisions with
/// unrelated same-named APIs a consuming service's own code might declare):
/// </para>
/// <list type="number">
///   <item><description><c>DateTime.UtcNow</c> / <c>.Now</c> / <c>DateTimeOffset.UtcNow</c> / <c>.Now</c>.</description></item>
///   <item><description><c>System.Guid.NewGuid()</c>.</description></item>
///   <item><description><c>new Random()</c> (explicit and target-typed <c>new()</c> forms).</description></item>
///   <item><description>
///     <c>System.Threading.Tasks.Task.Run</c> / <c>.Delay</c> (static methods), and a
///     <c>ConfigureAwait(false)</c> invocation whose receiver resolves to <c>Task</c> or
///     <c>ValueTask</c> (any generic arity) — grouped under one shared "escapes the deterministic
///     scheduler" trigger.
///   </description></item>
///   <item><description>Any member access resolved to <c>System.Environment</c> or <c>System.IO.File</c>.</description></item>
///   <item><description>A constructor parameter whose type resolves to <c>SharedKernel.Primitives.IClock</c>.</description></item>
///   <item><description>A constructor parameter whose type resolves to the open generic <c>Microsoft.Extensions.Logging.ILogger&lt;T&gt;</c>.</description></item>
/// </list>
/// <para>
/// <strong>Fix:</strong> use <c>Workflow.UtcNow</c> / <c>Workflow.NewGuid()</c> / <c>Workflow.Random</c>
/// for (1)/(2)/(3); use <c>Workflow.DelayAsync</c>/<c>Workflow.WaitConditionAsync</c> and the SDK's
/// own task combinators for (4); move any environment/filesystem access into an activity for (5);
/// never inject <c>IClock</c> or <c>ILogger&lt;T&gt;</c> into a <c>[Workflow]</c> type — use
/// <c>Workflow.Logger</c> for logging and <c>Workflow.UtcNow</c> rather than <c>IClock</c> for time.
/// </para>
/// <para>
/// <strong>Suppression:</strong> per-call-site via <c>#pragma warning disable SK0028</c>; no
/// legitimate case is known inside a genuine <c>[Workflow]</c> type.
/// </para>
/// <para>
/// <strong>Note:</strong> method-body-local analysis only — a shared helper method called from both
/// workflow and activity code is NOT analyzed for cross-call violations; documented limitation, not
/// a defect, consistent with this domain's established over-approximation-over-data-flow philosophy.
/// <c>HttpClient</c>/general I/O are documented Hard Violations in <c>17.Workflows/CLAUDE.md</c> too
/// but are deliberately NOT part of this rule's seven-shape trigger set.
/// </para>
/// <para>Introduced WO-046 P-290 — the single highest-value analyzer this domain can ship, because
/// every one of these seven shapes compiles cleanly and fails only on REPLAY, in production, at an
/// arbitrary time later, taking down every in-flight execution of that workflow type simultaneously.
/// This domain's twelfth semantic-model analyzer (after SK0011, SK0015, SK0017–SK0019, SK0020,
/// SK0022, SK0024, SK0025, SK0026, SK0027).</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonDeterministicApiUsageInsideWorkflowAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0028";

    private const string WorkflowAttributeFullName = "Temporalio.Workflows.WorkflowAttribute";
    private const string ActivityAttributeFullName = "Temporalio.Activities.ActivityAttribute";
    private const string WorkflowBaseFullName = "SharedKernel.Workflows.Temporal.Authoring.WorkflowBase";
    private const string ActivityBaseFullName = "SharedKernel.Workflows.Temporal.Authoring.ActivityBase";

    private const string DateTimeFullName = "System.DateTime";
    private const string DateTimeOffsetFullName = "System.DateTimeOffset";
    private const string GuidFullName = "System.Guid";
    private const string RandomFullName = "System.Random";
    private const string TaskNamespace = "System.Threading.Tasks";
    private const string TaskSimpleName = "Task";
    private const string ValueTaskSimpleName = "ValueTask";
    private const string EnvironmentFullName = "System.Environment";
    private const string FileFullName = "System.IO.File";
    private const string ClockFullName = "SharedKernel.Primitives.IClock";
    private const string LoggerNamespace = "Microsoft.Extensions.Logging";
    private const string LoggerSimpleName = "ILogger";

    /// <summary>The diagnostic descriptor for SK0028.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Non-deterministic or side-effecting API used inside workflow",
        messageFormat: "'{0}' is forbidden inside the [Workflow]-scoped type '{1}' — workflow code is "
            + "replay code and must produce byte-identical commands on every replay. Use {2} instead; "
            + "if this API is genuinely required, move it into an activity (a type deriving ActivityBase).",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0028-nondeterministicapiusageinsideworkflow"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeImplicitObjectCreation,
            SyntaxKind.ImplicitObjectCreationExpression
        );
        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
    }

    // -------------------------------------------------------------------------------------------
    // Shape 1: DateTime.UtcNow / .Now / DateTimeOffset.UtcNow / .Now (property access)
    // Shape 5 (property form): Environment.* property access (e.g. Environment.MachineName)
    // -------------------------------------------------------------------------------------------

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // If this member access is itself the target of an invocation, the invocation handler
        // below resolves and reports it — skip here to avoid double-analysis of the same node.
        if (memberAccess.Parent is InvocationExpressionSyntax invocationParent
            && invocationParent.Expression == memberAccess)
        {
            return;
        }

        if (!TryGetEnclosingWorkflowType(memberAccess, context.SemanticModel, context.CancellationToken, out var enclosingType))
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken);
        if (symbolInfo.Symbol is not IPropertySymbol { ContainingType: { } containingType } property)
            return;

        var containingTypeFullName = GetFullTypeName(containingType);

        if (property.Name is "UtcNow" or "Now" && containingTypeFullName is DateTimeFullName or DateTimeOffsetFullName)
        {
            Report(context, memberAccess, $"{containingType.Name}.{property.Name}", enclosingType.Name, "Workflow.UtcNow");
            return;
        }

        if (containingTypeFullName == EnvironmentFullName)
        {
            Report(
                context,
                memberAccess,
                $"Environment.{property.Name}",
                enclosingType.Name,
                "an activity — environment access is not deterministic across replay"
            );
        }
    }

    // -------------------------------------------------------------------------------------------
    // Shape 2: Guid.NewGuid()
    // Shape 4: Task.Run(...) / Task.Delay(...), and ConfigureAwait(false)
    // Shape 5 (method form): Environment.*/File.* method calls
    // -------------------------------------------------------------------------------------------

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (!TryGetEnclosingWorkflowType(invocation, context.SemanticModel, context.CancellationToken, out var enclosingType))
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol { ContainingType: { } containingType } method)
            return;

        var containingTypeFullName = GetFullTypeName(containingType);
        var methodName = method.Name;

        // Guid.NewGuid()
        if (methodName == "NewGuid" && containingTypeFullName == GuidFullName)
        {
            Report(context, invocation, "Guid.NewGuid()", enclosingType.Name, "Workflow.NewGuid()");
            return;
        }

        // Task.Run(...) / Task.Delay(...) — static methods declared on the non-generic Task type
        if (
            method.IsStatic
            && methodName is "Run" or "Delay"
            && containingType.Name == TaskSimpleName
            && containingType.ContainingNamespace?.ToDisplayString() == TaskNamespace
        )
        {
            Report(
                context,
                invocation,
                $"Task.{methodName}(...)",
                enclosingType.Name,
                methodName == "Delay" ? "Workflow.DelayAsync(...)" : "the SDK's own task combinators"
            );
            return;
        }

        // ConfigureAwait(false) on a Task/Task<T>/ValueTask/ValueTask<T> receiver
        if (
            methodName == "ConfigureAwait"
            && containingType.ContainingNamespace?.ToDisplayString() == TaskNamespace
            && containingType.Name is TaskSimpleName or ValueTaskSimpleName
            && invocation.ArgumentList.Arguments.Count == 1
            && IsLiteralFalse(invocation.ArgumentList.Arguments[0].Expression)
        )
        {
            Report(
                context,
                invocation,
                "ConfigureAwait(false)",
                enclosingType.Name,
                "no ConfigureAwait call at all — workflow code always runs on Temporal's own deterministic scheduler"
            );
            return;
        }

        // Environment.*/File.* method calls
        if (containingTypeFullName is EnvironmentFullName or FileFullName)
        {
            Report(
                context,
                invocation,
                $"{containingType.Name}.{methodName}(...)",
                enclosingType.Name,
                "an activity — environment/filesystem access is not deterministic across replay"
            );
        }
    }

    // -------------------------------------------------------------------------------------------
    // Shape 3: new Random() (explicit and target-typed new() forms)
    // -------------------------------------------------------------------------------------------

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context) =>
        CheckObjectCreationType(context, (ExpressionSyntax)context.Node);

    private static void AnalyzeImplicitObjectCreation(SyntaxNodeAnalysisContext context) =>
        CheckObjectCreationType(context, (ExpressionSyntax)context.Node);

    private static void CheckObjectCreationType(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        if (!TryGetEnclosingWorkflowType(expression, context.SemanticModel, context.CancellationToken, out var enclosingType))
            return;

        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        if (typeInfo.Type is not { } createdType)
            return;

        if (GetFullTypeName(createdType) == RandomFullName)
        {
            Report(context, expression, "new Random()", enclosingType.Name, "Workflow.Random");
        }
    }

    // -------------------------------------------------------------------------------------------
    // Shape 6: constructor parameter typed IClock
    // Shape 7: constructor parameter typed the open generic ILogger<T>
    // -------------------------------------------------------------------------------------------

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var ctorDecl = (ConstructorDeclarationSyntax)context.Node;

        if (!TryGetEnclosingWorkflowType(ctorDecl, context.SemanticModel, context.CancellationToken, out var enclosingType))
            return;

        foreach (var parameter in ctorDecl.ParameterList.Parameters)
        {
            if (parameter.Type is not { } parameterTypeSyntax)
                continue;

            var typeInfo = context.SemanticModel.GetTypeInfo(parameterTypeSyntax, context.CancellationToken);
            if (typeInfo.Type is not { } parameterType)
                continue;

            if (GetFullTypeName(parameterType) == ClockFullName)
            {
                Report(
                    context,
                    parameterTypeSyntax,
                    "IClock",
                    enclosingType.Name,
                    "Workflow.UtcNow — IClock cannot be injected into a [Workflow] type (workflows are "
                        + "instantiated by the Temporal worker, not by DI) and would be non-deterministic "
                        + "even if it could be"
                );
                continue;
            }

            if (
                parameterType is INamedTypeSymbol { Arity: 1 } namedType
                && namedType.Name == LoggerSimpleName
                && namedType.ContainingNamespace?.ToDisplayString() == LoggerNamespace
            )
            {
                Report(context, parameterTypeSyntax, "ILogger<T>", enclosingType.Name, "Workflow.Logger");
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // Scope resolution — type attribution/inheritance, not namespace
    // -------------------------------------------------------------------------------------------

    private static bool TryGetEnclosingWorkflowType(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out INamedTypeSymbol enclosingType
    )
    {
        enclosingType = null!;

        TypeDeclarationSyntax? typeDecl = null;
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is TypeDeclarationSyntax candidate)
            {
                typeDecl = candidate;
                break;
            }
        }

        if (typeDecl is null)
            return false;

        if (semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken) is not INamedTypeSymbol typeSymbol)
            return false;

        // Hard, positive exclusion — checked FIRST, independent of the workflow-scope check below.
        if (IsActivityScoped(typeSymbol))
            return false;

        if (!IsWorkflowScoped(typeSymbol))
            return false;

        enclosingType = typeSymbol;
        return true;
    }

    private static bool IsActivityScoped(INamedTypeSymbol typeSymbol) =>
        HasAttribute(typeSymbol, ActivityAttributeFullName) || InheritsFrom(typeSymbol, ActivityBaseFullName);

    private static bool IsWorkflowScoped(INamedTypeSymbol typeSymbol) =>
        HasAttribute(typeSymbol, WorkflowAttributeFullName) || InheritsFrom(typeSymbol, WorkflowBaseFullName);

    private static bool HasAttribute(INamedTypeSymbol typeSymbol, string attributeFullName)
    {
        foreach (var attribute in typeSymbol.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass && GetFullTypeName(attributeClass) == attributeFullName)
                return true;
        }

        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol typeSymbol, string baseTypeFullName)
    {
        var current = typeSymbol.BaseType;
        while (current is not null)
        {
            if (GetFullTypeName(current) == baseTypeFullName)
                return true;

            current = current.BaseType;
        }

        return false;
    }

    // -------------------------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------------------------

    private static bool IsLiteralFalse(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.FalseLiteralExpression);

    private static void Report(
        SyntaxNodeAnalysisContext context,
        SyntaxNode node,
        string apiDescription,
        string enclosingTypeName,
        string suggestion
    )
    {
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, node.GetLocation(), apiDescription, enclosingTypeName, suggestion)
        );
    }

    private static string GetFullTypeName(ITypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false }
            ? $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}"
            : type.Name;
}
