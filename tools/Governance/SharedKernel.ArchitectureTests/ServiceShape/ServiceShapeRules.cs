using SharedKernel.ArchitectureTests.Helpers;

namespace SharedKernel.ArchitectureTests.ServiceShape;

/// <summary>
/// The rules of a consuming service's own projects, checked against the real restore graph of its test assembly:
/// what the Domain, Application, Infrastructure and Api (or Worker) projects — and, in the six-project variant, the
/// Contracts and Client projects — may reference. The kernel's tier check (MSBuild) judges edges between kernel
/// packages; these rules judge which tiers each project of a <em>service</em> may reach, which the kernel cannot see.
/// </summary>
/// <remarks>
/// <para>
/// Every rule comes in two forms. <c>Find…</c> returns the findings (empty when the rule holds), so any assertion
/// library can report them; <c>Assert…</c> throws one <see cref="ArchitectureRuleViolationException"/> listing them,
/// which every test runner reports as a failure. A rule whose project is absent from the graph (a mistyped service
/// prefix or suffix) reports that as a finding instead of passing vacuously.
/// </para>
/// <para>
/// Every rule is opt-in: nothing runs until a test calls it, and the kernel's package tiers come from the embedded
/// <see cref="KernelPackageIndex"/>. <c>FindAll</c>/<c>AssertAll</c> run every rule that applies to the variant.
/// </para>
/// </remarks>
public static class ServiceShapeRules
{
    // ---------------------------------------------------------------------------------------------------------------
    // 1. Project references follow the layering
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Domain references no project; Application references Domain (and Contracts in the six-project variant);
    /// Infrastructure references Application only; the host references Application and Infrastructure. Projects named
    /// in <see cref="ServiceShapeOptions.SharedProjects"/> are ignored.
    /// </summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <returns>The findings; empty when the rule holds.</returns>
    public static IReadOnlyList<string> FindProjectReferencesFollowTheLayering(
        ServiceDependencyGraph graph,
        ServiceShapeOptions options
    )
    {
        var findings = Start(graph, options, [options.Domain, options.Application, options.Infrastructure, options.Host]);
        if (findings.Count > 0)
        {
            return findings;
        }

        string[] applicationReferences =
            options.Variant == ServiceShapeVariant.SixProject
                ? [options.Domain, options.Contracts]
                : [options.Domain];
        ExpectEqual(findings, Own(graph, options, options.Domain), [], $"{options.Domain}'s project references");
        ExpectEqual(findings, Own(graph, options, options.Application), applicationReferences, $"{options.Application}'s project references");
        ExpectEqual(findings, Own(graph, options, options.Infrastructure), [options.Application], $"{options.Infrastructure}'s project references");
        ExpectEqual(findings, Own(graph, options, options.Host), [options.Application, options.Infrastructure], $"{options.Host}'s project references");
        return findings;
    }

    /// <summary>Throws when <see cref="FindProjectReferencesFollowTheLayering"/> reports a finding.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <exception cref="ArchitectureRuleViolationException">The rule is violated.</exception>
    public static void AssertProjectReferencesFollowTheLayering(ServiceDependencyGraph graph, ServiceShapeOptions options) =>
        Throw("ProjectReferencesFollowTheLayering", FindProjectReferencesFollowTheLayering(graph, options));

    // ---------------------------------------------------------------------------------------------------------------
    // 2. Domain sees Foundation and Model only
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The kernel packages the Domain project reaches (transitively) are Foundation or Model tier only.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <returns>The findings; empty when the rule holds.</returns>
    public static IReadOnlyList<string> FindDomainSeesFoundationAndModelOnly(
        ServiceDependencyGraph graph,
        ServiceShapeOptions options
    )
    {
        var findings = Start(graph, options, [options.Domain]);
        if (findings.Count == 0)
        {
            ExpectOnly(findings, KernelTiersOf(graph.Closure(options.Domain)), [KernelTier.Foundation, KernelTier.Model], $"the kernel tiers {options.Domain} reaches");
        }

        return findings;
    }

    /// <summary>Throws when <see cref="FindDomainSeesFoundationAndModelOnly"/> reports a finding.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <exception cref="ArchitectureRuleViolationException">The rule is violated.</exception>
    public static void AssertDomainSeesFoundationAndModelOnly(ServiceDependencyGraph graph, ServiceShapeOptions options) =>
        Throw("DomainSeesFoundationAndModelOnly", FindDomainSeesFoundationAndModelOnly(graph, options));

    // ---------------------------------------------------------------------------------------------------------------
    // 3. Contracts are pure (six-project variant)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Six-project variant: the Contracts project references no project and only <c>SharedKernel.Contracts</c>, and
    /// the Domain and Contracts projects never reach each other.
    /// </summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names (<see cref="ServiceShapeOptions.Variant"/> must be six-project).</param>
    /// <returns>The findings; empty when the rule holds.</returns>
    /// <exception cref="InvalidOperationException">The variant is not <see cref="ServiceShapeVariant.SixProject"/>.</exception>
    public static IReadOnlyList<string> FindContractsArePure(ServiceDependencyGraph graph, ServiceShapeOptions options)
    {
        RequireSixProject(options, nameof(FindContractsArePure));
        var findings = Start(graph, options, [options.Domain, options.Contracts]);
        if (findings.Count > 0)
        {
            return findings;
        }

        ExpectEqual(findings, Own(graph, options, options.Contracts), [], $"{options.Contracts}'s project references");
        ExpectEqual(findings, graph.DirectKernelPackages(options.Contracts), [ContractsPackage], $"{options.Contracts}'s kernel packages");
        ExpectEmpty(findings, graph.Closure(options.Domain).Where(p => p == options.Contracts), $"{options.Domain} must never reach {options.Contracts}");
        ExpectEmpty(findings, graph.Closure(options.Contracts).Where(p => p == options.Domain), $"{options.Contracts} must never reach {options.Domain}");
        return findings;
    }

    /// <summary>Throws when <see cref="FindContractsArePure"/> reports a finding.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names (six-project variant).</param>
    /// <exception cref="ArchitectureRuleViolationException">The rule is violated.</exception>
    /// <exception cref="InvalidOperationException">The variant is not <see cref="ServiceShapeVariant.SixProject"/>.</exception>
    public static void AssertContractsArePure(ServiceDependencyGraph graph, ServiceShapeOptions options) =>
        Throw("ContractsArePure", FindContractsArePure(graph, options));

    // ---------------------------------------------------------------------------------------------------------------
    // 4. Application sees contracts only
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Handlers are written against contracts: the kernel packages the Application project reaches are Foundation,
    /// Model or Abstractions tier, and MediatR is never reached (send through <c>ISender</c>).
    /// </summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <returns>The findings; empty when the rule holds.</returns>
    public static IReadOnlyList<string> FindApplicationSeesContractsOnly(
        ServiceDependencyGraph graph,
        ServiceShapeOptions options
    )
    {
        var findings = Start(graph, options, [options.Application]);
        if (findings.Count > 0)
        {
            return findings;
        }

        var closure = graph.Closure(options.Application);
        ExpectOnly(findings, KernelTiersOf(closure), [KernelTier.Foundation, KernelTier.Model, KernelTier.Abstractions], $"the kernel tiers {options.Application} reaches");
        ExpectEmpty(findings, closure.Where(p => p.StartsWith(MediatRPrefix, StringComparison.Ordinal)), $"{options.Application} must never reach MediatR (send through ISender)");
        return findings;
    }

    /// <summary>Throws when <see cref="FindApplicationSeesContractsOnly"/> reports a finding.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <exception cref="ArchitectureRuleViolationException">The rule is violated.</exception>
    public static void AssertApplicationSeesContractsOnly(ServiceDependencyGraph graph, ServiceShapeOptions options) =>
        Throw("ApplicationSeesContractsOnly", FindApplicationSeesContractsOnly(graph, options));

    // ---------------------------------------------------------------------------------------------------------------
    // 5. Infrastructure sees adapters but no host
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The Infrastructure project adds adapters (and Foundation helpers), never a host package: its direct kernel
    /// packages are Adapter or Foundation tier and everything it reaches is Foundation, Model, Abstractions or Adapter.
    /// </summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <returns>The findings; empty when the rule holds.</returns>
    public static IReadOnlyList<string> FindInfrastructureSeesAdaptersButNoHost(
        ServiceDependencyGraph graph,
        ServiceShapeOptions options
    )
    {
        var findings = Start(graph, options, [options.Infrastructure]);
        if (findings.Count > 0)
        {
            return findings;
        }

        var index = KernelPackageIndex.Instance;
        ExpectOnly(findings, graph.DirectKernelPackages(options.Infrastructure).Select(index.TierOf), [KernelTier.Adapter, KernelTier.Foundation], $"the tiers of {options.Infrastructure}'s direct kernel packages");
        ExpectOnly(findings, KernelTiersOf(graph.Closure(options.Infrastructure)), [KernelTier.Foundation, KernelTier.Model, KernelTier.Abstractions, KernelTier.Adapter], $"the kernel tiers {options.Infrastructure} reaches");
        return findings;
    }

    /// <summary>Throws when <see cref="FindInfrastructureSeesAdaptersButNoHost"/> reports a finding.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <exception cref="ArchitectureRuleViolationException">The rule is violated.</exception>
    public static void AssertInfrastructureSeesAdaptersButNoHost(ServiceDependencyGraph graph, ServiceShapeOptions options) =>
        Throw("InfrastructureSeesAdaptersButNoHost", FindInfrastructureSeesAdaptersButNoHost(graph, options));

    // ---------------------------------------------------------------------------------------------------------------
    // 6. Host is the composition root
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>The host references Host-tier kernel packages directly; everything else arrives through its projects.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <returns>The findings; empty when the rule holds.</returns>
    public static IReadOnlyList<string> FindHostIsTheCompositionRoot(
        ServiceDependencyGraph graph,
        ServiceShapeOptions options
    )
    {
        var findings = Start(graph, options, [options.Host]);
        if (findings.Count == 0)
        {
            ExpectOnly(findings, graph.DirectKernelPackages(options.Host).Select(KernelPackageIndex.Instance.TierOf), [KernelTier.Host], $"the tiers of {options.Host}'s direct kernel packages");
        }

        return findings;
    }

    /// <summary>Throws when <see cref="FindHostIsTheCompositionRoot"/> reports a finding.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <exception cref="ArchitectureRuleViolationException">The rule is violated.</exception>
    public static void AssertHostIsTheCompositionRoot(ServiceDependencyGraph graph, ServiceShapeOptions options) =>
        Throw("HostIsTheCompositionRoot", FindHostIsTheCompositionRoot(graph, options));

    // ---------------------------------------------------------------------------------------------------------------
    // 7. Client sees contracts and the REST client only (six-project variant)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Six-project variant: the Client project references the Contracts project only, and only the
    /// <c>SharedKernel.Communication.Rest</c> kernel package.
    /// </summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names (<see cref="ServiceShapeOptions.Variant"/> must be six-project).</param>
    /// <returns>The findings; empty when the rule holds.</returns>
    /// <exception cref="InvalidOperationException">The variant is not <see cref="ServiceShapeVariant.SixProject"/>.</exception>
    public static IReadOnlyList<string> FindClientSeesContractsAndRestOnly(
        ServiceDependencyGraph graph,
        ServiceShapeOptions options
    )
    {
        RequireSixProject(options, nameof(FindClientSeesContractsAndRestOnly));
        var findings = Start(graph, options, [options.Client]);
        if (findings.Count > 0)
        {
            return findings;
        }

        ExpectEqual(findings, Own(graph, options, options.Client), [options.Contracts], $"{options.Client}'s project references");
        ExpectEqual(findings, graph.DirectKernelPackages(options.Client), [RestClientPackage], $"{options.Client}'s kernel packages");
        return findings;
    }

    /// <summary>Throws when <see cref="FindClientSeesContractsAndRestOnly"/> reports a finding.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names (six-project variant).</param>
    /// <exception cref="ArchitectureRuleViolationException">The rule is violated.</exception>
    /// <exception cref="InvalidOperationException">The variant is not <see cref="ServiceShapeVariant.SixProject"/>.</exception>
    public static void AssertClientSeesContractsAndRestOnly(ServiceDependencyGraph graph, ServiceShapeOptions options) =>
        Throw("ClientSeesContractsAndRestOnly", FindClientSeesContractsAndRestOnly(graph, options));

    // ---------------------------------------------------------------------------------------------------------------
    // 8. Nothing references the host
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>No other production project of the service reaches the host project.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <returns>The findings; empty when the rule holds.</returns>
    public static IReadOnlyList<string> FindNothingReferencesTheHost(
        ServiceDependencyGraph graph,
        ServiceShapeOptions options
    )
    {
        var findings = Start(graph, options, options.ProductionProjects);
        if (findings.Count > 0)
        {
            return findings;
        }

        foreach (string project in options.ProductionProjects.Where(p => p != options.Host))
        {
            ExpectEmpty(findings, graph.Closure(project).Where(p => p == options.Host), $"{project} must never reach {options.Host}");
        }

        return findings;
    }

    /// <summary>Throws when <see cref="FindNothingReferencesTheHost"/> reports a finding.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <exception cref="ArchitectureRuleViolationException">The rule is violated.</exception>
    public static void AssertNothingReferencesTheHost(ServiceDependencyGraph graph, ServiceShapeOptions options) =>
        Throw("NothingReferencesTheHost", FindNothingReferencesTheHost(graph, options));

    // ---------------------------------------------------------------------------------------------------------------
    // 9. Production never references Testing packages
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>No production project of the service reaches a Testing-tier kernel package.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <returns>The findings; empty when the rule holds.</returns>
    public static IReadOnlyList<string> FindProductionNeverReferencesTestingPackages(
        ServiceDependencyGraph graph,
        ServiceShapeOptions options
    )
    {
        var findings = Start(graph, options, options.ProductionProjects);
        if (findings.Count > 0)
        {
            return findings;
        }

        var index = KernelPackageIndex.Instance;
        foreach (string project in options.ProductionProjects)
        {
            ExpectEmpty(findings, graph.Closure(project).Where(p => index.TierOf(p) == KernelTier.Testing), $"{project} must never reach a Testing-tier package");
        }

        return findings;
    }

    /// <summary>Throws when <see cref="FindProductionNeverReferencesTestingPackages"/> reports a finding.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <exception cref="ArchitectureRuleViolationException">The rule is violated.</exception>
    public static void AssertProductionNeverReferencesTestingPackages(ServiceDependencyGraph graph, ServiceShapeOptions options) =>
        Throw("ProductionNeverReferencesTestingPackages", FindProductionNeverReferencesTestingPackages(graph, options));

    // ---------------------------------------------------------------------------------------------------------------
    // 10. Every kernel package is known
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every <c>SharedKernel.*</c> package in the production graph is in the kernel package index: none is unknown or
    /// renamed (a stale index or a package from a newer kernel version than this assembly's).
    /// </summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <returns>The findings; empty when the rule holds.</returns>
    public static IReadOnlyList<string> FindEveryKernelPackageIsKnown(
        ServiceDependencyGraph graph,
        ServiceShapeOptions options
    )
    {
        var findings = Start(graph, options, options.ProductionProjects);
        if (findings.Count > 0)
        {
            return findings;
        }

        var index = KernelPackageIndex.Instance;
        ExpectEmpty(
            findings,
            options.ProductionProjects
                .SelectMany(graph.Closure)
                .Distinct(StringComparer.Ordinal)
                .Where(KernelPackageIndex.IsKernelPackage)
                .Where(p => index.TierOf(p) == KernelPackageIndex.ThirdParty)
                .Order(StringComparer.Ordinal),
            "every SharedKernel package in the production graph must be in the kernel package index"
        );
        return findings;
    }

    /// <summary>Throws when <see cref="FindEveryKernelPackageIsKnown"/> reports a finding.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <exception cref="ArchitectureRuleViolationException">The rule is violated.</exception>
    public static void AssertEveryKernelPackageIsKnown(ServiceDependencyGraph graph, ServiceShapeOptions options) =>
        Throw("EveryKernelPackageIsKnown", FindEveryKernelPackageIsKnown(graph, options));

    // ---------------------------------------------------------------------------------------------------------------
    // All rules
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs every rule that applies to <see cref="ServiceShapeOptions.Variant"/> (the Contracts and Client rules only
    /// for the six-project variant). Each finding is prefixed with its rule name.
    /// </summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <returns>The findings of all rules; empty when every rule holds.</returns>
    public static IReadOnlyList<string> FindAll(ServiceDependencyGraph graph, ServiceShapeOptions options)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(options);
        var rules = new List<(string Name, IReadOnlyList<string> Findings)>
        {
            ("ProjectReferencesFollowTheLayering", FindProjectReferencesFollowTheLayering(graph, options)),
            ("DomainSeesFoundationAndModelOnly", FindDomainSeesFoundationAndModelOnly(graph, options)),
            ("ApplicationSeesContractsOnly", FindApplicationSeesContractsOnly(graph, options)),
            ("InfrastructureSeesAdaptersButNoHost", FindInfrastructureSeesAdaptersButNoHost(graph, options)),
            ("HostIsTheCompositionRoot", FindHostIsTheCompositionRoot(graph, options)),
            ("NothingReferencesTheHost", FindNothingReferencesTheHost(graph, options)),
            ("ProductionNeverReferencesTestingPackages", FindProductionNeverReferencesTestingPackages(graph, options)),
            ("EveryKernelPackageIsKnown", FindEveryKernelPackageIsKnown(graph, options)),
        };
        if (options.Variant == ServiceShapeVariant.SixProject)
        {
            rules.Add(("ContractsArePure", FindContractsArePure(graph, options)));
            rules.Add(("ClientSeesContractsAndRestOnly", FindClientSeesContractsAndRestOnly(graph, options)));
        }

        return rules.SelectMany(rule => rule.Findings.Select(finding => $"{rule.Name}: {finding}")).ToList();
    }

    /// <summary>Throws when <see cref="FindAll"/> reports a finding.</summary>
    /// <param name="graph">The restore graph of the service's test assembly.</param>
    /// <param name="options">The service's project names.</param>
    /// <exception cref="ArchitectureRuleViolationException">A rule is violated.</exception>
    public static void AssertAll(ServiceDependencyGraph graph, ServiceShapeOptions options) =>
        Throw("service shape", FindAll(graph, options));

    // ---------------------------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------------------------

    private const string ContractsPackage = "SharedKernel.Contracts";
    private const string RestClientPackage = "SharedKernel.Communication.Rest";
    private const string MediatRPrefix = "MediatR";

    private static List<string> Start(ServiceDependencyGraph graph, ServiceShapeOptions options, IEnumerable<string> judged)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(options);
        return judged
            .Where(project => !graph.ContainsProject(project))
            .Select(project =>
                $"{project} is not a project of the test assembly's restore graph (check the service prefix and the project suffixes)"
            )
            .ToList();
    }

    private static IReadOnlyList<string> Own(ServiceDependencyGraph graph, ServiceShapeOptions options, string project) =>
        graph.DirectProjects(project).Except(options.SharedProjects, StringComparer.Ordinal).ToList();

    private static IEnumerable<string> KernelTiersOf(IEnumerable<string> packages) =>
        packages.Where(KernelPackageIndex.IsKernelPackage).Select(KernelPackageIndex.Instance.TierOf).Distinct(StringComparer.Ordinal);

    private static void RequireSixProject(ServiceShapeOptions options, string rule)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Variant != ServiceShapeVariant.SixProject)
        {
            throw new InvalidOperationException(
                $"{rule} applies to the six-project variant; set ServiceShapeOptions.Variant to SixProject."
            );
        }
    }

    private static void ExpectEmpty(List<string> findings, IEnumerable<string> found, string rule)
    {
        var list = found.ToList();
        if (list.Count > 0)
        {
            findings.Add($"{rule}; found: {string.Join(", ", list)}");
        }
    }

    private static void ExpectOnly(List<string> findings, IEnumerable<string> found, string[] allowed, string what) =>
        ExpectEmpty(findings, found.Except(allowed, StringComparer.Ordinal), $"{what} must be one of {string.Join(", ", allowed)}");

    private static void ExpectEqual(List<string> findings, IEnumerable<string> found, string[] expected, string what)
    {
        var actual = found.Order(StringComparer.Ordinal).ToList();
        var wanted = expected.Order(StringComparer.Ordinal).ToList();
        if (!actual.SequenceEqual(wanted, StringComparer.Ordinal))
        {
            findings.Add($"{what} must be [{string.Join(", ", wanted)}]; found [{string.Join(", ", actual)}]");
        }
    }

    private static void Throw(string rule, IReadOnlyList<string> findings)
    {
        if (findings.Count > 0)
        {
            throw new ArchitectureRuleViolationException(
                $"Service shape rule violated ({rule}): {string.Join(" | ", findings)}"
            );
        }
    }
}
