using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharedKernel.Presentation.WebApi.Generators;

/// <summary>
/// Finds every type of the compilation that implements <c>SharedKernel.Presentation.WebApi.IEndpointModule</c> and
/// generates <c>MapEndpoints(this IEndpointRouteBuilder)</c>, which calls each module's static <c>Map</c> in the
/// ordinal order of the modules' full names (P-563 P2).
/// </summary>
[Generator(LanguageNames.CSharp)]
internal sealed class EndpointModuleGenerator : IIncrementalGenerator
{
    /// <summary>The metadata name of the module interface.</summary>
    internal const string ModuleInterfaceMetadataName = "SharedKernel.Presentation.WebApi.IEndpointModule";

    /// <summary>The name of the generated class.</summary>
    internal const string GeneratedClassName = "EndpointModuleMappingExtensions";

    /// <summary>The hint name of the generated file.</summary>
    internal const string HintName = "EndpointModuleMappingExtensions.g.cs";

    private const string RouteBuilder = "global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";
    private const string ModuleInterface = "global::" + ModuleInterfaceMetadataName;

    private static readonly SymbolDisplayFormat FullNameFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Only the WebApi assembly can be the interface's home: a compilation that declares a type of that name itself
        // (WebApi's own) gets nothing.
        var webApiReferenced = context.CompilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName(ModuleInterfaceMetadataName) is { } moduleInterface
                && !SymbolEqualityComparer.Default.Equals(moduleInterface.ContainingAssembly, compilation.Assembly))
            .WithTrackingName(TrackingNames.WebApiReferenced);

        var modules = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax { BaseList: not null } and not InterfaceDeclarationSyntax,
                transform: static (syntaxContext, cancellationToken) => ToCandidate(syntaxContext, cancellationToken))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!)
            .WithTrackingName(TrackingNames.Candidates)
            .Collect()
            .Select(static (candidates, _) => new EquatableArray<EndpointModuleCandidate>(Normalize(candidates)))
            .WithTrackingName(TrackingNames.Modules);

        context.RegisterSourceOutput(
            modules.Combine(webApiReferenced).WithTrackingName(TrackingNames.Output),
            static (productionContext, input) => Emit(productionContext, input.Left, input.Right));
    }

    private static EndpointModuleCandidate? ToCandidate(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;
        var moduleInterface = context.SemanticModel.Compilation.GetTypeByMetadataName(ModuleInterfaceMetadataName);
        if (moduleInterface is null
            || context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol type
            || type.TypeKind is TypeKind.Interface
            || !type.AllInterfaces.Contains(moduleInterface, SymbolEqualityComparer.Default))
        {
            return null;
        }

        var mapMember = moduleInterface.GetMembers("Map").OfType<IMethodSymbol>().FirstOrDefault();
        var implementation = mapMember is null ? null : type.FindImplementationForInterfaceMember(mapMember) as IMethodSymbol;
        if (implementation is null)
        {
            // The type does not satisfy the interface; the compiler already reports that (CS0535).
            return null;
        }

        var (issue, detail) = FindIssue(type, implementation);

        return new EndpointModuleCandidate(
            type.ToDisplayString(FullNameFormat),
            implementation.MethodKind == MethodKind.ExplicitInterfaceImplementation,
            issue,
            detail,
            LocationInfo.From(declaration.Identifier.GetLocation()));
    }

    private static (ModuleIssue Issue, string? Detail) FindIssue(INamedTypeSymbol type, IMethodSymbol implementation)
    {
        if (type.IsAbstract)
        {
            return (ModuleIssue.Abstract, null);
        }

        if (type.Arity > 0)
        {
            return (ModuleIssue.Generic, null);
        }

        for (var container = type.ContainingType; container is not null; container = container.ContainingType)
        {
            if (container.Arity > 0)
            {
                return (ModuleIssue.NestedInGeneric, container.ToDisplayString());
            }
        }

        for (var current = type; current is not null; current = current.ContainingType)
        {
            var reason = UnreachableReason(current, current.Equals(type, SymbolEqualityComparer.Default));
            if (reason is not null)
            {
                return (ModuleIssue.Inaccessible, reason);
            }
        }

        if (!SymbolEqualityComparer.Default.Equals(implementation.ContainingType.OriginalDefinition, type.OriginalDefinition))
        {
            return (ModuleIssue.InheritsMap, implementation.ContainingType.ToDisplayString());
        }

        return (ModuleIssue.None, null);
    }

    private static string? UnreachableReason(INamedTypeSymbol type, bool isModule)
    {
        var subject = isModule ? "it is" : $"the type '{type.ToDisplayString()}' it is nested in is";

        if (type.IsFileLocal)
        {
            return $"{subject} file-local";
        }

        return type.DeclaredAccessibility switch
        {
            Accessibility.Private => $"{subject} private",
            Accessibility.Protected => $"{subject} protected",
            Accessibility.ProtectedAndInternal => $"{subject} private protected",
            _ => null,
        };
    }

    /// <summary>One candidate per type (a partial type declares its bases more than once), in ordinal name order.</summary>
    private static ImmutableArray<EndpointModuleCandidate> Normalize(ImmutableArray<EndpointModuleCandidate> candidates) =>
        candidates
            .GroupBy(static candidate => candidate.FullName, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static candidate => candidate.SortKey, StringComparer.Ordinal)
            .ToImmutableArray();

    private static void Emit(SourceProductionContext context, EquatableArray<EndpointModuleCandidate> modules, bool webApiReferenced)
    {
        if (!webApiReferenced || modules.Length == 0)
        {
            return;
        }

        foreach (var module in modules)
        {
            var diagnostic = ToDiagnostic(module);
            if (diagnostic is not null)
            {
                context.ReportDiagnostic(diagnostic);
            }
        }

        context.AddSource(HintName, Render(modules.Items.Where(static module => module.Issue == ModuleIssue.None).ToImmutableArray()));
    }

    private static Diagnostic? ToDiagnostic(EndpointModuleCandidate module)
    {
        var location = module.Location.ToLocation();
        var name = module.SortKey;

        return module.Issue switch
        {
            ModuleIssue.Abstract => Diagnostic.Create(EndpointModuleDiagnostics.AbstractModule, location, name),
            ModuleIssue.Generic => Diagnostic.Create(EndpointModuleDiagnostics.GenericModule, location, name, "is generic"),
            ModuleIssue.NestedInGeneric => Diagnostic.Create(
                EndpointModuleDiagnostics.GenericModule, location, name, $"is nested in the generic type '{module.IssueDetail}'"),
            ModuleIssue.Inaccessible => Diagnostic.Create(EndpointModuleDiagnostics.InaccessibleModule, location, name, module.IssueDetail),
            ModuleIssue.InheritsMap => Diagnostic.Create(EndpointModuleDiagnostics.InheritedMap, location, name, module.IssueDetail),
            _ => null,
        };
    }

    /// <summary>Renders the generated class; <paramref name="modules"/> are the mappable modules in call order.</summary>
    internal static string Render(ImmutableArray<EndpointModuleCandidate> modules)
    {
        var version = typeof(EndpointModuleGenerator).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
        var needsHelper = modules.Any(static module => module.CallsExplicitImplementation);

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace SharedKernel.Presentation.WebApi");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>Maps the endpoint modules declared in this assembly.</summary>");
        source.AppendLine($"    [global::System.CodeDom.Compiler.GeneratedCode(\"SharedKernel.Presentation.WebApi.Generators\", \"{version}\")]");
        source.AppendLine($"    internal static partial class {GeneratedClassName}");
        source.AppendLine("    {");
        source.AppendLine("        /// <summary>");
        source.AppendLine($"        /// Maps every <see cref=\"{ModuleInterface}\"/> declared in this assembly, in the ordinal order of their full names.");
        source.AppendLine("        /// </summary>");
        source.AppendLine("        /// <param name=\"app\">The application, or the route group, to map the endpoints on.</param>");
        source.AppendLine("        /// <returns><paramref name=\"app\"/>.</returns>");
        source.AppendLine($"        public static {RouteBuilder} MapEndpoints(this {RouteBuilder} app)");
        source.AppendLine("        {");
        source.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(app);");
        source.AppendLine();

        foreach (var module in modules)
        {
            source.AppendLine(module.CallsExplicitImplementation
                ? $"            MapModule<{module.FullName}>(app);"
                : $"            {module.FullName}.Map(app);");
        }

        if (modules.Length > 0)
        {
            source.AppendLine();
        }

        source.AppendLine("            return app;");
        source.AppendLine("        }");

        if (needsHelper)
        {
            source.AppendLine();
            source.AppendLine("        // Reaches a Map that the module implements explicitly, which cannot be called by the type's name.");
            source.AppendLine($"        private static void MapModule<TModule>({RouteBuilder} app)");
            source.AppendLine($"            where TModule : {ModuleInterface} =>");
            source.AppendLine("            TModule.Map(app);");
        }

        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }

    /// <summary>The names of the pipeline steps, for the caching tests.</summary>
    internal static class TrackingNames
    {
        public const string WebApiReferenced = nameof(WebApiReferenced);
        public const string Candidates = nameof(Candidates);
        public const string Modules = nameof(Modules);
        public const string Output = nameof(Output);
    }
}
