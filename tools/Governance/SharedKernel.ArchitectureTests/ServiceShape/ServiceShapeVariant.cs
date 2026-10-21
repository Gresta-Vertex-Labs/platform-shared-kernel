namespace SharedKernel.ArchitectureTests.ServiceShape;

/// <summary>Which project layout a consuming service uses.</summary>
public enum ServiceShapeVariant
{
    /// <summary>
    /// Four projects: Domain, Application, Infrastructure and a host (<c>Api</c> or <c>Worker</c>). The Application
    /// project references the Domain project only.
    /// </summary>
    FourProject,

    /// <summary>
    /// Six projects: the four of <see cref="FourProject"/> plus <c>Contracts</c> (the published integration contracts)
    /// and <c>Client</c> (the published typed REST client). The Application project references Domain and Contracts.
    /// </summary>
    SixProject,
}
