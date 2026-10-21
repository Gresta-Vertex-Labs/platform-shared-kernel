namespace SharedKernel.ArchitectureTests.ServiceShape;

/// <summary>
/// Names a consuming service's projects. Project names derive from the service prefix and one suffix per role
/// (<c>{Service}.Domain</c>, <c>{Service}.Api</c>, …); every suffix is overridable and the host is <c>Api</c> by default
/// (<c>Worker</c> for a service without an HTTP surface).
/// </summary>
public sealed class ServiceShapeOptions
{
    /// <summary>Creates the options for the service whose projects are named <c>{service}.{Role}</c>.</summary>
    /// <param name="service">The service prefix, e.g. <c>Shop.Catalog</c>.</param>
    public ServiceShapeOptions(string service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        Service = service;
    }

    /// <summary>The service prefix of every project name.</summary>
    public string Service { get; }

    /// <summary>The project layout. Defaults to <see cref="ServiceShapeVariant.FourProject"/>.</summary>
    public ServiceShapeVariant Variant { get; init; } = ServiceShapeVariant.FourProject;

    /// <summary>Suffix of the Domain project. Defaults to <c>Domain</c>.</summary>
    public string DomainSuffix { get; init; } = "Domain";

    /// <summary>Suffix of the Contracts project (six-project variant). Defaults to <c>Contracts</c>.</summary>
    public string ContractsSuffix { get; init; } = "Contracts";

    /// <summary>Suffix of the Application project. Defaults to <c>Application</c>.</summary>
    public string ApplicationSuffix { get; init; } = "Application";

    /// <summary>Suffix of the Infrastructure project. Defaults to <c>Infrastructure</c>.</summary>
    public string InfrastructureSuffix { get; init; } = "Infrastructure";

    /// <summary>Suffix of the composition-root project: <c>Api</c> (default) or <c>Worker</c>.</summary>
    public string HostSuffix { get; init; } = "Api";

    /// <summary>Suffix of the Client project (six-project variant). Defaults to <c>Client</c>.</summary>
    public string ClientSuffix { get; init; } = "Client";

    /// <summary>
    /// Projects outside the service that its projects may legitimately reference (for example a platform-wide
    /// <c>Shop.Contracts</c>). They are ignored when a rule compares a project's direct project references.
    /// </summary>
    public IReadOnlyList<string> SharedProjects { get; init; } = [];

    /// <summary>The Domain project name.</summary>
    public string Domain => $"{Service}.{DomainSuffix}";

    /// <summary>The Contracts project name (six-project variant).</summary>
    public string Contracts => $"{Service}.{ContractsSuffix}";

    /// <summary>The Application project name.</summary>
    public string Application => $"{Service}.{ApplicationSuffix}";

    /// <summary>The Infrastructure project name.</summary>
    public string Infrastructure => $"{Service}.{InfrastructureSuffix}";

    /// <summary>The composition-root project name.</summary>
    public string Host => $"{Service}.{HostSuffix}";

    /// <summary>The Client project name (six-project variant).</summary>
    public string Client => $"{Service}.{ClientSuffix}";

    /// <summary>The projects of the service that ship, for the chosen <see cref="Variant"/>.</summary>
    public IReadOnlyList<string> ProductionProjects =>
        Variant == ServiceShapeVariant.SixProject
            ? [Domain, Contracts, Application, Infrastructure, Host, Client]
            : [Domain, Application, Infrastructure, Host];
}
