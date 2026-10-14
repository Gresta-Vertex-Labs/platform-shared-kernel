namespace SharedKernel.Persistence.Abstractions.Diagnostics;

/// <summary>
/// The telemetry identity of every persistence package: one <c>ActivitySource</c> and one <c>Meter</c>, both named
/// <see cref="Name"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddServiceDefaults</c> subscribes to every <c>SharedKernel.*</c> source and meter by wildcard, so a host needs no
/// registration for them; a host built without it adds <c>AddSource(PersistenceTelemetry.Name)</c> and
/// <c>AddMeter(PersistenceTelemetry.Name)</c>. The EF Core, auditing and field-encryption packages emit through it;
/// Dapper and Npgsql emit nothing of their own (Npgsql's <c>"Npgsql"</c> source traces the commands).
/// </para>
/// <para>
/// Instruments are named <c>sharedkernel.persistence.*</c>, carry a unit, and measure durations in seconds. A tag never
/// carries a tenant, an actor, a key or a value.
/// </para>
/// </remarks>
public static class PersistenceTelemetry
{
    /// <summary>The name of the persistence <c>ActivitySource</c> and <c>Meter</c> (<c>SharedKernel.Persistence</c>).</summary>
    public const string Name = "SharedKernel.Persistence";
}
