using System.Text.Json.Serialization.Metadata;
using Dapper;
using Pgvector;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Primitives.Enums;

namespace SharedKernel.Persistence.Dapper.TypeHandlers;

/// <summary>
/// The one place Dapper's process-wide configuration is set: column-name matching and type handlers.
/// </summary>
/// <remarks>
/// <para>
/// Dapper keeps this configuration in static state for the whole process, so it is set explicitly here —
/// normally once, by <c>AddSharedKernelDapper(b =&gt; ...)</c>. Calls are serialized; every call applies its
/// handlers (a later call never undoes an earlier one's handlers), and the last call decides
/// <see cref="DapperConfigurationBuilder.MatchNamesWithUnderscores"/>.
/// </para>
/// <para>
/// Always registered: pass-through handlers for pgvector's <see cref="Vector"/>, <see cref="HalfVector"/>
/// and <see cref="SparseVector"/>, and a <c>uuid</c> handler for
/// <see cref="SharedKernel.Execution.Tenancy.TenantId"/>.
/// </para>
/// </remarks>
public static class DapperConfiguration
{
    private static readonly Lock Gate = new();

    /// <summary>Applies the platform defaults plus whatever <paramref name="configure"/> adds.</summary>
    /// <param name="configure">Adds type handlers or changes name matching.</param>
    public static void Apply(Action<DapperConfigurationBuilder>? configure = null)
    {
        var builder = new DapperConfigurationBuilder();
        configure?.Invoke(builder);

        lock (Gate)
        {
            DefaultTypeMap.MatchNamesWithUnderscores = builder.MatchesNamesWithUnderscores;

            SqlMapper.AddTypeHandler(new PassThroughTypeHandler<Vector>());
            SqlMapper.AddTypeHandler(new PassThroughTypeHandler<HalfVector>());
            SqlMapper.AddTypeHandler(new PassThroughTypeHandler<SparseVector>());
            SqlMapper.AddTypeHandler(new TenantIdTypeHandler());

            foreach (var register in builder.Registrations)
                register();
        }
    }
}

/// <summary>Collects the Dapper configuration applied by <see cref="DapperConfiguration.Apply"/>.</summary>
public sealed class DapperConfigurationBuilder
{
    private readonly List<Action> _registrations = [];

    internal DapperConfigurationBuilder()
    {
    }

    internal bool MatchesNamesWithUnderscores { get; private set; } = true;

    internal IReadOnlyList<Action> Registrations => _registrations;

    /// <summary>
    /// Whether a <c>snake_case</c> column (<c>tenant_id</c>) maps to a PascalCase property (<c>TenantId</c>)
    /// without an alias. On by default, matching the platform's snake_case schema.
    /// </summary>
    /// <param name="enabled">Whether underscores are ignored when matching names.</param>
    /// <returns>The same builder.</returns>
    public DapperConfigurationBuilder MatchNamesWithUnderscores(bool enabled)
    {
        MatchesNamesWithUnderscores = enabled;
        return this;
    }

    /// <summary>Maps a <see cref="SmartEnum{TEnum, TValue}"/> to its underlying value.</summary>
    /// <typeparam name="TEnum">The SmartEnum type.</typeparam>
    /// <typeparam name="TValue">Its underlying value type.</typeparam>
    /// <returns>The same builder.</returns>
    public DapperConfigurationBuilder AddSmartEnum<TEnum, TValue>()
        where TEnum : SmartEnum<TEnum, TValue>
        where TValue : IEquatable<TValue> =>
        AddTypeHandler(new SmartEnumTypeHandler<TEnum, TValue>());

    /// <summary>Maps a <see cref="StronglyTypedId{TValue}"/> to its underlying value.</summary>
    /// <typeparam name="TId">The identifier type.</typeparam>
    /// <typeparam name="TValue">Its underlying value type.</typeparam>
    /// <param name="factory">
    /// Creates the identifier from a stored value, or <see langword="null"/> to use its public
    /// single-<typeparamref name="TValue"/> constructor.
    /// </param>
    /// <returns>The same builder.</returns>
    public DapperConfigurationBuilder AddStronglyTypedId<TId, TValue>(Func<TValue, TId>? factory = null)
        where TId : StronglyTypedId<TValue>
        where TValue : notnull =>
        AddTypeHandler(new StronglyTypedIdTypeHandler<TId, TValue>(factory));

    /// <summary>Stores <typeparamref name="T"/> as <c>jsonb</c>, serialized with <paramref name="typeInfo"/>.</summary>
    /// <typeparam name="T">The mapped type.</typeparam>
    /// <param name="typeInfo">Source-generated serialization metadata, e.g. <c>MyJsonContext.Default.Address</c>.</param>
    /// <returns>The same builder.</returns>
    public DapperConfigurationBuilder AddJsonb<T>(JsonTypeInfo<T> typeInfo) =>
        AddTypeHandler(new JsonbTypeHandler<T>(typeInfo));

    /// <summary>Registers any other Dapper type handler.</summary>
    /// <typeparam name="T">The handled type.</typeparam>
    /// <param name="handler">The handler.</param>
    /// <returns>The same builder.</returns>
    public DapperConfigurationBuilder AddTypeHandler<T>(SqlMapper.TypeHandler<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _registrations.Add(() => SqlMapper.AddTypeHandler(handler));
        return this;
    }
}
