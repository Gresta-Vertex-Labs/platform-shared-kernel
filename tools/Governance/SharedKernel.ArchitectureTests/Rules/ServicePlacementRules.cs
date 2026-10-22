using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;
using static SharedKernel.ArchitectureTests.Predicates.ServiceTypeFacts;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Where each kind of type lives in a consuming service, checked by namespace (the namespace equals the folder).
/// Types are selected by what they implement or derive from — matched by metadata full name, never by their own name —
/// so a misplaced type cannot hide behind a convenient name. Namespaces are compared by segment, never by prefix
/// (<c>Rules</c> does not match <c>RulesEngine</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every factory takes the service's assemblies and a <see cref="ServicePlacementOptions"/> that carries the service
/// prefix, every folder and every anchor name, and returns a <see cref="ConditionList"/> for
/// <see cref="Helpers.ArchitectureRuleBase.AssertRule"/> (or <c>GetResult()</c>). The package references no kernel,
/// MassTransit, EF Core or ASP.NET Core assembly: <see cref="KernelTypeNames"/> holds the metadata names the options
/// default to, so a rule whose anchor is absent from the supplied assemblies simply selects nothing there. A rule is
/// opt-in; nothing here runs until a test calls it.
/// </para>
/// <para>
/// The rules mirror the service template's tested placement rules, so a service generated from it and a service that
/// calls these factories agree on where a type lives.
/// </para>
/// </remarks>
public static class ServicePlacementRules
{
    private static readonly string[] InvocationMethodNames = ["Invoke", "InvokeAsync"];

    /// <summary>
    /// Aggregate roots live in <c>Domain.{Feature}</c>, business rules in <c>Domain.{Feature}.Rules</c>,
    /// specifications in <c>Domain.{Feature}.Specifications</c> and domain events in <c>Domain.{Feature}.Events</c>.
    /// <see cref="ServicePlacementOptions.DomainSharedFolders"/> are not features.
    /// </summary>
    /// <param name="assemblies">Every assembly of the service whose types are judged.</param>
    /// <param name="options">The service's namespaces and anchor names.</param>
    /// <returns>A rule failing for each misplaced type.</returns>
    public static ConditionList DomainTypesLiveInTheirFeature(IEnumerable<Assembly> assemblies, ServicePlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        bool InFeature(string ns, string? folder)
        {
            string[] segments = SegmentsBelow(ns, options.Domain);
            return segments.Length == (folder is null ? 1 : 2)
                && !options.DomainSharedFolders.Contains(segments[0], StringComparer.Ordinal)
                && (folder is null || segments[1] == folder);
        }

        return ServiceTypeRule.Build(
            assemblies,
            type =>
                IsConcrete(type)
                && (
                    Implements(type, options.AggregateRootType)
                    || Implements(type, options.BusinessRuleType)
                    || Implements(type, options.SpecificationType)
                    || Implements(type, options.DomainEventType)
                ),
            type =>
            {
                string ns = type.Namespace ?? string.Empty;
                return (!Implements(type, options.AggregateRootType) || InFeature(ns, null))
                    && (!Implements(type, options.BusinessRuleType) || InFeature(ns, options.RulesFolder))
                    && (!Implements(type, options.SpecificationType) || InFeature(ns, options.SpecificationsFolder))
                    && (!Implements(type, options.DomainEventType) || InFeature(ns, options.EventsFolder));
            }
        );
    }

    /// <summary>
    /// A command or query is named <c>{UseCase}Command</c>/<c>{UseCase}Query</c> and lives in
    /// <c>Application.{Feature}.Commands.{UseCase}</c>/<c>Application.{Feature}.Queries.{UseCase}</c>, one per
    /// namespace; its handler and its validator live in the namespace of the request they serve.
    /// </summary>
    /// <param name="assemblies">Every assembly of the service whose types are judged.</param>
    /// <param name="options">The service's namespaces and anchor names.</param>
    /// <returns>A rule failing for each misplaced request, handler or validator.</returns>
    public static ConditionList UseCasesLiveInTheirOwnNamespace(IEnumerable<Assembly> assemblies, ServicePlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(options);
        var assemblyList = assemblies.ToList();

        bool IsRequest(Type type) =>
            IsConcrete(type) && (Implements(type, options.CommandType) || Implements(type, options.QueryType));
        var crowded = assemblyList
            .SelectMany(a => a.GetTypes())
            .Where(IsRequest)
            .GroupBy(type => type.Namespace, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key!)
            .ToHashSet(StringComparer.Ordinal);

        bool RequestIsPlaced(Type type, string folder, string suffix)
        {
            string ns = type.Namespace ?? string.Empty;
            string useCase = type.Name.EndsWith(suffix, StringComparison.Ordinal) ? type.Name[..^suffix.Length] : string.Empty;
            string feature = SegmentsBelow(ns, options.Application).FirstOrDefault() ?? "{Feature}";
            return useCase.Length > 0
                && ns == $"{options.Application}.{feature}.{folder}.{useCase}"
                && !crowded.Contains(ns);
        }

        return ServiceTypeRule.Build(
            assemblyList,
            type =>
                IsRequest(type)
                || (
                    IsConcrete(type)
                    && (Implements(type, options.RequestHandlerType) || Implements(type, options.RequestValidatorType))
                ),
            type =>
                (!Implements(type, options.CommandType) || RequestIsPlaced(type, options.CommandsFolder, options.CommandSuffix))
                && (!Implements(type, options.QueryType) || RequestIsPlaced(type, options.QueriesFolder, options.QuerySuffix))
                && ClosingArguments(type, options.RequestHandlerType)
                    .Concat(ClosingArguments(type, options.RequestValidatorType))
                    .All(request => request.Namespace == type.Namespace)
        );
    }

    /// <summary>Domain-event handlers live in <c>Application.{Feature}.EventHandlers</c>.</summary>
    /// <param name="assemblies">Every assembly of the service whose types are judged.</param>
    /// <param name="options">The service's namespaces and anchor names.</param>
    /// <returns>A rule failing for each misplaced handler.</returns>
    public static ConditionList DomainEventHandlersLiveInEventHandlers(IEnumerable<Assembly> assemblies, ServicePlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ServiceTypeRule.Build(
            assemblies,
            type => IsConcrete(type) && Implements(type, options.DomainEventHandlerType),
            type =>
            {
                string[] segments = SegmentsBelow(type.Namespace ?? string.Empty, options.Application);
                return segments.Length == 2 && segments[1] == options.EventHandlersFolder;
            }
        );
    }

    /// <summary>EF Core entity configurations live in <c>Infrastructure.Persistence.Configurations</c>.</summary>
    /// <param name="assemblies">Every assembly of the service whose types are judged.</param>
    /// <param name="options">The service's namespaces and anchor names.</param>
    /// <returns>A rule failing for each misplaced configuration.</returns>
    public static ConditionList EntityConfigurationsLiveInConfigurations(IEnumerable<Assembly> assemblies, ServicePlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ExactlyIn(assemblies, options.EntityConfigurationType, $"{options.Infrastructure}.{options.ConfigurationsNamespace}");
    }

    /// <summary>Message consumers live in <c>Infrastructure.Messaging.Consumers</c>.</summary>
    /// <param name="assemblies">Every assembly of the service whose types are judged.</param>
    /// <param name="options">The service's namespaces and anchor names.</param>
    /// <returns>A rule failing for each misplaced consumer.</returns>
    public static ConditionList ConsumersLiveInConsumers(IEnumerable<Assembly> assemblies, ServicePlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ExactlyIn(assemblies, options.ConsumerType, $"{options.Infrastructure}.{options.ConsumersNamespace}");
    }

    /// <summary>Endpoint modules live in <c>Api.Endpoints</c>.</summary>
    /// <param name="assemblies">Every assembly of the service whose types are judged.</param>
    /// <param name="options">The service's namespaces and anchor names.</param>
    /// <returns>A rule failing for each misplaced endpoint module.</returns>
    public static ConditionList EndpointModulesLiveInEndpoints(IEnumerable<Assembly> assemblies, ServicePlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ExactlyIn(assemblies, options.EndpointModuleType, $"{options.Host}.{options.EndpointsFolder}");
    }

    /// <summary>
    /// Middleware lives in <c>Api.Middleware</c> and endpoint filters in <c>Api.Filters</c>. A middleware is a type
    /// implementing the factory-based middleware interface or declaring a public <c>Invoke</c>/<c>InvokeAsync</c> whose
    /// first parameter is the HTTP context.
    /// </summary>
    /// <param name="assemblies">Every assembly of the service whose types are judged.</param>
    /// <param name="options">The service's namespaces and anchor names.</param>
    /// <returns>A rule failing for each misplaced middleware or filter.</returns>
    public static ConditionList MiddlewareAndFiltersLiveInTheirFolders(IEnumerable<Assembly> assemblies, ServicePlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        bool IsMiddleware(Type type) =>
            Implements(type, options.MiddlewareType)
            || type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(method =>
                    InvocationMethodNames.Contains(method.Name, StringComparer.Ordinal)
                    && method.GetParameters().FirstOrDefault()?.ParameterType.FullName == options.HttpContextType
                );

        return ServiceTypeRule.Build(
            assemblies,
            type => IsConcrete(type) && (IsMiddleware(type) || Implements(type, options.EndpointFilterType)),
            type =>
                (!IsMiddleware(type) || type.Namespace == $"{options.Host}.{options.MiddlewareFolder}")
                && (!Implements(type, options.EndpointFilterType) || type.Namespace == $"{options.Host}.{options.FiltersFolder}")
        );
    }

    /// <summary>
    /// No namespace has a segment named in <see cref="ServicePlacementOptions.BannedSegments"/> (by default
    /// <c>Managers</c>, <c>Helpers</c>, <c>Utils</c>, <c>Extensions</c>, <c>Exceptions</c>, <c>Shared</c>, <c>Misc</c>):
    /// name the folder after what it holds. Interfaces are judged too.
    /// </summary>
    /// <param name="assemblies">Every assembly of the service whose types are judged.</param>
    /// <param name="options">The service's namespaces and banned segments.</param>
    /// <returns>A rule failing for each type under a banned segment.</returns>
    public static ConditionList NoGenericFolderNames(IEnumerable<Assembly> assemblies, ServicePlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ServiceTypeRule.Build(
            assemblies,
            IsEligible,
            type => !(type.Namespace ?? string.Empty).Split('.').Intersect(options.BannedSegments, StringComparer.Ordinal).Any()
        );
    }

    /// <summary>
    /// No type derives from <c>System.Exception</c>: expected failures are results and invariants use rule checks. A
    /// type deriving from one of <see cref="ServicePlacementOptions.PermittedExceptionBaseTypes"/> (for example the
    /// kernel's classified exception) is allowed.
    /// </summary>
    /// <param name="assemblies">Every assembly of the service whose types are judged.</param>
    /// <param name="options">The service's namespaces and permitted exception bases.</param>
    /// <returns>A rule failing for each exception type.</returns>
    public static ConditionList NoExceptionTypes(IEnumerable<Assembly> assemblies, ServicePlacementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ServiceTypeRule.Build(
            assemblies,
            type => IsConcrete(type) && BaseTypeNames(type).Contains(KernelTypeNames.Exception, StringComparer.Ordinal),
            type => BaseTypeNames(type).Intersect(options.PermittedExceptionBaseTypes, StringComparer.Ordinal).Any()
        );
    }

    private static ConditionList ExactlyIn(IEnumerable<Assembly> assemblies, string anchor, string expectedNamespace) =>
        ServiceTypeRule.Build(
            assemblies,
            type => IsConcrete(type) && Implements(type, anchor),
            type => type.Namespace == expectedNamespace
        );
}
