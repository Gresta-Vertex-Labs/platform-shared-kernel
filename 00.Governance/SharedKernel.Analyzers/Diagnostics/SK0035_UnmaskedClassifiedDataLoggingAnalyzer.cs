using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0035 — Fires when a call to a <c>[LoggerMessage]</c>-attributed logging method passes, as one
/// of its message-template arguments, a member (or a whole object declaring a member) carrying
/// <c>SharedKernel.DataPrivacy.Classification.DataClassificationAttribute</c>
/// (Classification == Restricted) or
/// <c>SharedKernel.DataPrivacy.Classification.SensitiveDataCategoryAttribute</c> (any category),
/// without first routing it through a <c>SharedKernel.DataPrivacy.Masking.PiiMasking.*</c> helper.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fully-qualified metadata-name resolution, not a compiled <c>ProjectReference</c>.</strong>
/// <c>DataClassificationAttribute</c>/<c>SensitiveDataCategoryAttribute</c>/<c>PiiMasking</c> are
/// resolved via <see cref="Compilation.GetTypeByMetadataName(string)"/> against their fully-qualified
/// names — the same technique WO-040/P-248's marker-interface rules (SK0017–SK0019) established.
/// This means a test fixture can declare its OWN fixture-local <c>SharedKernel.DataPrivacy</c>
/// namespace with matching type names inside the same test compilation, requiring no
/// <c>ProjectReference</c> to the real <c>SharedKernel.DataPrivacy</c> package.
/// </para>
/// <para>
/// <strong>Two covered shapes:</strong> (1) a direct member reference — the argument expression
/// itself resolves to a property/field symbol carrying either attribute; (2) whole-object
/// destructuring — the argument expression's STATIC TYPE declares any member carrying either
/// attribute, passed directly with no masking call (the <c>{@ParamName}</c>-shaped case, covering an
/// entire classified-bearing DTO/entity passed as a single argument).
/// </para>
/// <para>
/// <strong>Special parameters are excluded</strong> from inspection: the reduced <c>this ILogger</c>
/// receiver (already absent from the reduced extension-method argument list), and any parameter
/// typed <c>Microsoft.Extensions.Logging.LogLevel</c> or <see cref="Exception"/>
/// (or an exception-derived type) — neither is a message-template placeholder in the sense this
/// rule cares about.
/// </para>
/// <para>
/// <strong>Restricted-classification check:</strong> <c>DataClassificationAttribute</c>'s first
/// constructor argument is compared, by constant value, against the <c>Restricted</c> member of the
/// resolved <c>SharedKernel.DataPrivacy.Classification.DataClassification</c> enum — not by string-matching the
/// syntax. <c>SensitiveDataCategoryAttribute</c> has no such filter: ANY category is in scope,
/// matching this rule's Trigger contract.
/// </para>
/// <para>
/// <strong>Scope limit (documented, intentional):</strong> only a DIRECT member reference or a
/// direct <c>PiiMasking.*</c> wrapper call is recognized — an intermediate local variable
/// (<c>var x = entity.Ssn; logger.LogX(x);</c>) or a helper method that internally reads a
/// classified member and returns it unmasked is not traced across that boundary. This mirrors this
/// file's established "pattern/presence check, not full data-flow analysis" convention (SK0028,
/// <c>HealthCheckTagIntegrityRules</c>, SK0032).
/// </para>
/// <para>
/// No <c>SharedKernel.ArchitectureTests</c> counterpart — a per-compilation-unit source-level check
/// this Roslyn analyzer resolves completely on its own. Introduced WO-076 P-476. Composes with, but
/// is structurally distinct from, SK0022 (magic strings) and SK0020/SK0021 (logging authoring
/// shape) — this rule inspects the DATA flowing into an already-correctly-shaped
/// <c>[LoggerMessage]</c> call, not the call's own shape or its string-literal arguments.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnmaskedClassifiedDataLoggingAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0035";

    private const string LoggerMessageAttributeMetadataName =
        "Microsoft.Extensions.Logging.LoggerMessageAttribute";
    private const string LogLevelMetadataName = "Microsoft.Extensions.Logging.LogLevel";
    private const string ExceptionMetadataName = "System.Exception";
    private const string DataClassificationAttributeMetadataName =
        "SharedKernel.DataPrivacy.Classification.DataClassificationAttribute";
    private const string DataClassificationEnumMetadataName =
        "SharedKernel.DataPrivacy.Classification.DataClassification";
    private const string SensitiveDataCategoryAttributeMetadataName =
        "SharedKernel.DataPrivacy.Classification.SensitiveDataCategoryAttribute";
    private const string PiiMaskingMetadataName = "SharedKernel.DataPrivacy.Masking.PiiMasking";
    private const string RestrictedMemberName = "Restricted";

    /// <summary>The diagnostic descriptor for SK0035.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Unmasked classified data reaches a logging call site",
        messageFormat: "'{0}' carries {1} and is passed directly to [LoggerMessage]-attributed "
            + "parameter '{2}'. Route it through the matching SharedKernel.DataPrivacy.PiiMasking.* "
            + "helper (.Email/.Phone/.Pan/.Suppress) before passing it as a logging argument.",
        category: Security,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0035-unmaskedclassifieddataatloggingcallsite"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var compilation = compilationContext.Compilation;

            var loggerMessageAttributeType = compilation.GetTypeByMetadataName(
                LoggerMessageAttributeMetadataName
            );

            if (loggerMessageAttributeType is null)
                return;

            var dataClassificationAttributeType = compilation.GetTypeByMetadataName(
                DataClassificationAttributeMetadataName
            );
            var sensitiveDataCategoryAttributeType = compilation.GetTypeByMetadataName(
                SensitiveDataCategoryAttributeMetadataName
            );

            // Nothing this rule can ever flag in this compilation — neither classification marker
            // attribute is resolvable.
            if (dataClassificationAttributeType is null && sensitiveDataCategoryAttributeType is null)
                return;

            var resolvedTypes = new ResolvedTypes(
                loggerMessageAttributeType,
                dataClassificationAttributeType,
                compilation.GetTypeByMetadataName(DataClassificationEnumMetadataName),
                sensitiveDataCategoryAttributeType,
                compilation.GetTypeByMetadataName(PiiMaskingMetadataName),
                compilation.GetTypeByMetadataName(LogLevelMetadataName),
                compilation.GetTypeByMetadataName(ExceptionMetadataName)
            );

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, resolvedTypes),
                SyntaxKind.InvocationExpression
            );
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, ResolvedTypes resolvedTypes)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);

        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        if (!HasAttribute(methodSymbol, resolvedTypes.LoggerMessageAttributeType))
            return;

        var parameters = methodSymbol.Parameters;
        var arguments = invocation.ArgumentList.Arguments;

        var count = Math.Min(parameters.Length, arguments.Count);

        for (var i = 0; i < count; i++)
        {
            var parameter = parameters[i];

            if (IsSpecialParameter(parameter, resolvedTypes))
                continue;

            var argumentExpression = arguments[i].Expression;

            if (IsMaskingCall(context, argumentExpression, resolvedTypes.PiiMaskingType))
                continue;

            var classification = ResolveClassification(context, argumentExpression, resolvedTypes, out var member);

            if (classification is null || member is null)
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    argumentExpression.GetLocation(),
                    $"{member.ContainingType?.Name}.{member.Name}",
                    classification,
                    parameter.Name
                )
            );
        }
    }

    private static bool IsSpecialParameter(IParameterSymbol parameter, ResolvedTypes resolvedTypes)
    {
        var type = parameter.Type;

        if (resolvedTypes.LogLevelType is not null
            && SymbolEqualityComparer.Default.Equals(type, resolvedTypes.LogLevelType))
        {
            return true;
        }

        if (resolvedTypes.ExceptionType is not null && DerivesFromOrIs(type, resolvedTypes.ExceptionType))
            return true;

        return false;
    }

    private static bool DerivesFromOrIs(ITypeSymbol type, INamedTypeSymbol candidateBaseType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, candidateBaseType))
                return true;
        }

        return false;
    }

    private static bool IsMaskingCall(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression,
        INamedTypeSymbol? piiMaskingType
    )
    {
        if (piiMaskingType is null)
            return false;

        if (expression is not InvocationExpressionSyntax innerInvocation)
            return false;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(innerInvocation, context.CancellationToken);

        return symbolInfo.Symbol is IMethodSymbol method
            && SymbolEqualityComparer.Default.Equals(method.ContainingType, piiMaskingType);
    }

    /// <summary>
    /// Resolves the classification label ("DataClassification(Restricted)"/"SensitiveDataCategory")
    /// for <paramref name="argumentExpression"/>, checking both the direct-member-reference shape
    /// and the whole-object-destructuring shape. Returns <see langword="null"/> when neither shape
    /// matches.
    /// </summary>
    private static string? ResolveClassification(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax argumentExpression,
        ResolvedTypes resolvedTypes,
        out ISymbol? member
    )
    {
        // Shape 1 — a direct property/field member reference.
        var symbolInfo = context.SemanticModel.GetSymbolInfo(argumentExpression, context.CancellationToken);

        if (symbolInfo.Symbol is IPropertySymbol or IFieldSymbol)
        {
            var classification = GetClassificationLabel(symbolInfo.Symbol, resolvedTypes);

            if (classification is not null)
            {
                member = symbolInfo.Symbol;
                return classification;
            }
        }

        // Shape 2 — whole-object destructuring: the argument's STATIC TYPE declares a classified
        // member, even though the argument expression itself is not a direct reference to that
        // member (e.g. passing an entire entity/DTO instance).
        var typeInfo = context.SemanticModel.GetTypeInfo(argumentExpression, context.CancellationToken);

        if (typeInfo.Type is INamedTypeSymbol namedType)
        {
            foreach (var candidateMember in namedType.GetMembers())
            {
                if (candidateMember is not (IPropertySymbol or IFieldSymbol))
                    continue;

                var classification = GetClassificationLabel(candidateMember, resolvedTypes);

                if (classification is null)
                    continue;

                member = candidateMember;
                return classification;
            }
        }

        member = null;
        return null;
    }

    private static string? GetClassificationLabel(ISymbol member, ResolvedTypes resolvedTypes)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (resolvedTypes.DataClassificationAttributeType is not null
                && SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass,
                    resolvedTypes.DataClassificationAttributeType)
                && IsRestrictedClassification(attribute, resolvedTypes.DataClassificationEnumType))
            {
                return "DataClassification(Restricted)";
            }

            if (resolvedTypes.SensitiveDataCategoryAttributeType is not null
                && SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass,
                    resolvedTypes.SensitiveDataCategoryAttributeType))
            {
                return "SensitiveDataCategory";
            }
        }

        return null;
    }

    private static bool IsRestrictedClassification(AttributeData attribute, INamedTypeSymbol? classificationEnumType)
    {
        if (classificationEnumType is null || attribute.ConstructorArguments.Length == 0)
            return false;

        IFieldSymbol? restrictedField = null;

        foreach (var candidateMember in classificationEnumType.GetMembers(RestrictedMemberName))
        {
            if (candidateMember is IFieldSymbol field)
            {
                restrictedField = field;
                break;
            }
        }

        if (restrictedField?.ConstantValue is null)
            return false;

        var argumentValue = attribute.ConstructorArguments[0].Value;

        if (argumentValue is null)
            return false;

        try
        {
            return Convert.ToInt64(argumentValue) == Convert.ToInt64(restrictedField.ConstantValue);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return false;
        }
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attributeType)
    {
        if (attributeType is null)
            return false;

        foreach (var attribute in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Snapshot of every metadata-name-resolved type this analyzer needs, computed once per
    /// <see cref="Compilation"/> inside the compilation-start action and threaded through every
    /// subsequent syntax-node action for that compilation.
    /// </summary>
    private sealed class ResolvedTypes
    {
        public ResolvedTypes(
            INamedTypeSymbol loggerMessageAttributeType,
            INamedTypeSymbol? dataClassificationAttributeType,
            INamedTypeSymbol? dataClassificationEnumType,
            INamedTypeSymbol? sensitiveDataCategoryAttributeType,
            INamedTypeSymbol? piiMaskingType,
            INamedTypeSymbol? logLevelType,
            INamedTypeSymbol? exceptionType
        )
        {
            LoggerMessageAttributeType = loggerMessageAttributeType;
            DataClassificationAttributeType = dataClassificationAttributeType;
            DataClassificationEnumType = dataClassificationEnumType;
            SensitiveDataCategoryAttributeType = sensitiveDataCategoryAttributeType;
            PiiMaskingType = piiMaskingType;
            LogLevelType = logLevelType;
            ExceptionType = exceptionType;
        }

        public INamedTypeSymbol LoggerMessageAttributeType { get; }

        public INamedTypeSymbol? DataClassificationAttributeType { get; }

        public INamedTypeSymbol? DataClassificationEnumType { get; }

        public INamedTypeSymbol? SensitiveDataCategoryAttributeType { get; }

        public INamedTypeSymbol? PiiMaskingType { get; }

        public INamedTypeSymbol? LogLevelType { get; }

        public INamedTypeSymbol? ExceptionType { get; }
    }
}
