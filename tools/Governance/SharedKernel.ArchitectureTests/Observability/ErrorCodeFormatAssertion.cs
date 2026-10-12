using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// What the caller decides for <see cref="ErrorCodeFormatAssertion"/>: protocol-mirror prefixes and exempt values.
/// </summary>
public sealed class ErrorCodeFormatOptions
{
    /// <summary>Creates the options.</summary>
    /// <param name="protocolMirrorPrefixes">First segments that mirror a wire protocol (the platform keeps <c>grpc</c> and <c>http</c>); their remaining segments may start with a digit (<c>http.404</c>).</param>
    /// <param name="exemptValues">Code values allowed to break the format, each with the reason (cite the domain <c>CLAUDE.md</c> that names them, for example the protocol values 12.Security emits); <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentException">An exemption has no reason.</exception>
    public ErrorCodeFormatOptions(IEnumerable<string> protocolMirrorPrefixes, IReadOnlyDictionary<string, string>? exemptValues = null)
    {
        ArgumentNullException.ThrowIfNull(protocolMirrorPrefixes);
        ProtocolMirrorPrefixes = [.. protocolMirrorPrefixes.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal)];
        ExemptValues = exemptValues ?? new Dictionary<string, string>();
        foreach (var (value, reason) in ExemptValues)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException($"The exemption for '{value}' has no reason.", nameof(exemptValues));
        }
    }

    /// <summary>Gets the protocol-mirror prefixes.</summary>
    public IReadOnlyCollection<string> ProtocolMirrorPrefixes { get; }

    /// <summary>Gets the exempt values with their reasons.</summary>
    public IReadOnlyDictionary<string, string> ExemptValues { get; }
}

/// <summary>
/// Checks the format of every error code the kernel declares or mints: lowercase <c>{domain}.{snake_reason}</c>, with an optional middle area.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Scope.</strong> Every <c>public const string</c> of a type named <c>*ErrorCodes</c> (nested types included), and the first
/// <see cref="string"/> argument of a constructor or factory of <c>SharedKernel.Primitives.Errors.Error</c> when it is an <c>ldstr</c>. A domain with
/// no <c>*ErrorCodes</c> class, or whose codes are built at run time, yields no finding: nothing is demanded of it.
/// </para>
/// <para>
/// <strong>Format.</strong> Two or more dot-separated segments, each matching <c>[a-z][a-z0-9]*(_[a-z0-9]+)*</c>. A code whose first segment is a
/// caller-supplied protocol-mirror prefix keeps that prefix and its remaining segments match <c>[a-z0-9]+(_[a-z0-9]+)*</c>, so <c>http.404</c> and
/// <c>grpc.not_found</c> pass. A code in the caller's exemption list passes. The rule id is <c>errorcode.format</c>; a missing assembly is <c>unscanned</c>.
/// </para>
/// <para>No check looks at the visibility of an error factory.</para>
/// </remarks>
public static partial class ErrorCodeFormatAssertion
{
    private const string ErrorTypeFullName = "SharedKernel.Primitives.Errors.Error";

    /// <summary>Finds the violations without failing.</summary>
    /// <param name="targets">The assemblies to inspect.</param>
    /// <param name="registry">The domain registry.</param>
    /// <param name="options">The protocol prefixes and exemptions.</param>
    /// <returns>The report.</returns>
    public static ObservabilityReport Find(IReadOnlyCollection<ScanTarget> targets, DomainRegistry registry, ErrorCodeFormatOptions options)
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
                findings.Add(new ObservabilityFinding("unscanned", domain, target.AssemblyName, "-", "The assembly file does not exist, so its error codes were not checked."));
                continue;
            }

            using var module = IlReader.Open(target.AssemblyFile);
            foreach (var type in IlReader.AllTypes(module))
            {
                if (IsErrorCodesType(type))
                {
                    foreach (var field in type.Fields)
                    {
                        if (field is { IsLiteral: true, IsPublic: true } && field.FieldType.FullName == "System.String" && field.Constant is string code)
                            Check(code, $"{type.FullName}.{field.Name}", domain, target.AssemblyName, options, findings);
                    }
                }

                foreach (var method in type.Methods.Where(m => m.HasBody))
                    InspectErrorCalls(method, domain, target.AssemblyName, options, findings);
            }
        }

        return new ObservabilityReport(findings, []);
    }

    /// <summary>Finds the violations and applies the mode: a table in report mode, an aggregated exception in enforce mode.</summary>
    /// <param name="targets">The assemblies to inspect.</param>
    /// <param name="registry">The domain registry.</param>
    /// <param name="options">The protocol prefixes and exemptions.</param>
    /// <param name="mode">Whether findings fail.</param>
    /// <param name="output">Where the table goes; the console when <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    /// <exception cref="InvalidOperationException">Enforce mode and at least one finding.</exception>
    public static ObservabilityReport Assert(
        IReadOnlyCollection<ScanTarget> targets,
        DomainRegistry registry,
        ErrorCodeFormatOptions options,
        ObservabilityMode mode,
        TextWriter? output = null)
    {
        var report = Find(targets, registry, options);
        ObservabilityEnforcement.Apply(report, mode, output);
        return report;
    }

    private static bool IsErrorCodesType(TypeDefinition type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current.Name.EndsWith("ErrorCodes", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void InspectErrorCalls(MethodDefinition method, string domain, string assembly, ErrorCodeFormatOptions options, List<ObservabilityFinding> findings)
    {
        var body = method.Body;
        for (var i = 0; i < body.Instructions.Count; i++)
        {
            var instruction = body.Instructions[i];
            if (instruction.OpCode.Code is not (Code.Call or Code.Newobj) || instruction.Operand is not MethodReference callee)
                continue;

            if (callee.DeclaringType.FullName != ErrorTypeFullName)
                continue;

            // A factory returns Error; the constructor builds it. Anything else on Error (a property getter, Equals) is not a code source.
            var mintsError = instruction.OpCode.Code == Code.Newobj || (!callee.HasThis && callee.ReturnType.FullName == ErrorTypeFullName);
            if (!mintsError)
                continue;

            var codeParameter = IlReader.FirstStringParameter(callee);
            if (codeParameter < 0)
                continue;

            var producer = IlReader.ArgumentProducer(body, i, codeParameter);
            if (producer.Kind == ProducerKind.String)
                Check(producer.Text, IlReader.Site(method), domain, assembly, options, findings);
        }
    }

    private static void Check(string code, string site, string domain, string assembly, ErrorCodeFormatOptions options, List<ObservabilityFinding> findings)
    {
        if (options.ExemptValues.ContainsKey(code) || IsWellFormed(code, options))
            return;

        findings.Add(new ObservabilityFinding("errorcode.format", domain, assembly, site, $"The code '{code}' is not lowercase {{domain}}.{{snake_reason}} (two or more dot-separated segments of [a-z][a-z0-9]*(_[a-z0-9]+)*)."));
    }

    private static bool IsWellFormed(string code, ErrorCodeFormatOptions options)
    {
        var segments = code.Split('.');
        if (segments.Length < 2)
            return false;

        if (options.ProtocolMirrorPrefixes.Contains(segments[0], StringComparer.Ordinal))
            return Segment().IsMatch(segments[0]) && segments.Skip(1).All(s => ProtocolSegment().IsMatch(s));

        return segments.All(s => Segment().IsMatch(s));
    }

    [GeneratedRegex(@"^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Segment();

    [GeneratedRegex(@"^[a-z0-9]+(_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProtocolSegment();
}
