namespace SharedKernel.ArchitectureTests.ServiceShape;

/// <summary>
/// The tier names of the kernel packages, spelled as the kernel's package index spells them
/// (<see cref="KernelPackageIndex.TierOf"/> returns one of these, or <see cref="KernelPackageIndex.ThirdParty"/>).
/// </summary>
public static class KernelTier
{
    /// <summary>Referenced from any project of a service.</summary>
    public const string Foundation = "Foundation";

    /// <summary>Referenced from the Domain project.</summary>
    public const string Model = "Model";

    /// <summary>Referenced from the Application project.</summary>
    public const string Abstractions = "Abstractions";

    /// <summary>Referenced from the Infrastructure project.</summary>
    public const string Adapter = "Adapter";

    /// <summary>Referenced from the Api or Worker project.</summary>
    public const string Host = "Host";

    /// <summary>Referenced from test projects only.</summary>
    public const string Testing = "Testing";

    /// <summary>Referenced by the build only (analyzers, architecture tests, linter).</summary>
    public const string Tooling = "Tooling";
}
