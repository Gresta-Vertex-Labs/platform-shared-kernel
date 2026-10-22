namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Names where each kind of type lives in a consuming service, for <see cref="ServicePlacementRules"/>. The service
/// prefix, the project suffixes, every folder (namespace segment) and every anchor type are parameters with the
/// kernel's conventions as defaults: a service that keeps another layout overrides the property, never the rule.
/// </summary>
public sealed class ServicePlacementOptions
{
    /// <summary>
    /// Folder names that say nothing about what they hold. The default is the list the service template ships; it
    /// is only a default, so a caller adds or removes names by passing its own.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultBannedSegments = Array.AsReadOnly(
        ["Managers", "Helpers", "Utils", "Extensions", "Exceptions", "Shared", "Misc"]
    );

    /// <summary>Creates the options for the service whose root namespace is <paramref name="service"/>.</summary>
    /// <param name="service">The service prefix of every project and root namespace, e.g. <c>Shop.Catalog</c>.</param>
    public ServicePlacementOptions(string service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        Service = service;
    }

    /// <summary>The service prefix of every project and root namespace.</summary>
    public string Service { get; }

    /// <summary>Suffix of the Domain project's root namespace. Defaults to <c>Domain</c>.</summary>
    public string DomainSuffix { get; init; } = "Domain";

    /// <summary>Suffix of the Application project's root namespace. Defaults to <c>Application</c>.</summary>
    public string ApplicationSuffix { get; init; } = "Application";

    /// <summary>Suffix of the Infrastructure project's root namespace. Defaults to <c>Infrastructure</c>.</summary>
    public string InfrastructureSuffix { get; init; } = "Infrastructure";

    /// <summary>Suffix of the host project's root namespace: <c>Api</c> (default) or <c>Worker</c>.</summary>
    public string HostSuffix { get; init; } = "Api";

    /// <summary>Domain folders that hold shared kinds rather than a feature. Defaults to <c>Common</c> and <c>Services</c>.</summary>
    public IReadOnlyList<string> DomainSharedFolders { get; init; } = ["Common", "Services"];

    /// <summary>The folder of business rules inside a domain feature. Defaults to <c>Rules</c>.</summary>
    public string RulesFolder { get; init; } = "Rules";

    /// <summary>The folder of specifications inside a domain feature. Defaults to <c>Specifications</c>.</summary>
    public string SpecificationsFolder { get; init; } = "Specifications";

    /// <summary>The folder of domain events inside a domain feature. Defaults to <c>Events</c>.</summary>
    public string EventsFolder { get; init; } = "Events";

    /// <summary>The folder of commands inside an application feature. Defaults to <c>Commands</c>.</summary>
    public string CommandsFolder { get; init; } = "Commands";

    /// <summary>The folder of queries inside an application feature. Defaults to <c>Queries</c>.</summary>
    public string QueriesFolder { get; init; } = "Queries";

    /// <summary>The type-name suffix of a command; the rest of the name is the use case. Defaults to <c>Command</c>.</summary>
    public string CommandSuffix { get; init; } = "Command";

    /// <summary>The type-name suffix of a query; the rest of the name is the use case. Defaults to <c>Query</c>.</summary>
    public string QuerySuffix { get; init; } = "Query";

    /// <summary>The folder of domain-event handlers inside an application feature. Defaults to <c>EventHandlers</c>.</summary>
    public string EventHandlersFolder { get; init; } = "EventHandlers";

    /// <summary>The Infrastructure namespace of EF Core entity configurations. Defaults to <c>Persistence.Configurations</c>.</summary>
    public string ConfigurationsNamespace { get; init; } = "Persistence.Configurations";

    /// <summary>The Infrastructure namespace of message consumers. Defaults to <c>Messaging.Consumers</c>.</summary>
    public string ConsumersNamespace { get; init; } = "Messaging.Consumers";

    /// <summary>The host namespace of endpoint modules. Defaults to <c>Endpoints</c>.</summary>
    public string EndpointsFolder { get; init; } = "Endpoints";

    /// <summary>The host namespace of middleware. Defaults to <c>Middleware</c>.</summary>
    public string MiddlewareFolder { get; init; } = "Middleware";

    /// <summary>The host namespace of endpoint filters. Defaults to <c>Filters</c>.</summary>
    public string FiltersFolder { get; init; } = "Filters";

    /// <summary>Namespace segments no type may live under. Defaults to <see cref="DefaultBannedSegments"/>.</summary>
    public IReadOnlyList<string> BannedSegments { get; init; } = DefaultBannedSegments;

    /// <summary>
    /// Metadata full names of exception base types a service type may derive from (for example the kernel's
    /// <c>SharedKernelException</c>, which classified failures derive from). Empty by default: no type derives from
    /// <c>System.Exception</c> at all.
    /// </summary>
    public IReadOnlyList<string> PermittedExceptionBaseTypes { get; init; } = [];

    /// <summary>Anchor of aggregate roots (open generic metadata name). Defaults to <see cref="KernelTypeNames.AggregateRoot"/>.</summary>
    public string AggregateRootType { get; init; } = KernelTypeNames.AggregateRoot;

    /// <summary>Anchor of business rules. Defaults to <see cref="KernelTypeNames.BusinessRule"/>.</summary>
    public string BusinessRuleType { get; init; } = KernelTypeNames.BusinessRule;

    /// <summary>Anchor of specifications. Defaults to <see cref="KernelTypeNames.Specification"/>.</summary>
    public string SpecificationType { get; init; } = KernelTypeNames.Specification;

    /// <summary>Anchor of domain events. Defaults to <see cref="KernelTypeNames.DomainEvent"/>.</summary>
    public string DomainEventType { get; init; } = KernelTypeNames.DomainEvent;

    /// <summary>Marker of commands. Defaults to <see cref="KernelTypeNames.Command"/>.</summary>
    public string CommandType { get; init; } = KernelTypeNames.Command;

    /// <summary>Marker of queries. Defaults to <see cref="KernelTypeNames.Query"/>.</summary>
    public string QueryType { get; init; } = KernelTypeNames.Query;

    /// <summary>Anchor of request handlers. Defaults to <see cref="KernelTypeNames.RequestHandler"/>.</summary>
    public string RequestHandlerType { get; init; } = KernelTypeNames.RequestHandler;

    /// <summary>Anchor of request validators. Defaults to <see cref="KernelTypeNames.RequestValidator"/>.</summary>
    public string RequestValidatorType { get; init; } = KernelTypeNames.RequestValidator;

    /// <summary>Anchor of domain-event handlers. Defaults to <see cref="KernelTypeNames.DomainEventHandler"/>.</summary>
    public string DomainEventHandlerType { get; init; } = KernelTypeNames.DomainEventHandler;

    /// <summary>Anchor of EF Core entity configurations. Defaults to <see cref="KernelTypeNames.EntityTypeConfiguration"/>.</summary>
    public string EntityConfigurationType { get; init; } = KernelTypeNames.EntityTypeConfiguration;

    /// <summary>Anchor of message consumers. Defaults to <see cref="KernelTypeNames.Consumer"/>.</summary>
    public string ConsumerType { get; init; } = KernelTypeNames.Consumer;

    /// <summary>Anchor of endpoint modules. Defaults to <see cref="KernelTypeNames.EndpointModule"/>.</summary>
    public string EndpointModuleType { get; init; } = KernelTypeNames.EndpointModule;

    /// <summary>Anchor of factory-based middleware. Defaults to <see cref="KernelTypeNames.Middleware"/>.</summary>
    public string MiddlewareType { get; init; } = KernelTypeNames.Middleware;

    /// <summary>Anchor of endpoint filters. Defaults to <see cref="KernelTypeNames.EndpointFilter"/>.</summary>
    public string EndpointFilterType { get; init; } = KernelTypeNames.EndpointFilter;

    /// <summary>First parameter type of a conventional middleware's <c>Invoke</c>/<c>InvokeAsync</c>. Defaults to <see cref="KernelTypeNames.HttpContext"/>.</summary>
    public string HttpContextType { get; init; } = KernelTypeNames.HttpContext;

    /// <summary>The Domain root namespace.</summary>
    public string Domain => $"{Service}.{DomainSuffix}";

    /// <summary>The Application root namespace.</summary>
    public string Application => $"{Service}.{ApplicationSuffix}";

    /// <summary>The Infrastructure root namespace.</summary>
    public string Infrastructure => $"{Service}.{InfrastructureSuffix}";

    /// <summary>The host root namespace.</summary>
    public string Host => $"{Service}.{HostSuffix}";
}
