using System.Reflection;
using Mono.Cecil;

namespace SharedKernel.ArchitectureTests;

/// <summary>
/// Reflection/Mono.Cecil-based helper that asserts every <c>[LoggerMessage]</c>-attributed
/// method's <c>EventId</c> across a supplied set of assemblies is globally unique and falls
/// inside that assembly's own reserved range.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="PipelineOrderAssertion"/>'s precedent — a plain public helper, not a
/// NetArchTest <c>ConditionList</c>/<c>ICustomRule</c>, because "every <c>[LoggerMessage]</c>
/// <c>EventId</c> across every shipped assembly is globally unique and falls inside its own
/// assembly's reserved range" is a cross-assembly, whole-platform invariant with no single-
/// assembly "fire on a contrived violating assembly" shape a <c>ConditionList</c> naturally
/// expresses.
/// </para>
/// <para>
/// <strong>Deliberately bypasses NetArchTest.</strong> This helper walks Mono.Cecil
/// <see cref="ModuleDefinition.Types"/> and <see cref="TypeDefinition.NestedTypes"/> directly and
/// recursively — it does NOT use <c>NetArchTest.Types.InAssembly(...)</c>. SK0012's own
/// documented gap (see <c>ReflectionGuardRules</c> in <c>00.Governance/CLAUDE.md</c>) proved that
/// NetArchTest's own type-discovery layer never surfaces compiler-generated or nested types to an
/// <c>ICustomRule</c>. Walking Mono.Cecil directly sidesteps that exact blind spot, so a
/// <c>[LoggerMessage]</c> partial method declared inside a nested logging-helper class is never
/// missed.
/// </para>
/// <para>
/// <strong>Caller-supplied ranges.</strong> <c>00.Governance</c> never references
/// <c>SharedKernel.Primitives</c> (it references nothing in production code). The consuming test
/// project builds the <c>assemblyRanges</c> (see
/// <see cref="AssertGloballyUniqueAndInRange"/>) dictionary itself, typically from
/// <c>SharedKernel.Primitives.Logging.LoggingEventIdRanges</c> constants — keeping that
/// registry the single source of truth for range numbers while this helper stays
/// dependency-free.
/// </para>
/// <para>
/// <strong>Expect failures on first adoption.</strong> Pointed at a codebase that has been
/// logging for a while, this helper normally fails the first time: duplicate
/// <c>EventId</c> values accumulate quietly, because nothing in the compiler or the runtime
/// objects to two log messages sharing a number. That first failure is the point — it lists the
/// collisions so they can be retrofitted. Treat a green run as a property to be earned and then
/// defended, not as the expected initial state.
/// </para>
/// </remarks>
public static class LoggingEventIdIntegrityAssertion
{
    private const string LoggerMessageAttributeFullName = "Microsoft.Extensions.Logging.LoggerMessageAttribute";
    private const string EventIdPropertyName = "EventId";
    private const string Int32TypeFullName = "System.Int32";

    private readonly record struct DiscoveredEventId(
        int EventId,
        string DeclaringTypeFullName,
        string MethodName,
        Assembly Assembly);

    /// <summary>
    /// Walks every <c>[LoggerMessage]</c>-attributed method across every assembly key in
    /// <paramref name="assemblyRanges"/> and asserts two independent invariants: every
    /// discovered <c>EventId</c> is globally unique across the ENTIRE aggregate set (including
    /// across different assemblies), and every discovered <c>EventId</c> falls within its own
    /// declaring assembly's caller-supplied <c>(RangeMin, RangeMax)</c> tuple (inclusive).
    /// </summary>
    /// <param name="assemblyRanges">
    /// Maps each assembly to inspect to its reserved <c>EventId</c> range, inclusive on both
    /// ends. Supplied entirely by the caller — this helper never hard-codes an assembly path or
    /// range value.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown once, aggregating every violation found (collisions and out-of-range entries) —
    /// this method does not stop at the first failure. The message lists every offending site,
    /// mirroring <see cref="PipelineOrderAssertion"/>'s aggregate-failure-message convention.
    /// </exception>
    public static void AssertGloballyUniqueAndInRange(
        IReadOnlyDictionary<Assembly, (int RangeMin, int RangeMax)> assemblyRanges)
    {
        var discovered = new List<DiscoveredEventId>();

        foreach (var assembly in assemblyRanges.Keys)
        {
            using var assemblyDefinition = AssemblyDefinition.ReadAssembly(assembly.Location);
            var module = assemblyDefinition.MainModule;

            foreach (var type in EnumerateAllTypes(module.Types))
            {
                foreach (var method in type.Methods)
                {
                    var eventId = TryGetLoggerMessageEventId(method);
                    if (eventId is not null)
                    {
                        discovered.Add(new DiscoveredEventId(
                            eventId.Value,
                            type.FullName,
                            method.Name,
                            assembly));
                    }
                }
            }
        }

        var violations = new List<string>();

        CollectRangeViolations(discovered, assemblyRanges, violations);
        CollectUniquenessViolations(discovered, violations);

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"LoggingEventIdIntegrityAssertion found {violations.Count} violation(s):"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
        }
    }

    private static void CollectRangeViolations(
        IReadOnlyList<DiscoveredEventId> discovered,
        IReadOnlyDictionary<Assembly, (int RangeMin, int RangeMax)> assemblyRanges,
        List<string> violations)
    {
        foreach (var entry in discovered)
        {
            var (rangeMin, rangeMax) = assemblyRanges[entry.Assembly];
            if (entry.EventId < rangeMin || entry.EventId > rangeMax)
            {
                violations.Add(
                    $"{entry.DeclaringTypeFullName}.{entry.MethodName} declares EventId {entry.EventId}, "
                    + $"which is outside the reserved range [{rangeMin}, {rangeMax}] for assembly "
                    + $"'{entry.Assembly.GetName().Name}'.");
            }
        }
    }

    private static void CollectUniquenessViolations(
        IReadOnlyList<DiscoveredEventId> discovered,
        List<string> violations)
    {
        var groupedByEventId = discovered
            .GroupBy(entry => entry.EventId)
            .Where(group => group
                .Select(entry => (entry.DeclaringTypeFullName, entry.MethodName))
                .Distinct()
                .Count() > 1);

        foreach (var group in groupedByEventId)
        {
            var sites = string.Join(
                ", ",
                group.Select(entry =>
                    $"{entry.DeclaringTypeFullName}.{entry.MethodName} (assembly '{entry.Assembly.GetName().Name}')"));

            violations.Add($"EventId {group.Key} is declared by multiple sites: {sites}.");
        }
    }

    /// <summary>
    /// Recursively enumerates every <see cref="TypeDefinition"/> in <paramref name="types"/> plus
    /// every <see cref="TypeDefinition.NestedTypes"/> entry, at any nesting depth — the deliberate
    /// alternative to NetArchTest's <c>Types.InAssembly(...)</c> projection (see class remarks).
    /// </summary>
    private static IEnumerable<TypeDefinition> EnumerateAllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;

            foreach (var nested in EnumerateAllTypes(type.NestedTypes))
                yield return nested;
        }
    }

    /// <summary>
    /// Extracts the <c>EventId</c> from a <c>[LoggerMessage]</c> attribute on
    /// <paramref name="method"/>, if present. Checks the attribute's <c>EventId</c> named
    /// property first (covers <c>[LoggerMessage(EventId = 5042, Level = LogLevel.Information,
    /// Message = "...")]</c>), falling back to the first <c>int</c>-typed positional
    /// constructor argument (covers <c>[LoggerMessage(5042, LogLevel.Information, "...")]</c>).
    /// </summary>
    /// <returns>The discovered <c>EventId</c>, or <see langword="null"/> if the method has no
    /// <c>[LoggerMessage]</c> attribute or the attribute carries no discoverable EventId.</returns>
    private static int? TryGetLoggerMessageEventId(MethodDefinition method)
    {
        if (!method.HasCustomAttributes)
            return null;

        var attribute = method.CustomAttributes.FirstOrDefault(
            a => a.AttributeType.FullName == LoggerMessageAttributeFullName);

        if (attribute is null)
            return null;

        foreach (var namedArgument in attribute.Properties)
        {
            if (namedArgument.Name == EventIdPropertyName
                && namedArgument.Argument.Value is int namedEventId)
            {
                return namedEventId;
            }
        }

        foreach (var constructorArgument in attribute.ConstructorArguments)
        {
            if (constructorArgument.Type.FullName == Int32TypeFullName
                && constructorArgument.Value is int positionalEventId)
            {
                return positionalEventId;
            }
        }

        return null;
    }
}
