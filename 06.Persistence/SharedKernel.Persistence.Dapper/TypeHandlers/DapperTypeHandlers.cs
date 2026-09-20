namespace SharedKernel.Persistence.Dapper.TypeHandlers;

/// <summary>
/// Central, process-wide registration point for Dapper type handlers — both this platform's
/// zero-configuration defaults and any caller-supplied
/// <see cref="Dapper.SqlMapper.ITypeHandler"/> registrations.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Dapper.SqlMapper.AddTypeHandler(System.Type,Dapper.SqlMapper.ITypeHandler)"/> mutates
/// process-wide static state — calling it twice for the same handler set is harmless, but a
/// composition root that calls <c>AddSharedKernelDapper(...)</c> from more than one module (a real
/// shape: a shared "infra" module plus a per-feature module) must not race applying two different
/// builders' registrations concurrently. <see cref="Apply"/> serializes every call under one lock.
/// </para>
/// <para>
/// <strong>Snake-case column mapping is likewise process-wide, static Dapper state</strong> — see
/// <see cref="Apply"/>'s own remarks.
/// </para>
/// </remarks>
public static class DapperTypeHandlers
{
    private static readonly object Lock = new();
    private static bool _platformDefaultsRegistered;

    /// <summary>
    /// Runs <paramref name="configure"/> against a fresh <see cref="DapperTypeHandlerBuilder"/> and
    /// applies every registration it collected, under one process-wide lock alongside this
    /// platform's own (currently empty) set of default handlers, and sets
    /// <see cref="global::Dapper.DefaultTypeMap.MatchNamesWithUnderscores"/> to
    /// <paramref name="enableSnakeCaseMapping"/>.
    /// </summary>
    /// <param name="configure">
    /// Optional. Registers one or more caller-supplied type handlers — see
    /// <see cref="DapperTypeHandlerBuilder.AddTypeHandler{T}(Dapper.SqlMapper.ITypeHandler)"/>.
    /// </param>
    /// <param name="enableSnakeCaseMapping">
    /// Whether Dapper's default column-to-property mapper strips underscores when matching a column
    /// name to a property name (e.g. a <c>tenant_id</c> column binds to a <c>TenantId</c> property
    /// with no <c>AS "TenantId"</c> alias needed) — <see cref="global::Dapper.DefaultTypeMap.MatchNamesWithUnderscores"/>.
    /// Defaults to <see langword="true"/>, matching this platform's snake_case naming convention
    /// (<c>SharedKernel.Persistence.PostgreSQL</c>'s <c>SnakeCaseNamingConvention</c>) so a raw-SQL
    /// query written against the SAME schema an EF Core context maps needs no manual aliasing.
    /// </param>
    /// <remarks>
    /// <para>
    /// Safe to call more than once, including with different <paramref name="configure"/> delegates
    /// each time (e.g. from independent composition-root modules) — every call's type-handler
    /// registrations are applied; nothing is skipped as "already registered" the way a naive
    /// idempotency flag would.
    /// </para>
    /// <para>
    /// <strong><see cref="global::Dapper.DefaultTypeMap.MatchNamesWithUnderscores"/> is a single, process-wide
    /// static <see langword="bool"/> — never per-service, per-connection, or per-call.</strong> Every
    /// call to <see cref="Apply"/> (directly, or via <c>AddSharedKernelDapper</c>) OVERWRITES it, so
    /// whichever call runs LAST in the process wins, regardless of which module made it or what value
    /// an earlier call used. A single service process should decide this once, consistently, across
    /// every <c>AddSharedKernelDapper(...)</c> call site it has — passing
    /// <c>enableSnakeCaseMapping: false</c> from one module while another keeps the default
    /// <see langword="true"/> produces genuinely nondeterministic behavior depending on call order,
    /// not a per-module override.
    /// </para>
    /// </remarks>
    public static void Apply(Action<DapperTypeHandlerBuilder>? configure = null, bool enableSnakeCaseMapping = true)
    {
        lock (Lock)
        {
            global::Dapper.DefaultTypeMap.MatchNamesWithUnderscores = enableSnakeCaseMapping;

            if (!_platformDefaultsRegistered)
            {
                RegisterPlatformDefaults();
                _platformDefaultsRegistered = true;
            }

            if (configure is null)
                return;

            var builder = new DapperTypeHandlerBuilder();
            configure(builder);
            builder.Apply();
        }
    }

    /// <summary>
    /// Registers this platform's own type handlers exactly once per process. Safe to call multiple
    /// times — idempotent.
    /// </summary>
    /// <remarks>
    /// Kept for source compatibility with any existing direct caller; <see cref="Apply"/> (via
    /// <c>AddSharedKernelDapper</c>) is the supported entry point going forward.
    /// </remarks>
    public static void Register() => Apply();

    // Platform-wide handlers registered unconditionally, once per process. Service-specific
    // StronglyTypedId and SmartEnum handlers are registered by the consuming service via
    // DapperTypeHandlerBuilder, never here — registering them here would couple the platform to
    // service-specific types.
    private static void RegisterPlatformDefaults()
    {
        // As of Dapper 2.x with Npgsql, DateTimeOffset round-trips correctly with no custom handler
        // — nothing to register here today. This method exists as the one, documented place a
        // future genuinely platform-wide handler (applicable to every consuming service, not tied to
        // one service's domain types) would be added.
    }
}
