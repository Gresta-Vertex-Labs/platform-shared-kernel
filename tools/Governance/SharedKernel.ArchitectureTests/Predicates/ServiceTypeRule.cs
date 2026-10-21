using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// A NetArchTest custom rule that judges a type by its reflection <see cref="Type"/>, so a rule can ask what a type
/// implements or derives from by metadata full name without this package referencing the assembly that declares the
/// interface. NetArchTest hands the rule a Cecil <see cref="TypeDefinition"/>; it is mapped back to the reflection type
/// of the assemblies the caller supplied.
/// </summary>
internal sealed class ServiceTypeRule : ICustomRule
{
    private readonly IReadOnlyDictionary<string, Type> _types;
    private readonly Func<Type, bool> _test;
    private readonly bool _whenUnmapped;

    private ServiceTypeRule(IReadOnlyDictionary<string, Type> types, Func<Type, bool> test, bool whenUnmapped)
    {
        _types = types;
        _test = test;
        _whenUnmapped = whenUnmapped;
    }

    /// <summary>
    /// Builds <c>Types.InAssemblies(assemblies).That().MeetCustomRule(selects).Should().MeetCustomRule(satisfies)</c>:
    /// every type <paramref name="selects"/> accepts must satisfy <paramref name="satisfies"/>.
    /// </summary>
    internal static ConditionList Build(
        IEnumerable<Assembly> assemblies,
        Func<Type, bool> selects,
        Func<Type, bool> satisfies
    )
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        var list = assemblies.Distinct().ToArray();
        if (list.Length == 0)
        {
            throw new ArgumentException("Supply at least one assembly of the service.", nameof(assemblies));
        }

        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in list.SelectMany(a => a.GetTypes()))
        {
            map.TryAdd(Key(type), type);
        }

        return Types
            .InAssemblies(list)
            .That()
            .MeetCustomRule(new ServiceTypeRule(map, selects, whenUnmapped: false))
            .Should()
            .MeetCustomRule(new ServiceTypeRule(map, satisfies, whenUnmapped: true));
    }

    /// <inheritdoc />
    public bool MeetsRule(TypeDefinition type) =>
        _types.TryGetValue($"{type.Module.Assembly.Name.Name}|{type.FullName}", out var reflected)
            ? _test(reflected)
            : _whenUnmapped;

    // Cecil spells a nested type 'Outer/Inner'; reflection spells it 'Outer+Inner'.
    private static string Key(Type type) =>
        $"{type.Assembly.GetName().Name}|{type.FullName?.Replace('+', '/')}";
}

/// <summary>What a type is, by metadata name, for the service placement rules.</summary>
internal static class ServiceTypeFacts
{
    /// <summary>A class or struct a service declares: not an interface, not abstract (a static class is abstract and sealed).</summary>
    internal static bool IsConcrete(Type type) =>
        IsEligible(type) && ((!type.IsInterface && !type.IsAbstract) || (type.IsAbstract && type.IsSealed));

    /// <summary>A type the compiler did not synthesise and that sits in a namespace.</summary>
    internal static bool IsEligible(Type type) =>
        type.Namespace is not null
        && !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        && !type.Name.Contains('<', StringComparison.Ordinal);

    /// <summary>Whether <paramref name="type"/> implements the interface whose metadata full name is <paramref name="metadataName"/>.</summary>
    internal static bool Implements(Type type, string metadataName) =>
        type.GetInterfaces().Any(i => MetadataNameOf(i) == metadataName);

    /// <summary>The first generic argument of every closed form of the interface <paramref name="metadataName"/> the type implements.</summary>
    internal static IEnumerable<Type> ClosingArguments(Type type, string metadataName) =>
        type.GetInterfaces().Where(i => i.IsGenericType && MetadataNameOf(i) == metadataName).Select(i => i.GetGenericArguments()[0]);

    /// <summary>The metadata full names of every base type of <paramref name="type"/>, nearest first.</summary>
    internal static IEnumerable<string> BaseTypeNames(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return MetadataNameOf(current);
        }
    }

    /// <summary>The namespace segments of <paramref name="ns"/> below <paramref name="root"/>, or empty when it is not below it.</summary>
    internal static string[] SegmentsBelow(string ns, string root) =>
        ns.StartsWith($"{root}.", StringComparison.Ordinal) ? ns[(root.Length + 1)..].Split('.') : [];

    private static string MetadataNameOf(Type type) =>
        (type.IsGenericType ? type.GetGenericTypeDefinition().FullName : type.FullName) ?? type.Name;
}
