using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Conversions;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// Pre-convention mappings every <c>SharedKernelDbContext</c> gets: <see cref="Money"/> as a complex type with a
/// three-letter currency column, and a value converter for every <see cref="StronglyTypedId{TValue}"/> the
/// context can reach.
/// </summary>
/// <remarks>
/// <para>
/// A strongly-typed id is a record class; without a converter registered before model discovery EF Core would
/// take it for an entity type. Ids are discovered by walking the properties of every type reachable from the
/// context's <c>DbSet&lt;T&gt;</c> properties (whatever assembly they live in) plus every id type declared in the
/// context's own assembly. The result is cached per context type.
/// </para>
/// <para>An id type reachable only through a configuration (never through a CLR property) is not found; expose it
/// on the entity or declare it in the context's assembly.</para>
/// </remarks>
internal static class DomainTypeMappings
{
    private const int CurrencyCodeLength = 3;

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<(Type IdType, Type ValueType)>> Cache = new();

    /// <summary>Registers the <see cref="Money"/> and strongly-typed id mappings for <paramref name="contextType"/>.</summary>
    public static void Apply(ModelConfigurationBuilder configurationBuilder, Type contextType)
    {
        configurationBuilder.ComplexProperties<Money>();
        configurationBuilder.Properties<Currency>()
            .HaveConversion<CurrencyValueConverter>()
            .HaveMaxLength(CurrencyCodeLength)
            .AreFixedLength();

        foreach (var (idType, valueType) in Cache.GetOrAdd(contextType, Discover))
        {
            configurationBuilder.Properties(idType)
                .HaveConversion(typeof(StronglyTypedIdValueConverter<,>).MakeGenericType(idType, valueType));
        }
    }

    /// <summary>Returns the <c>TValue</c> of a concrete <see cref="StronglyTypedId{TValue}"/> subclass, or <see langword="null"/>.</summary>
    internal static Type? GetStronglyTypedIdValueType(Type type)
    {
        if (type.IsAbstract || !type.IsClass)
            return null;

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(StronglyTypedId<>))
                return current.GetGenericArguments()[0];
        }

        return null;
    }

    private static IReadOnlyList<(Type IdType, Type ValueType)> Discover(Type contextType)
    {
        var found = new Dictionary<Type, Type>();
        var visited = new HashSet<Type>();
        var pending = new Queue<Type>();

        foreach (var property in AllProperties(contextType))
        {
            var propertyType = property.PropertyType;
            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                pending.Enqueue(propertyType.GetGenericArguments()[0]);
        }

        foreach (var type in LoadableTypes(contextType.Assembly))
        {
            if (GetStronglyTypedIdValueType(type) is { } valueType)
                found[type] = valueType;
        }

        while (pending.TryDequeue(out var type))
        {
            if (!visited.Add(type))
                continue;

            foreach (var property in AllProperties(type))
            {
                foreach (var candidate in Unwrap(property.PropertyType))
                {
                    if (GetStronglyTypedIdValueType(candidate) is { } valueType)
                        found[candidate] = valueType;
                    else if (IsDomainType(candidate))
                        pending.Enqueue(candidate);
                }
            }
        }

        return found.Select(pair => (pair.Key, pair.Value)).ToList();
    }

    // Instance properties of the type and all its base types, public and non-public.
    private static IEnumerable<PropertyInfo> AllProperties(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var property in current.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length == 0)
                    yield return property;
            }
        }
    }

    // The type itself, the underlying type of a Nullable<T>, and the element type of a collection.
    private static IEnumerable<Type> Unwrap(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        yield return underlying;

        if (underlying.IsArray && underlying.GetElementType() is { } element)
        {
            yield return element;
            yield break;
        }

        foreach (var contract in underlying.GetInterfaces().Append(underlying))
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                yield return contract.GetGenericArguments()[0];
        }
    }

    private static bool IsDomainType(Type type) =>
        !type.IsPrimitive
        && !type.IsEnum
        && !type.IsPointer
        && !type.IsGenericTypeDefinition
        && type != typeof(string)
        && type.Namespace is { } ns
        && !ns.StartsWith("System", StringComparison.Ordinal)
        && !ns.StartsWith("Microsoft", StringComparison.Ordinal);

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
