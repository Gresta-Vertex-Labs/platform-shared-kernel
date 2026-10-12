using System.Collections.Frozen;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// What the caller decides for <see cref="TelemetryNamingAssertion"/>: the name pattern and any exempt names.
/// </summary>
public sealed class TelemetryNamingOptions
{
    /// <summary>Creates the options.</summary>
    /// <param name="namePattern">The regular expression every <c>ActivitySource</c> and <c>Meter</c> name must match (the platform's is <c>^SharedKernel\.[A-Z]\w+$</c>).</param>
    /// <param name="exemptNames">Names allowed to break the pattern, each with the reason it is allowed (cite the domain <c>CLAUDE.md</c>); <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentException">The pattern is empty or invalid, or an exemption has no reason.</exception>
    public TelemetryNamingOptions(string namePattern, IReadOnlyDictionary<string, string>? exemptNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namePattern);
        try
        {
            NamePattern = new Regex(namePattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"'{namePattern}' is not a valid regular expression.", nameof(namePattern), ex);
        }

        ExemptNames = exemptNames ?? new Dictionary<string, string>();
        foreach (var (name, reason) in ExemptNames)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException($"The exemption for '{name}' has no reason.", nameof(exemptNames));
        }
    }

    /// <summary>Gets the pattern a source or meter name must match.</summary>
    public Regex NamePattern { get; }

    /// <summary>Gets the exempt names with their reasons.</summary>
    public IReadOnlyDictionary<string, string> ExemptNames { get; }
}

/// <summary>
/// Checks, over IL, how the kernel names its <c>ActivitySource</c>s and <c>Meter</c>s and whether its instruments carry a unit.
/// </summary>
/// <remarks>
/// <para>Rules (each finding carries one of these ids):</para>
/// <list type="bullet">
///   <item><description><c>telemetry.name-not-prefixed</c> — a source or meter name that does not match the caller's pattern (and is not exempt).</description></item>
///   <item><description><c>telemetry.name-not-constant</c> — a name that is not a compile-time constant (a field or a call result), or a string with no <c>public const string</c> of that exact value in the same assembly. A <c>const</c> folds to <c>ldstr</c> (Cecil sees no difference), so "is a public constant" means "a public const carries this value".</description></item>
///   <item><description><c>telemetry.unit-missing</c> — a <c>Meter.Create*</c> instrument whose <c>unit</c> argument is <see langword="null"/> (omitted) or empty.</description></item>
///   <item><description><c>unscanned</c> — the assembly file is missing.</description></item>
/// </list>
/// <para>
/// A unit or name that arrives through a parameter, a field or a call cannot be followed through IL; it is listed as
/// <see cref="ObservabilityReport.Unverifiable"/>, never as a violation. The Temporal SDK creates its own instruments through its
/// <c>CustomMetricMeter</c>, never at a kernel <c>Meter.Create*</c> call site, so this check cannot see them and needs no allow-list; the
/// meter's own name still passes through the name rules. Tag-key literals belong to the SK0022 analyzer, not to this assertion.
/// </para>
/// </remarks>
public static class TelemetryNamingAssertion
{
    // (method name, parameter count) -> index of the 'unit' parameter, read from the Meter type of the running .NET 10 runtime.
    private static readonly FrozenDictionary<(string Name, int Count), int> UnitParameterIndex = BuildUnitIndex();

    /// <summary>Finds the violations without failing.</summary>
    /// <param name="targets">The assemblies to inspect.</param>
    /// <param name="registry">The domain registry.</param>
    /// <param name="options">The naming pattern and exemptions.</param>
    /// <returns>The report.</returns>
    public static ObservabilityReport Find(IReadOnlyCollection<ScanTarget> targets, DomainRegistry registry, TelemetryNamingOptions options)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);

        var findings = new List<ObservabilityFinding>();
        var unverifiable = new List<ObservabilityFinding>();

        foreach (var target in targets)
        {
            var domain = registry.IdOf(target.ProjectPath);
            if (target.AssemblyFile is null || !File.Exists(target.AssemblyFile))
            {
                findings.Add(new ObservabilityFinding("unscanned", domain, target.AssemblyName, "-", "The assembly file does not exist, so its telemetry names were not checked."));
                continue;
            }

            using var module = IlReader.Open(target.AssemblyFile);
            var publicConstants = PublicConstantStrings(module);

            foreach (var type in IlReader.AllTypes(module))
            {
                foreach (var method in type.Methods.Where(m => m.HasBody))
                    Inspect(method, domain, target.AssemblyName, options, publicConstants, findings, unverifiable);
            }
        }

        return new ObservabilityReport(findings, unverifiable);
    }

    /// <summary>Finds the violations and applies the mode: a table in report mode, an aggregated exception in enforce mode.</summary>
    /// <param name="targets">The assemblies to inspect.</param>
    /// <param name="registry">The domain registry.</param>
    /// <param name="options">The naming pattern and exemptions.</param>
    /// <param name="mode">Whether findings fail.</param>
    /// <param name="output">Where the table goes; the console when <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    /// <exception cref="InvalidOperationException">Enforce mode and at least one finding.</exception>
    public static ObservabilityReport Assert(
        IReadOnlyCollection<ScanTarget> targets,
        DomainRegistry registry,
        TelemetryNamingOptions options,
        ObservabilityMode mode,
        TextWriter? output = null)
    {
        var report = Find(targets, registry, options);
        ObservabilityEnforcement.Apply(report, mode, output);
        return report;
    }

    private static void Inspect(
        MethodDefinition method,
        string domain,
        string assembly,
        TelemetryNamingOptions options,
        HashSet<string> publicConstants,
        List<ObservabilityFinding> findings,
        List<ObservabilityFinding> unverifiable)
    {
        var body = method.Body;
        var site = IlReader.Site(method);

        for (var i = 0; i < body.Instructions.Count; i++)
        {
            var instruction = body.Instructions[i];
            if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj) || instruction.Operand is not MethodReference callee)
                continue;

            var declaring = callee.DeclaringType.FullName;

            if (NameParameter(callee, instruction.OpCode.Code, declaring) is { } nameParameter)
            {
                CheckName(body, i, nameParameter, domain, assembly, site, options, publicConstants, findings, unverifiable);
                continue;
            }

            if (declaring == "System.Diagnostics.Metrics.Meter"
                && callee.Name.StartsWith("Create", StringComparison.Ordinal)
                && UnitParameterIndex.TryGetValue((callee.Name, callee.Parameters.Count), out var unitIndex))
            {
                CheckUnit(body, i, unitIndex, callee.Name, domain, assembly, site, findings, unverifiable);
            }
        }
    }

    // The index of the string parameter that carries the name, or null when this call does not create a source or meter.
    private static int? NameParameter(MethodReference callee, Code code, string declaring)
    {
        if (code == Code.Newobj
            && declaring is "System.Diagnostics.ActivitySource" or "System.Diagnostics.Metrics.Meter" or "System.Diagnostics.Metrics.MeterOptions"
            && callee.Parameters.Count > 0
            && callee.Parameters[0].ParameterType.FullName == "System.String")
        {
            return 0;
        }

        if (code != Code.Newobj
            && callee.Name == "Create"
            && (declaring == "System.Diagnostics.Metrics.IMeterFactory" || declaring.EndsWith("MeterFactoryExtensions", StringComparison.Ordinal))
            && IlReader.FirstStringParameter(callee) is var index and >= 0)
        {
            return index;
        }

        return null;
    }

    private static void CheckName(
        MethodBody body,
        int callIndex,
        int nameParameter,
        string domain,
        string assembly,
        string site,
        TelemetryNamingOptions options,
        HashSet<string> publicConstants,
        List<ObservabilityFinding> findings,
        List<ObservabilityFinding> unverifiable)
    {
        var producer = IlReader.ArgumentProducer(body, callIndex, nameParameter);
        switch (producer.Kind)
        {
            case ProducerKind.String:
                var name = producer.Text;
                if (!options.NamePattern.IsMatch(name) && !options.ExemptNames.ContainsKey(name))
                    findings.Add(new ObservabilityFinding("telemetry.name-not-prefixed", domain, assembly, site, $"The name '{name}' does not match {options.NamePattern}."));

                if (!publicConstants.Contains(name))
                    findings.Add(new ObservabilityFinding("telemetry.name-not-constant", domain, assembly, site, $"No public const string in this assembly equals '{name}'; declare the name once as a public const."));
                break;

            case ProducerKind.Null:
                findings.Add(new ObservabilityFinding("telemetry.name-not-constant", domain, assembly, site, "The name is null."));
                break;

            case ProducerKind.Field:
            case ProducerKind.Call:
                findings.Add(new ObservabilityFinding("telemetry.name-not-constant", domain, assembly, site, $"The name is not a compile-time constant ({producer.Kind} {producer.Text}); declare a public const string."));
                break;

            default:
                unverifiable.Add(new ObservabilityFinding("telemetry.name-unverifiable", domain, assembly, site, $"The name comes from {producer.Kind} {producer.Text}, which IL analysis cannot follow."));
                break;
        }
    }

    private static void CheckUnit(
        MethodBody body,
        int callIndex,
        int unitIndex,
        string creator,
        string domain,
        string assembly,
        string site,
        List<ObservabilityFinding> findings,
        List<ObservabilityFinding> unverifiable)
    {
        var producer = IlReader.ArgumentProducer(body, callIndex, unitIndex);
        switch (producer.Kind)
        {
            case ProducerKind.Null:
                findings.Add(new ObservabilityFinding("telemetry.unit-missing", domain, assembly, site, $"{creator} is called without a unit."));
                break;

            case ProducerKind.String when producer.Text.Length == 0:
                findings.Add(new ObservabilityFinding("telemetry.unit-missing", domain, assembly, site, $"{creator} is called with an empty unit."));
                break;

            case ProducerKind.String:
                break;

            default:
                unverifiable.Add(new ObservabilityFinding("telemetry.unit-unverifiable", domain, assembly, site, $"The unit of {creator} comes from {producer.Kind} {producer.Text}, which IL analysis cannot follow."));
                break;
        }
    }

    private static HashSet<string> PublicConstantStrings(ModuleDefinition module)
    {
        var constants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in IlReader.AllTypes(module))
        {
            foreach (var field in type.Fields)
            {
                if (field is { IsLiteral: true, IsPublic: true } && field.FieldType.FullName == "System.String" && field.Constant is string value)
                    constants.Add(value);
            }
        }

        return constants;
    }

    private static FrozenDictionary<(string Name, int Count), int> BuildUnitIndex()
    {
        var index = new Dictionary<(string, int), int>();
        foreach (var method in typeof(Meter).GetMethods())
        {
            if (!method.Name.StartsWith("Create", StringComparison.Ordinal))
                continue;

            var parameters = method.GetParameters();
            var unit = Array.FindIndex(parameters, p => p.Name == "unit");
            if (unit >= 0)
                index[(method.Name, parameters.Length)] = unit;
        }

        return index.ToFrozenDictionary();
    }
}
