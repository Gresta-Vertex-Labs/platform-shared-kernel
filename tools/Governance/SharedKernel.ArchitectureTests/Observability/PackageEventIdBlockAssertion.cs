namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// Checks that every assembly's <c>[LoggerMessage]</c> EventIds stay inside the sub-block it owns, and that no two assemblies share one.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Rules</strong> (each finding carries one of these ids):
/// </para>
/// <list type="bullet">
///   <item><description><c>eventid.outside-domain-block</c> — an id outside the thousand-block of the assembly's domain (<c>Number * 1000</c>).</description></item>
///   <item><description><c>eventid.outside-declared-range</c> — the assembly's domain <c>CLAUDE.md</c> declares the ranges it owns (see <see cref="EventIdOwnership"/>) and an id lies outside them.</description></item>
///   <item><description><c>eventid.block-span</c> — an assembly with no declared range uses more than one 100-wide sub-block; ids outside the sub-block holding most of them are reported.</description></item>
///   <item><description><c>eventid.shared-block</c> — an assembly with no declared range uses a range that another assembly owns or also uses.</description></item>
///   <item><description><c>eventid.declared-overlap</c> — two declared ranges of different owners overlap.</description></item>
///   <item><description><c>eventid.declared-outside-domain</c> — a declared range lies outside its domain's thousand-block.</description></item>
///   <item><description><c>unscanned</c> — the assembly file is missing, so nothing about it was checked.</description></item>
/// </list>
/// <para>
/// The default ("one 100-wide sub-block per assembly, shared with nobody") applies to an assembly that declares nothing; a declared
/// family (a base package at offsets 1-49 and its Adapters at 50-99) is read from the domain <c>CLAUDE.md</c>, never from a list here. Global
/// uniqueness of ids stays in <see cref="LoggingEventIdIntegrityAssertion"/>, whose signature is unchanged; this adds ownership beside it.
/// </para>
/// </remarks>
public static class PackageEventIdBlockAssertion
{
    private sealed record Usage(string Assembly, string Domain, DomainInfo? Info, List<(int EventId, string Site)> Ids, bool Declared, List<(int Start, int End)> Effective);

    /// <summary>Finds the violations without failing.</summary>
    /// <param name="targets">The assemblies to inspect.</param>
    /// <param name="registry">The domain registry parsed from the root <c>CLAUDE.md</c>.</param>
    /// <param name="ownership">The ranges the domain <c>CLAUDE.md</c> files declare.</param>
    /// <returns>The report.</returns>
    public static ObservabilityReport Find(IReadOnlyCollection<ScanTarget> targets, DomainRegistry registry, EventIdOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ownership);

        var findings = new List<ObservabilityFinding>();
        var usages = new List<Usage>();

        foreach (var target in targets)
        {
            var info = registry.Find(target.ProjectPath);
            var domain = info?.Id ?? DomainRegistry.UnknownDomain;

            if (target.AssemblyFile is null || !File.Exists(target.AssemblyFile))
            {
                findings.Add(new ObservabilityFinding("unscanned", domain, target.AssemblyName, "-", "The assembly file does not exist, so its EventIds were not checked."));
                continue;
            }

            using var module = IlReader.Open(target.AssemblyFile);
            var ids = LoggerMessageReader.Read(module)
                .Where(m => m.EventId is not null)
                .Select(m => (m.EventId!.Value, IlReader.Site(m.Method)))
                .ToList();
            if (ids.Count == 0)
                continue;

            var declared = ownership.For(target.AssemblyName);
            var effective = declared.Count > 0
                ? [.. declared.Select(e => (e.Start, e.End))]
                : DominantSubBlock(ids);
            usages.Add(new Usage(target.AssemblyName, domain, info, ids, declared.Count > 0, effective));
        }

        foreach (var usage in usages)
        {
            foreach (var (eventId, site) in usage.Ids)
            {
                if (usage.Info is { } info && (eventId < info.EventIdBlockStart || eventId > info.EventIdBlockEnd))
                {
                    findings.Add(new ObservabilityFinding("eventid.outside-domain-block", usage.Domain, usage.Assembly, site,
                        $"EventId {eventId} is outside the domain block {info.EventIdBlockStart}-{info.EventIdBlockEnd}."));
                }

                if (!usage.Effective.Any(r => eventId >= r.Start && eventId <= r.End))
                {
                    findings.Add(usage.Declared
                        ? new ObservabilityFinding("eventid.outside-declared-range", usage.Domain, usage.Assembly, site,
                            $"EventId {eventId} is outside the ranges declared for this assembly ({Describe(usage.Effective)}).")
                        : new ObservabilityFinding("eventid.block-span", usage.Domain, usage.Assembly, site,
                            $"EventId {eventId} is outside the sub-block {Describe(usage.Effective)} that holds most of this assembly's ids."));
                }
            }
        }

        foreach (var usage in usages.Where(u => !u.Declared))
        {
            foreach (var other in usages.Where(o => !ReferenceEquals(o, usage)))
            {
                var inOther = usage.Ids.Where(i => other.Effective.Any(r => i.EventId >= r.Start && i.EventId <= r.End)).ToList();
                if (inOther.Count == 0)
                    continue;

                findings.Add(new ObservabilityFinding("eventid.shared-block", usage.Domain, usage.Assembly, inOther[0].Site,
                    $"{inOther.Count} id(s) (first {inOther[0].EventId}) lie in {Describe(other.Effective)}, which {other.Assembly} {(other.Declared ? "owns" : "also uses")}."));
            }
        }

        AddDeclaredRangeFindings(ownership, registry, findings);

        return new ObservabilityReport(findings, []);
    }

    /// <summary>Finds the violations and throws one aggregated exception when there is any.</summary>
    /// <param name="targets">The assemblies to inspect.</param>
    /// <param name="registry">The domain registry.</param>
    /// <param name="ownership">The declared ranges.</param>
    /// <param name="output">Where the table goes; the console when <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    /// <exception cref="InvalidOperationException">At least one finding.</exception>
    public static ObservabilityReport Assert(
        IReadOnlyCollection<ScanTarget> targets,
        DomainRegistry registry,
        EventIdOwnership ownership,
        TextWriter? output = null)
    {
        var report = Find(targets, registry, ownership);
        ObservabilityEnforcement.Apply(report, output);
        return report;
    }

    private static void AddDeclaredRangeFindings(EventIdOwnership ownership, DomainRegistry registry, List<ObservabilityFinding> findings)
    {
        var entries = ownership.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var domain = registry.Domains.FirstOrDefault(d => d.Id == entry.DomainId);
            if (domain is not null && (entry.Start < domain.EventIdBlockStart || entry.End > domain.EventIdBlockEnd))
            {
                findings.Add(new ObservabilityFinding("eventid.declared-outside-domain", entry.DomainId, entry.Assemblies[0], $"{entry.Start}-{entry.End}",
                    $"The declared range {entry.Start}-{entry.End} for {string.Join(", ", entry.Assemblies)} is outside the domain block {domain.EventIdBlockStart}-{domain.EventIdBlockEnd}."));
            }

            for (var j = i + 1; j < entries.Count; j++)
            {
                var other = entries[j];
                if (other.Start > entry.End || entry.Start > other.End)
                    continue;

                findings.Add(new ObservabilityFinding("eventid.declared-overlap", entry.DomainId, entry.Assemblies[0], $"{entry.Start}-{entry.End}",
                    $"The declared range {entry.Start}-{entry.End} ({string.Join(", ", entry.Assemblies)}) overlaps {other.Start}-{other.End} ({string.Join(", ", other.Assemblies)})."));
            }
        }
    }

    private static List<(int Start, int End)> DominantSubBlock(List<(int EventId, string Site)> ids)
    {
        var block = ids
            .GroupBy(i => i.EventId / 100)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First()
            .Key;
        return [(block * 100, (block * 100) + 99)];
    }

    private static string Describe(List<(int Start, int End)> ranges) => string.Join(", ", ranges.Select(r => $"{r.Start}-{r.End}"));
}
