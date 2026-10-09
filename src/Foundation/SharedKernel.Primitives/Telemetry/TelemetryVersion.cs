using System.Reflection;

namespace SharedKernel.Primitives.Telemetry;

/// <summary>
/// Resolves the version string a package reports to telemetry: the <c>ActivitySource</c> and <c>Meter</c> version
/// and the <c>service.version</c> resource attribute.
/// </summary>
/// <remarks>
/// <para>
/// <b>Source of the value.</b> The <c>AssemblyInformationalVersion</c> as stamped by MinVer, build metadata kept
/// (for example <c>1.4.0+3f2a9c1</c>); when the attribute is absent, the assembly version; when neither carries a
/// version, <see cref="Unknown"/>. It never returns <see langword="null"/> and never throws for a loaded assembly.
/// </para>
/// <para>
/// <b>No cache.</b> Hold the result in a <see langword="static"/> <see langword="readonly"/> field of the type that
/// owns the <c>ActivitySource</c>:
/// <code>
/// internal static readonly ActivitySource Source = new("SharedKernel.Example", TelemetryVersion.Of(typeof(Marker).Assembly));
/// </code>
/// </para>
/// <para>
/// Reading an assembly-level attribute is trim- and AOT-safe; this is the only reflection the helper performs.
/// </para>
/// </remarks>
public static class TelemetryVersion
{
    /// <summary>The version reported when an assembly carries none (<c>"0.0.0"</c>).</summary>
    public const string Unknown = "0.0.0";

    /// <summary>Gets the telemetry version of <paramref name="assembly"/>.</summary>
    /// <param name="assembly">The assembly that owns the instrumentation.</param>
    /// <returns>
    /// The informational version, else the assembly version, else <see cref="Unknown"/>. Never <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is <see langword="null"/>.</exception>
    public static string Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        try
        {
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational;

            var version = assembly.GetName().Version;
            if (version is not null && version != new Version(0, 0, 0, 0))
                return version.ToString();
        }
        catch (Exception)
        {
            // Telemetry metadata must never fail startup; fall through to Unknown.
        }

        return Unknown;
    }
}
