using Dapper;

namespace SharedKernel.Persistence.Dapper.TypeHandlers;

/// <summary>
/// Collects the caller-supplied Dapper <see cref="SqlMapper.ITypeHandler"/> registrations passed to
/// <c>AddSharedKernelDapper(Action&lt;DapperTypeHandlerBuilder&gt;)</c>.
/// </summary>
/// <remarks>
/// Every consuming service's <see cref="StronglyTypedIdTypeHandler{TStronglyTypedId,TValue}"/>/
/// <see cref="SmartEnumTypeHandler{TEnum,TValue}"/> subclass registers itself through this builder
/// rather than calling <see cref="SqlMapper.AddTypeHandler{T}(SqlMapper.ITypeHandler)"/> directly at
/// an arbitrary point in startup — see <see cref="DapperPersistenceExtensions"/> for why this matters
/// (idempotent, once-under-lock registration across a process that may call
/// <c>AddSharedKernelDapper</c> more than once, e.g. from two independent composition-root modules).
/// </remarks>
public sealed class DapperTypeHandlerBuilder
{
    private readonly List<Action> _registrations = [];

    /// <summary>
    /// Registers <paramref name="handler"/> as the Dapper type handler for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The CLR type the handler reads/writes.</typeparam>
    /// <param name="handler">The type handler instance.</param>
    /// <returns>The same builder, for fluent chaining.</returns>
    public DapperTypeHandlerBuilder AddTypeHandler<T>(SqlMapper.ITypeHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _registrations.Add(() => SqlMapper.AddTypeHandler(typeof(T), handler));
        return this;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> (constructed via its public parameterless
    /// constructor) as the Dapper type handler for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The CLR type the handler reads/writes.</typeparam>
    /// <typeparam name="THandler">
    /// The concrete <see cref="SqlMapper.ITypeHandler"/> implementation, e.g. a one-line
    /// <see cref="StronglyTypedIdTypeHandler{TStronglyTypedId,TValue}"/>/
    /// <see cref="SmartEnumTypeHandler{TEnum,TValue}"/> subclass.
    /// </typeparam>
    /// <returns>The same builder, for fluent chaining.</returns>
    public DapperTypeHandlerBuilder AddTypeHandler<T, THandler>()
        where THandler : SqlMapper.ITypeHandler, new() =>
        AddTypeHandler<T>(new THandler());

    internal void Apply()
    {
        foreach (var register in _registrations)
            register();
    }
}
