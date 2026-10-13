using System.Text.RegularExpressions;

namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// What the caller decides for <see cref="LogTemplateAssertion"/>: the free-text placeholder names that must not appear.
/// </summary>
public sealed class LogTemplateOptions
{
    /// <summary>Creates the options.</summary>
    /// <param name="forbiddenPlaceholders">Placeholder names that carry free text and so can leak data into a log (the platform's are <c>Error</c>, <c>Reason</c>, <c>ErrorMessage</c>, <c>Detail</c>, <c>Message</c>). Compared case-sensitively.</param>
    public LogTemplateOptions(IEnumerable<string> forbiddenPlaceholders)
    {
        ArgumentNullException.ThrowIfNull(forbiddenPlaceholders);
        ForbiddenPlaceholders = [.. forbiddenPlaceholders.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Gets the placeholder names a template must not use.</summary>
    public IReadOnlyCollection<string> ForbiddenPlaceholders { get; }
}

/// <summary>
/// Checks the message template of every <c>[LoggerMessage]</c> against the platform's log-template style.
/// </summary>
/// <remarks>
/// <para>Rules (each finding carries one of these ids; every site is reported, not the first):</para>
/// <list type="bullet">
///   <item><description><c>template.no-terminal-period</c> — the template does not end with a period.</description></item>
///   <item><description><c>template.multiple-sentences</c> — a period followed by a space outside a quoted placeholder (<c>'{Name}'</c>) and outside a placeholder: more than one sentence.</description></item>
///   <item><description><c>template.em-dash</c> — an em-dash.</description></item>
///   <item><description><c>template.key-value-parenthetical</c> — a parenthetical holding <c>key=value</c>.</description></item>
///   <item><description><c>template.free-text-placeholder</c> — a placeholder whose name the caller forbids.</description></item>
///   <item><description><c>unscanned</c> — the assembly file is missing.</description></item>
/// </list>
/// <para>A <c>[LoggerMessage]</c> with no message is skipped (its message is the default). Placeholders may carry <c>@</c>, <c>$</c>, an alignment or a format.</para>
/// </remarks>
public static partial class LogTemplateAssertion
{
    /// <summary>Finds the violations without failing.</summary>
    /// <param name="targets">The assemblies to inspect.</param>
    /// <param name="registry">The domain registry.</param>
    /// <param name="options">The forbidden placeholder names.</param>
    /// <returns>The report.</returns>
    public static ObservabilityReport Find(IReadOnlyCollection<ScanTarget> targets, DomainRegistry registry, LogTemplateOptions options)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);

        var findings = new List<ObservabilityFinding>();
        foreach (var target in targets)
        {
            var domain = registry.IdOf(target.ProjectPath);
            if (target.AssemblyFile is null || !File.Exists(target.AssemblyFile))
            {
                findings.Add(new ObservabilityFinding("unscanned", domain, target.AssemblyName, "-", "The assembly file does not exist, so its log templates were not checked."));
                continue;
            }

            using var module = IlReader.Open(target.AssemblyFile);
            foreach (var message in LoggerMessageReader.Read(module))
            {
                if (message.Message is { } template)
                    CheckTemplate(template, domain, target.AssemblyName, IlReader.Site(message.Method), options, findings);
            }
        }

        return new ObservabilityReport(findings, []);
    }

    /// <summary>Finds the violations and throws one aggregated exception when there is any.</summary>
    /// <param name="targets">The assemblies to inspect.</param>
    /// <param name="registry">The domain registry.</param>
    /// <param name="options">The forbidden placeholder names.</param>
    /// <param name="output">Where the table goes; the console when <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    /// <exception cref="InvalidOperationException">At least one finding.</exception>
    public static ObservabilityReport Assert(
        IReadOnlyCollection<ScanTarget> targets,
        DomainRegistry registry,
        LogTemplateOptions options,
        TextWriter? output = null)
    {
        var report = Find(targets, registry, options);
        ObservabilityEnforcement.Apply(report, output);
        return report;
    }

    private static void CheckTemplate(string template, string domain, string assembly, string site, LogTemplateOptions options, List<ObservabilityFinding> findings)
    {
        void Add(string rule, string detail) => findings.Add(new ObservabilityFinding(rule, domain, assembly, site, $"{detail} Template: \"{template}\""));

        if (!template.TrimEnd().EndsWith('.'))
            Add("template.no-terminal-period", "The template does not end with a period.");

        // Sentences are counted on the text outside placeholders and outside quoted placeholders.
        var prose = Placeholder().Replace(template, "{}");
        prose = QuotedSpan().Replace(prose, "''");
        if (SentenceBreak().IsMatch(prose))
            Add("template.multiple-sentences", "The template holds more than one sentence.");

        if (template.Contains('—'))
            Add("template.em-dash", "The template contains an em-dash.");

        if (KeyValueParenthetical().IsMatch(prose))
            Add("template.key-value-parenthetical", "The template holds a key=value parenthetical.");

        foreach (Match match in Placeholder().Matches(template))
        {
            var name = match.Groups["name"].Value;
            if (options.ForbiddenPlaceholders.Contains(name, StringComparer.Ordinal))
                Add("template.free-text-placeholder", $"The placeholder {{{name}}} carries free text.");
        }
    }

    [GeneratedRegex(@"\{[@$]?(?<name>[A-Za-z_]\w*)[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"'[^']*'", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedSpan();

    [GeneratedRegex(@"\.\s", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"\([^()]*\b\w+=[^()]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueParenthetical();
}
